using System;
using System.IO;
using System.Linq;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Net;

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
        /// The search is quality-major: the user's own tier is looked for across every location before
        /// the next tier is tried anywhere. A tier is a choice somebody made; which location a file
        /// happens to sit in is not, so the tier wins.
        /// </remarks>
        /// <param name="options">The download options.</param>
        /// <param name="path">The path of the item being downloaded.</param>
        /// <param name="preferredQuality">The tier the user asked for, or <c>null</c> for the default.</param>
        /// <returns>The path of the download version, or <c>null</c> if there isn't one.</returns>
        public static string? FindDownloadVersion(DownloadOptions options, string? path, string? preferredQuality = null)
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

            foreach (var quality in DownloadQualities.GetSearchOrder(options, preferredQuality))
            {
                var match = FindInLocations(options.Locations, folderName, stem, quality);
                if (match is not null)
                {
                    return match;
                }
            }

            return null;
        }

        private static string? FindInLocations(string[] locations, string folderName, string stem, string quality)
        {
            var suffix = " - " + quality;
            var preferredName = stem + suffix;

            foreach (var location in locations)
            {
                if (string.IsNullOrWhiteSpace(location))
                {
                    continue;
                }

                var folder = Path.Combine(location, folderName);
                if (!Directory.Exists(folder))
                {
                    continue;
                }

                var versions = Directory.EnumerateFiles(folder)
                    .Where(file => Path.GetFileNameWithoutExtension(file).EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                        && MimeTypes.GetMimeType(file).StartsWith("video/", StringComparison.OrdinalIgnoreCase))
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
    }
}
