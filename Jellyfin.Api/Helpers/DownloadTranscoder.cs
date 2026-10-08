using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Api.Extensions;
using Jellyfin.Extensions.Json;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Streaming;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Api.Helpers
{
    /// <summary>
    /// Makes transcoded download copies: starts the encode, lets later requests join one still
    /// running, and writes the sidecar that marks a copy finished.
    /// </summary>
    /// <remarks>
    /// A copy is encoded exactly as a progressive stream is, through <see cref="StreamingHelpers"/>
    /// and <see cref="EncodingHelper"/>, but as a <see cref="TranscodingJobType.Download"/> job, so it
    /// runs to the end with nobody reading it. Registered as a singleton: it keeps track of the copies
    /// being made across requests.
    /// </remarks>
    /// <param name="transcodeManager">Instance of the <see cref="ITranscodeManager"/> interface.</param>
    /// <param name="encodingHelper">Instance of <see cref="EncodingHelper"/>.</param>
    /// <param name="mediaSourceManager">Instance of the <see cref="IMediaSourceManager"/> interface.</param>
    /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="serverConfigurationManager">Instance of the <see cref="IServerConfigurationManager"/> interface.</param>
    /// <param name="mediaEncoder">Instance of the <see cref="IMediaEncoder"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{DownloadTranscoder}"/> interface.</param>
    public sealed class DownloadTranscoder(
        ITranscodeManager transcodeManager,
        EncodingHelper encodingHelper,
        IMediaSourceManager mediaSourceManager,
        IUserManager userManager,
        ILibraryManager libraryManager,
        IServerConfigurationManager serverConfigurationManager,
        IMediaEncoder mediaEncoder,
        ILogger<DownloadTranscoder> logger)
    {
        /// <summary>
        /// The encode settings every copy is made with, for now: one hardcoded tier. Recorded in the
        /// sidecar, so changing them here makes existing copies stale.
        /// </summary>
        private const string Settings = "mkv h264 1280x720 3000k aac 2ch 192k";

        /// <summary>
        /// The bitrate a copy is expected to average, video and audio together, for estimating its
        /// size. Matches <see cref="Settings"/>.
        /// </summary>
        private const long ExpectedBitRate = 3_000_000 + 192_000;

        /// <summary>
        /// How often a running copy is checked for having finished.
        /// </summary>
        private static readonly TimeSpan _exitPollInterval = TimeSpan.FromSeconds(1);

        /// <summary>
        /// The copies being made, by relative path, which is the same in every location. Kept here
        /// rather than looked up in the transcode manager, whose list keeps finished progressive
        /// jobs: a copy being remade could otherwise be matched to the job that made the old one.
        /// </summary>
        private readonly ConcurrentDictionary<string, TranscodingJob> _running = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Opens a copy that is not finished yet: joins its encode if one is running, or starts one
        /// in the first location with room for it.
        /// </summary>
        /// <param name="item">The version being copied.</param>
        /// <param name="tierId">The tier's id.</param>
        /// <param name="locations">The locations copies are kept in, in order of preference.</param>
        /// <param name="relativePath">The copy's path relative to a location.</param>
        /// <param name="httpContext">The request asking for the copy. Read while the encode is set up,
        /// for the user and the request path, and not kept.</param>
        /// <returns>A stream that follows the copy as it grows, and ends when the encode does.</returns>
        /// <exception cref="IOException">No location has room for the copy.</exception>
        public async Task<Stream> OpenAsync(BaseItem item, string tierId, IReadOnlyList<string> locations, string relativePath, HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(item);
            ArgumentNullException.ThrowIfNull(locations);
            ArgumentNullException.ThrowIfNull(httpContext);

            // Locked on the relative path, so two requests cannot start the same copy in two locations.
            using (await transcodeManager.LockAsync(relativePath, CancellationToken.None).ConfigureAwait(false))
            {
                if (_running.TryGetValue(relativePath, out var running) && !running.HasExited)
                {
                    running.IncrementActiveRequestCount();
                    logger.LogInformation("Joining the download copy being made at {Path}", running.Path);

                    return new ProgressiveFileStream(running.Path!, running, transcodeManager);
                }

                var location = DownloadStorage.ChooseLocation(locations, EstimateBytes(item), GetFreeSpace)
                    ?? throw new IOException($"No download location has room for a copy of {item.Path}");
                var outputPath = Path.Combine(location, relativePath);

                // Stale copies go first, in every location, so none is taken for finished while this
                // one is made. In the chosen location ffmpeg overwrites the copy itself.
                foreach (var stalePath in locations.Select(other => Path.Combine(other, relativePath)))
                {
                    DeleteIfExists(DownloadStorage.GetSidecarPath(stalePath));
                    if (!string.Equals(stalePath, outputPath, StringComparison.OrdinalIgnoreCase))
                    {
                        DeleteIfExists(stalePath);
                    }
                }

                var job = await StartAsync(item, outputPath, httpContext).ConfigureAwait(false);
                _running[relativePath] = job;
                _ = FinishAsync(job, item, tierId, relativePath, outputPath, ReadSource(item));

                return new ProgressiveFileStream(outputPath, job, transcodeManager);
            }
        }

        /// <summary>
        /// Gets a value indicating whether a finished copy was made from the version as it is now,
        /// with the tier's current settings.
        /// </summary>
        /// <param name="copyPath">The copy's path.</param>
        /// <param name="item">The version the copy was made from.</param>
        /// <param name="tierId">The tier's id.</param>
        /// <returns><c>true</c> when the copy can be served; <c>false</c> when it has to be made again.</returns>
        public bool IsCurrent(string copyPath, BaseItem item, string tierId)
        {
            ArgumentNullException.ThrowIfNull(item);

            Sidecar? sidecar;
            try
            {
                sidecar = JsonSerializer.Deserialize<Sidecar>(File.ReadAllBytes(DownloadStorage.GetSidecarPath(copyPath)), JsonDefaults.Options);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                logger.LogWarning(ex, "Could not read the sidecar of download copy {Path}", copyPath);
                return false;
            }

            var source = ReadSource(item);

            return sidecar is not null
                && sidecar.VersionId.Equals(item.Id)
                && string.Equals(sidecar.TierId, tierId, StringComparison.Ordinal)
                && string.Equals(sidecar.Settings, Settings, StringComparison.Ordinal)
                && string.Equals(sidecar.SourcePath, source.Path, StringComparison.Ordinal)
                && sidecar.SourceSize == source.Size
                && sidecar.SourceModifiedUtc == source.ModifiedUtc;
        }

        /// <summary>
        /// Stops every copy of a version still being made, whatever its tier. For a version that has
        /// left the library, its copies would never be served.
        /// </summary>
        /// <param name="versionId">The version's id.</param>
        /// <remarks>
        /// The copy comes off the list first, which is how <see cref="FinishAsync"/> knows to delete
        /// it. Stopping ffmpeg ends the encode cleanly, with an exit code of 0, so that code alone
        /// would mark the cut-short copy finished.
        /// </remarks>
        public void Abandon(Guid versionId)
        {
            var prefix = DownloadStorage.GetVersionPrefix(versionId);
            foreach (var (relativePath, job) in _running)
            {
                if (Path.GetFileName(relativePath).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && _running.TryRemove(new KeyValuePair<string, TranscodingJob>(relativePath, job)))
                {
                    logger.LogInformation("Stopping download copy {Path}: its item was removed", job.Path);
                    job.Stop();
                }
            }
        }

        private static (string Path, long Size, DateTime ModifiedUtc) ReadSource(BaseItem item)
        {
            var file = new FileInfo(item.Path);

            return (item.Path, file.Length, file.LastWriteTimeUtc);
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        /// <summary>
        /// Estimates a copy's size from the item's runtime, with a tenth again as margin.
        /// </summary>
        /// <returns>The estimate in bytes, or 0 when the runtime is unknown.</returns>
        private static long EstimateBytes(BaseItem item)
        {
            var seconds = TimeSpan.FromTicks(item.RunTimeTicks ?? 0).TotalSeconds;

            return (long)(seconds * ExpectedBitRate / 8 * 1.1);
        }

        /// <summary>
        /// Gets the free space of the filesystem a location is on, the way the dashboard's storage
        /// view finds it: the mounted drive whose root is the longest match for the path.
        /// </summary>
        /// <returns>The free space in bytes, or -1 if it cannot be read.</returns>
        private static long GetFreeSpace(string location)
        {
            try
            {
                var path = Path.GetFullPath(location);
                DriveInfo? bestMatch = null;
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (path.StartsWith(drive.RootDirectory.FullName, StringComparison.OrdinalIgnoreCase)
                        && (bestMatch is null || drive.RootDirectory.FullName.Length > bestMatch.RootDirectory.FullName.Length))
                    {
                        bestMatch = drive;
                    }
                }

                return bestMatch?.AvailableFreeSpace ?? -1;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return -1;
            }
        }

        private async Task<TranscodingJob> StartAsync(BaseItem item, string outputPath, HttpContext httpContext)
        {
            // Owned by the job, which cancels it if the copy is killed. Never the request's token:
            // the encode carries on after the client that started it has gone.
            var cancellationTokenSource = new CancellationTokenSource();

            // No DeviceId or PlaySessionId: stopping playback kills every job for the device or
            // session, and this one belongs to neither. No subtitle stream and no user preferences:
            // a copy is the same for everyone who downloads it.
            var request = new VideoRequestDto
            {
                Id = item.Id,
                MediaSourceId = item.Id.ToString("N", CultureInfo.InvariantCulture),
                Container = "mkv",
                VideoCodec = "h264",
                AudioCodec = "aac",
                MaxWidth = 1280,
                MaxHeight = 720,
                VideoBitRate = 3_000_000,
                AudioBitRate = 192_000,
                MaxAudioChannels = 2,
                EnableAutoStreamCopy = true,
                AllowVideoStreamCopy = true,
                AllowAudioStreamCopy = true
            };

            // The stream state stays Progressive, so the encode is exactly a progressive stream's.
            // Not `using`: once ffmpeg starts, the job owns the state and the transcode manager disposes
            // of it when the encode ends. The catch below disposes it if the encode never starts.
            var state = await StreamingHelpers.GetStreamingState(
                    request,
                    httpContext,
                    mediaSourceManager,
                    userManager,
                    libraryManager,
                    serverConfigurationManager,
                    mediaEncoder,
                    encodingHelper,
                    transcodeManager,
                    TranscodingJobType.Progressive,
                    cancellationTokenSource.Token)
                .ConfigureAwait(false);

            state.OutputFilePath = outputPath;

            var encodingOptions = serverConfigurationManager.GetEncodingOptions();
            var arguments = encodingHelper.GetProgressiveVideoFullCommandLine(state, encodingOptions, EncoderPreset.medium);

            logger.LogInformation("Making download copy {Path} from {Source}", outputPath, item.Path);

            try
            {
                return await transcodeManager.StartFfMpeg(
                        state,
                        outputPath,
                        arguments,
                        httpContext.User.GetUserId(),
                        TranscodingJobType.Download,
                        cancellationTokenSource)
                    .ConfigureAwait(false);
            }
            catch
            {
                state.Dispose();
                DeleteIfExists(outputPath);
                throw;
            }
        }

        /// <summary>
        /// Waits for a copy's encode to end, then marks the copy finished or removes what was written.
        /// </summary>
        private async Task FinishAsync(TranscodingJob job, BaseItem item, string tierId, string relativePath, string outputPath, (string Path, long Size, DateTime ModifiedUtc) source)
        {
            try
            {
                while (!job.HasExited)
                {
                    await Task.Delay(_exitPollInterval).ConfigureAwait(false);
                }

                // Under the same lock as OpenAsync, so no request sees the copy between the encode
                // ending and the sidecar being written, and starts it again.
                using (await transcodeManager.LockAsync(relativePath, CancellationToken.None).ConfigureAwait(false))
                {
                    // Already off the list means abandoned - its item was removed while it was made.
                    if (!_running.TryRemove(new KeyValuePair<string, TranscodingJob>(relativePath, job)))
                    {
                        logger.LogInformation("Discarding download copy {Path}: its item was removed", outputPath);
                        DeleteIfExists(outputPath);
                    }
                    else if (job.ExitCode == 0)
                    {
                        var sidecar = new Sidecar(item.Id, tierId, Settings, source.Path, source.Size, source.ModifiedUtc, DateTime.UtcNow);
                        await File.WriteAllBytesAsync(
                                DownloadStorage.GetSidecarPath(outputPath),
                                JsonSerializer.SerializeToUtf8Bytes(sidecar, JsonDefaults.Options))
                            .ConfigureAwait(false);

                        logger.LogInformation("Finished download copy {Path}", outputPath);
                    }
                    else
                    {
                        // A reader still open on the partial copy can stop the delete on Windows. No
                        // sidecar means it is never served, and the next request overwrites it.
                        logger.LogWarning("Download copy {Path} failed with exit code {ExitCode}", outputPath, job.ExitCode);
                        DeleteIfExists(outputPath);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error finishing download copy {Path}", outputPath);
            }
        }

        /// <summary>
        /// What a copy was made from and how, written beside it once it is finished.
        /// </summary>
        /// <param name="VersionId">The id of the version copied.</param>
        /// <param name="TierId">The tier's id.</param>
        /// <param name="Settings">The encode settings used.</param>
        /// <param name="SourcePath">The source file's path.</param>
        /// <param name="SourceSize">The source file's size when the encode started.</param>
        /// <param name="SourceModifiedUtc">The source file's last write time when the encode started.</param>
        /// <param name="CreatedUtc">When the copy was finished.</param>
        private sealed record Sidecar(
            Guid VersionId,
            string TierId,
            string Settings,
            string SourcePath,
            long SourceSize,
            DateTime SourceModifiedUtc,
            DateTime CreatedUtc);
    }
}
