using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    /// Renaming or moving a file, renaming its folder or a parent folder, deleting an extras folder,
    /// and removing a folder or a whole library all reach <see cref="ILibraryManager.ItemRemoved"/>
    /// once per video, since an item's id comes from its path and removing a folder announces every
    /// item beneath it. A file changed in place keeps its id, and its sidecar marks its copies stale.
    /// </para>
    /// <para>
    /// Not core's <c>GetExtractedDataPaths</c>, which deletes whole folders: one folder here holds
    /// every version's copies, so only the removed version's files are deleted. Runs whether or not
    /// the feature is on, since copies made while it was on are still on disk.
    /// </para>
    /// </remarks>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="serverConfigurationManager">Instance of the <see cref="IServerConfigurationManager"/> interface.</param>
    /// <param name="downloadTranscoder">Instance of <see cref="DownloadTranscoder"/>.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{DownloadCopyCleaner}"/> interface.</param>
    public sealed class DownloadCopyCleaner(
        ILibraryManager libraryManager,
        IServerConfigurationManager serverConfigurationManager,
        DownloadTranscoder downloadTranscoder,
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
        /// Deletes a version's copies and sidecars from every location, then any folders that leaves empty.
        /// </summary>
        /// <remarks>
        /// The library may already be gone, so the copies' folder is searched for among the
        /// candidates rather than worked out: a few existence checks per location.
        /// </remarks>
        private void DeleteCopies(Guid versionId, string sourcePath)
        {
            var options = serverConfigurationManager.GetConfiguration<DownloadOptions>(DownloadConfigurationStore.StoreKey);
            var locations = DownloadStorage.GetLocations(options, serverConfigurationManager.ApplicationPaths.DataPath);
            var pattern = DownloadStorage.GetVersionPrefix(versionId) + "*";

            foreach (var location in locations)
            {
                foreach (var candidate in DownloadStorage.GetCandidateFolders(sourcePath))
                {
                    var folder = Path.Combine(location, candidate);
                    if (!Directory.Exists(folder))
                    {
                        continue;
                    }

                    var files = Directory.GetFiles(folder, pattern);
                    foreach (var file in files)
                    {
                        try
                        {
                            File.Delete(file);
                            logger.LogInformation("Deleted download copy file {Path}: its item was removed", file);
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            // On Windows a reader still downloading the copy stops the delete.
                            logger.LogWarning(ex, "Could not delete download copy file {Path}", file);
                        }
                    }

                    if (files.Length > 0)
                    {
                        DeleteEmptyFolders(folder, location);
                    }
                }
            }
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
