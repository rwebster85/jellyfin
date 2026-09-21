using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Caching.Memory;

namespace Jellyfin.Api.Helpers
{
    /// <summary>
    /// Describes a download version by probing it, so the server can hand over a rendition and its
    /// own <see cref="MediaSourceInfo"/> rather than the source's.
    /// </summary>
    public static class DownloadProbeHelper
    {
        /// <summary>
        /// How long an unused probe is kept. The key already covers the file changing; this only
        /// bounds how much is held for files nobody is downloading any more.
        /// </summary>
        private static readonly TimeSpan _cacheDuration = TimeSpan.FromHours(6);

        /// <summary>
        /// Probes a download version and returns what it actually contains. Results are cached against
        /// the file's path, length and write time, so a replaced or re-made file probes again by itself.
        /// </summary>
        /// <param name="mediaEncoder">Instance of the <see cref="IMediaEncoder"/> interface.</param>
        /// <param name="cache">Instance of the <see cref="IMemoryCache"/> interface.</param>
        /// <param name="itemId">The id of the item the download version belongs to.</param>
        /// <param name="path">The path of the download version.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The probed <see cref="MediaSourceInfo"/>, or <c>null</c> if the file has gone.</returns>
        public static async Task<MediaSourceInfo?> ProbeAsync(
            IMediaEncoder mediaEncoder,
            IMemoryCache cache,
            Guid itemId,
            string path,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(mediaEncoder);
            ArgumentNullException.ThrowIfNull(cache);

            var file = new FileInfo(path);
            if (!file.Exists)
            {
                return null;
            }

            // The item id is part of the key because alternate versions share one download version, and each
            // gets a source describing itself. It costs a probe per version, once.
            var key = string.Create(
                CultureInfo.InvariantCulture,
                $"downloadprobe:{itemId:N}:{file.FullName}:{file.Length}:{file.LastWriteTimeUtc.Ticks}");

            if (cache.TryGetValue(key, out MediaSourceInfo? cached) && cached is not null)
            {
                return cached;
            }

            var info = await mediaEncoder.GetMediaInfo(
                new MediaInfoRequest
                {
                    MediaType = DlnaProfileType.Video,
                    ExtractChapters = false,
                    MediaSource = new MediaSourceInfo
                    {
                        Path = file.FullName,
                        Protocol = MediaProtocol.File
                    }
                },
                cancellationToken).ConfigureAwait(false);

            info.Id = itemId.ToString("N", CultureInfo.InvariantCulture);
            info.Path = file.FullName;
            info.Protocol = MediaProtocol.File;
            info.IsRemote = false;

            // The probe's Name comes from the container's title tag, which is usually absent and is stale
            // when it isn't. The filename is what the file is.
            info.Name = Path.GetFileNameWithoutExtension(file.FullName);
            info.Size ??= file.Length;

            cache.Set(key, (MediaSourceInfo)info, _cacheDuration);

            return info;
        }
    }
}
