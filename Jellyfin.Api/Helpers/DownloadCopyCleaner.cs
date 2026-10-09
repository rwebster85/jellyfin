using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    /// <see cref="ILibraryManager.ItemRemoved"/> once per video, and its copies' folder is deleted. A
    /// file changed in place keeps its id: its sidecar marks the copies stale instead.
    /// </para>
    /// <para>
    /// Extras removed with their owner raise no event: the database deletes them with it. So each
    /// removal also schedules a sweep, which deletes every copies folder whose id is no longer in the
    /// library. The sweep waits for removals to stop, so a removed library is swept once.
    /// </para>
    /// <para>
    /// Runs even when the feature is off, as copies made while it was on remain.
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
        ILogger<DownloadCopyCleaner> logger) : IHostedService, IDisposable
    {
        /// <summary>
        /// How long the sweep waits after the last removal.
        /// </summary>
        private static readonly TimeSpan _sweepDelay = TimeSpan.FromSeconds(30);

        private readonly Lock _sweepLock = new();

        private Timer? _sweepTimer;

        /// <inheritdoc />
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _sweepTimer = new Timer(_ => Sweep(), null, Timeout.Infinite, Timeout.Infinite);
            libraryManager.ItemRemoved += OnItemRemoved;

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken)
        {
            libraryManager.ItemRemoved -= OnItemRemoved;
            _sweepTimer?.Change(Timeout.Infinite, Timeout.Infinite);

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _sweepTimer?.Dispose();
        }

        private void OnItemRemoved(object? sender, ItemChangeEventArgs e)
        {
            if (e.Item is not Video video || !video.IsFileProtocol)
            {
                return;
            }

            try
            {
                downloadTranscoder.Abandon(video.Id);

                var versionFolder = DownloadStorage.GetVersionFolder(video.Id);
                foreach (var location in GetLocations())
                {
                    DeleteVersionFolder(Path.Combine(location, versionFolder));
                }

                // An extra owns nothing, so removing one leaves nothing for the sweep.
                if (video.ExtraType is null)
                {
                    _sweepTimer?.Change(_sweepDelay, Timeout.InfiniteTimeSpan);
                }
            }
            catch (Exception ex)
            {
                // Never let a clean-up failure interrupt the library scan that raised the event.
                logger.LogError(ex, "Error deleting the download copies of {Path}", video.Path);
            }
        }

        /// <summary>
        /// Deletes the copies folder of every version no longer in the library, in every location.
        /// </summary>
        /// <remarks>
        /// Query the database, not <see cref="ILibraryManager.GetItemById"/>: which searches the
        /// cache first, and removing an item evicts it and its folder children but not the extras
        /// deleted with it, so a removed extra is still found there.
        /// </remarks>
        private void Sweep()
        {
            lock (_sweepLock)
            {
                try
                {
                    var folders = GetLocations().SelectMany(FindVersionFolders).ToList();
                    if (folders.Count == 0)
                    {
                        return;
                    }

                    Guid[] ids = [.. folders.Select(folder => folder.Id).Distinct()];
                    var existing = libraryManager.GetItemIds(new InternalItemsQuery { ItemIds = ids, IncludeOwnedItems = true }).ToHashSet();

                    foreach (var (path, id) in folders.Where(folder => !existing.Contains(folder.Id)))
                    {
                        downloadTranscoder.Abandon(id);
                        DeleteVersionFolder(path);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error sweeping download copies");
                }
            }
        }

        private IReadOnlyList<string> GetLocations()
        {
            var options = serverConfigurationManager.GetConfiguration<DownloadOptions>(DownloadConfigurationStore.StoreKey);

            return DownloadStorage.GetLocations(options, serverConfigurationManager.ApplicationPaths.DataPath);
        }

        /// <summary>
        /// Finds the copies folders in a location: only those laid out as
        /// <see cref="DownloadStorage.GetVersionFolder"/> makes them, so nothing else an admin keeps
        /// there is touched.
        /// </summary>
        private static IEnumerable<(string Path, Guid Id)> FindVersionFolders(string location)
        {
            if (!Directory.Exists(location))
            {
                yield break;
            }

            foreach (var prefixFolder in Directory.EnumerateDirectories(location))
            {
                foreach (var folder in Directory.EnumerateDirectories(prefixFolder))
                {
                    if (Guid.TryParseExact(Path.GetFileName(folder), "D", out var id)
                        && !id.IsEmpty()
                        && string.Equals(DownloadStorage.GetVersionFolder(id), Path.GetRelativePath(location, folder), StringComparison.OrdinalIgnoreCase))
                    {
                        yield return (folder, id);
                    }
                }
            }
        }

        /// <summary>
        /// Deletes a version's copies folder, then its prefix folder if that is left empty.
        /// </summary>
        private void DeleteVersionFolder(string folder)
        {
            if (!Directory.Exists(folder))
            {
                return;
            }

            try
            {
                Directory.Delete(folder, true);
                logger.LogInformation("Deleted download copies {Path}: their item was removed", folder);

                var prefixFolder = Path.GetDirectoryName(folder)!;
                if (!Directory.EnumerateFileSystemEntries(prefixFolder).Any())
                {
                    Directory.Delete(prefixFolder);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // On Windows a reader still downloading a copy stops the delete: the next sweep retries.
                logger.LogWarning(ex, "Could not delete download copies {Path}", folder);
            }
        }
    }
}
