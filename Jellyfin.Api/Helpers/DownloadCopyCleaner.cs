using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.Naming.Common;
using Jellyfin.Extensions;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Api.Helpers
{
    /// <summary>
    /// Deletes a video's transcoded download copies when it leaves the library.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An item's id comes from its path, so a rename, a move or a removed folder or library reaches
    /// <see cref="ILibraryManager.ItemRemoved"/> once per video. A file changed in place keeps its id:
    /// its sidecar marks the copies stale instead.
    /// </para>
    /// <para>
    /// Extras are removed with their owner and raise no event, so the copies in the owner's extras
    /// folders are deleted once their items are gone. A suffix extra beside its film is not searched for.
    /// </para>
    /// <para>
    /// Only the removed version's files are deleted, since one folder holds every version's copies.
    /// Runs even when the feature is off, as copies made while it was on remain.
    /// </para>
    /// </remarks>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="serverConfigurationManager">Instance of the <see cref="IServerConfigurationManager"/> interface.</param>
    /// <param name="downloadTranscoder">Instance of <see cref="DownloadTranscoder"/>.</param>
    /// <param name="namingOptions">Instance of <see cref="NamingOptions"/>, for the extras folder names.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{DownloadCopyCleaner}"/> interface.</param>
    public sealed class DownloadCopyCleaner(
        ILibraryManager libraryManager,
        IServerConfigurationManager serverConfigurationManager,
        DownloadTranscoder downloadTranscoder,
        NamingOptions namingOptions,
        ILogger<DownloadCopyCleaner> logger) : IHostedService
    {
        /// <inheritdoc />
        public Task StartAsync(CancellationToken cancellationToken)
        {
            libraryManager.ItemRemoved += OnItemRemoved;

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken)
        {
            libraryManager.ItemRemoved -= OnItemRemoved;

            return Task.CompletedTask;
        }

        private void OnItemRemoved(object? sender, ItemChangeEventArgs e)
        {
            if (e.Item is not Video video || !video.IsFileProtocol || string.IsNullOrEmpty(video.Path))
            {
                return;
            }

            try
            {
                downloadTranscoder.Abandon(video.Id);
                DeleteCopies(video.Id, video.Path);
            }
            catch (Exception ex)
            {
                // Never let a clean-up failure interrupt the library scan that raised the event.
                logger.LogError(ex, "Error deleting the download copies of {Path}", video.Path);
            }
        }

        /// <summary>
        /// Deletes a version's copies and sidecars from every location, and those of its extras, then
        /// any folders that leaves empty.
        /// </summary>
        /// <remarks>
        /// The library may already be gone, so the copies' folder is searched for among the
        /// candidates rather than worked out: a few existence checks per location.
        /// </remarks>
        private void DeleteCopies(Guid versionId, string sourcePath)
        {
            var options = serverConfigurationManager.GetConfiguration<DownloadOptions>(DownloadConfigurationStore.StoreKey);
            var locations = DownloadStorage.GetLocations(options, serverConfigurationManager.ApplicationPaths.DataPath);

            foreach (var location in locations)
            {
                foreach (var candidate in DownloadStorage.GetCandidateFolders(sourcePath))
                {
                    var folder = Path.Combine(location, candidate);
                    if (!Directory.Exists(folder))
                    {
                        continue;
                    }

                    var deleted = DeleteCopiesIn(folder, id => id.Equals(versionId));

                    foreach (var extrasFolder in Directory.EnumerateDirectories(folder)
                        .Where(sub => namingOptions.AllExtrasTypesFolderNames.ContainsKey(Path.GetFileName(sub)))
                        .ToList())
                    {
                        var existing = GetExistingIds(extrasFolder);
                        if (DeleteCopiesIn(extrasFolder, id => !existing.Contains(id)))
                        {
                            // Stops at the version's own folder while it still holds anything.
                            DeleteEmptyFolders(extrasFolder, location);
                            deleted = true;
                        }
                    }

                    // An extras folder left empty may already have taken this one with it.
                    if (deleted && Directory.Exists(folder))
                    {
                        DeleteEmptyFolders(folder, location);
                    }
                }
            }
        }

        /// <summary>
        /// Gets which of the versions with copies in a folder are still in the library.
        /// </summary>
        /// <remarks>
        /// Query the database, not <see cref="ILibraryManager.GetItemById"/>: which searches the
        /// cache first, and removing an item evicts it and its folder children but not the extras
        /// deleted with it, so a removed extra is still found there.
        /// </remarks>
        private HashSet<Guid> GetExistingIds(string folder)
        {
            Guid[] ids = [.. Directory.EnumerateFiles(folder)
                .Select(file => DownloadStorage.TryGetVersionId(file, out var id) ? id : Guid.Empty)
                .Where(id => !id.IsEmpty())
                .Distinct()];

            return ids.Length == 0
                ? []
                : [.. libraryManager.GetItemIds(new InternalItemsQuery { ItemIds = ids, IncludeOwnedItems = true })];
        }

        /// <summary>
        /// Deletes the copies and sidecars in one folder whose version is chosen, stopping any of
        /// those versions copies still being made.
        /// </summary>
        /// <returns><c>true</c> if any file was deleted.</returns>
        private bool DeleteCopiesIn(string folder, Func<Guid, bool> isRemoved)
        {
            var deleted = false;
            foreach (var file in Directory.GetFiles(folder))
            {
                if (!DownloadStorage.TryGetVersionId(file, out var id) || !isRemoved(id))
                {
                    continue;
                }

                downloadTranscoder.Abandon(id);

                try
                {
                    File.Delete(file);
                    deleted = true;
                    logger.LogInformation("Deleted download copy file {Path}: its item was removed", file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // On Windows a reader still downloading the copy stops the delete.
                    logger.LogWarning(ex, "Could not delete download copy file {Path}", file);
                }
            }

            return deleted;
        }

        /// <summary>
        /// Deletes a folder if it is empty, then each parent in turn that is left empty, stopping at
        /// the location itself.
        /// </summary>
        private void DeleteEmptyFolders(string folder, string location)
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(location));
            for (var current = Path.GetFullPath(folder);
                current.Length > root.Length && current.StartsWith(root, StringComparison.OrdinalIgnoreCase);
                current = Path.GetDirectoryName(current)!)
            {
                try
                {
                    if (Directory.EnumerateFileSystemEntries(current).Any())
                    {
                        return;
                    }

                    Directory.Delete(current);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(ex, "Could not delete empty download folder {Path}", current);
                    return;
                }
            }
        }
    }
}
