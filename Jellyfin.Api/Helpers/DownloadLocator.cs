using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Api.Helpers
{
    /// <summary>
    /// Locates pre-made download versions of a media file.
    /// </summary>
    public static class DownloadLocator
    {
        /// <summary>
        /// Finds a download version of a file in the configured locations, looked up by the name of the
        /// folder the file lives in. Prefers a version named after the file itself, and otherwise accepts
        /// the folder's only version.
        /// </summary>
        /// <remarks>
        /// Each tier is looked for in every location before the next tier is tried, so the user's
        /// choice beats location order. A folder's only video, whatever its name, is tried last.
        /// </remarks>
        /// <example>
        /// Downloading <c>/movies/Up (2009)/Up (2009) - BluRay 1080p.mkv</c> as a user on
        /// <c>High</c>, with <c>Standard</c> the other tier. The download folder is
        /// <c>&lt;location&gt;/Up (2009)/</c>:
        /// <code>
        /// 1. Up (2009) - BluRay 1080p - High.mkv      served: named after the item's own file
        ///    Up (2009) - WEB-DL 2160p - High.mkv
        ///
        /// 2. Up (2009) - WEB-DL 2160p - High.mkv      served: the only High file
        ///
        /// 3. Up (2009) - WEB-DL 2160p - High.mkv      nothing: two High files, neither named after
        ///    Up (2009) - DV 2160p - High.mkv          the item's own, so it will not guess
        ///
        /// 4. Up (2009) - BluRay 1080p - Standard.mkv  served: no High file, so the next tier
        ///
        /// 5. portable copy.mkv                        served: the folder's only video, no tier needed
        ///
        /// 6. one.mkv                                  nothing: two videos with no tier suffix
        ///    two.mkv
        /// </code>
        /// With two locations, a <c>High</c> file in the second beats a <c>Standard</c> file in the
        /// first. Nothing found means the item's own file is served, or 404 on the optimised routes.
        /// </example>
        /// <param name="options">The download options.</param>
        /// <param name="path">The path of the item being downloaded.</param>
        /// <param name="preferredTier">The tier the user asked for, or <c>null</c> for the default.</param>
        /// <param name="logger">Where to report a location that cannot be read, or <c>null</c>.</param>
        /// <returns>The path of the download version, or <c>null</c> if there isn't one.</returns>
        public static string? FindDownloadVersion(DownloadOptions options, string? path, string? preferredTier = null, ILogger? logger = null)
        {
            ArgumentNullException.ThrowIfNull(options);

            var folderName = Path.GetFileName(Path.GetDirectoryName(path));
            if (string.IsNullOrEmpty(folderName))
            {
                return null;
            }

            var stem = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrEmpty(stem))
            {
                return null;
            }

            foreach (var suffix in DownloadTiers.GetSearchOrder(options, preferredTier))
            {
                var match = FindInLocations(options.Locations, folderName, stem, suffix, logger);
                if (match is not null)
                {
                    return match;
                }
            }

            return FindUntieredInLocations(options.Locations, folderName, logger);
        }

        private static string? FindInLocations(string[] locations, string folderName, string stem, string suffixName, ILogger? logger)
        {
            var suffix = " - " + suffixName;
            var preferredName = stem + suffix;

            foreach (var videos in VideosInEachLocation(locations, folderName, logger))
            {
                var versions = videos
                    .Where(file => Path.GetFileNameWithoutExtension(file).EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var match = versions.Find(file => string.Equals(Path.GetFileNameWithoutExtension(file), preferredName, StringComparison.OrdinalIgnoreCase))
                    ?? (versions.Count == 1 ? versions[0] : null);

                if (match is not null)
                {
                    return match;
                }
            }

            return null;
        }

        /// <summary>
        /// Finds the item folder's single video whatever it is called, so tiers are optional: one
        /// plainly-named file in a folder is served as it is.
        /// </summary>
        /// <remarks>
        /// A file with an unrecognised suffix (<c>Film - Low.mkv</c>, or a typo) counts as having no tier.
        /// With two videos in the folder it refuses to guess - the wrong film is worse than the
        /// original.
        /// </remarks>
        private static string? FindUntieredInLocations(string[] locations, string folderName, ILogger? logger)
        {
            foreach (var videos in VideosInEachLocation(locations, folderName, logger))
            {
                if (videos.Count == 1)
                {
                    return videos[0];
                }
            }

            return null;
        }

        /// <summary>
        /// Lists the videos in the item's folder in each location, in order, skipping a location
        /// without the folder.
        /// </summary>
        /// <remarks>
        /// A folder that exists but cannot be read - permissions, or a network mount that has gone
        /// away - is logged and skipped rather than thrown. Throwing would fail the plain download,
        /// which should fall back to the item's own file instead.
        /// </remarks>
        private static IEnumerable<List<string>> VideosInEachLocation(string[] locations, string folderName, ILogger? logger)
        {
            foreach (var location in locations)
            {
                if (string.IsNullOrWhiteSpace(location))
                {
                    continue;
                }

                var folder = Path.Combine(location, folderName);
                List<string> videos;

                try
                {
                    if (!Directory.Exists(folder))
                    {
                        continue;
                    }

                    videos = Directory.EnumerateFiles(folder)
                        .Where(file => MimeTypes.GetMimeType(file).StartsWith("video/", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger?.LogWarning(ex, "Skipping download location folder {Folder}, which could not be read", folder);
                    continue;
                }

                yield return videos;
            }
        }
    }
}
