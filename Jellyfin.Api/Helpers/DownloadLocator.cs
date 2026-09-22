using System;
using System.Collections.Generic;
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
        ///
        /// Tiers are opt-in. A file whose name ends with a suffix the administrator defined joins
        /// the tier system - <c>&lt;stem&gt; - High</c>, if that is what they called one; a folder
        /// holding one plainly-named video is served as-is, so an admin who does not care about two
        /// sizes never has to learn the suffix. That untiered file is the last thing tried, after
        /// every tier including ones the admin has not enabled, because the only remaining
        /// alternative is the item's own file.
        /// </remarks>
        /// <param name="options">The download options.</param>
        /// <param name="path">The path of the item being downloaded.</param>
        /// <param name="preferredTier">The tier the user asked for, or <c>null</c> for the default.</param>
        /// <returns>The path of the download version, or <c>null</c> if there isn't one.</returns>
        public static string? FindDownloadVersion(DownloadOptions options, string? path, string? preferredTier = null)
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
                var match = FindInLocations(options.Locations, folderName, stem, suffix);
                if (match is not null)
                {
                    return match;
                }
            }

            return FindUntieredInLocations(options.Locations, folderName);
        }

        private static string? FindInLocations(string[] locations, string folderName, string stem, string suffixName)
        {
            var suffix = " - " + suffixName;
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

                var versions = EnumerateVideos(folder)
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
        /// Finds the item folder's single video whatever it is called, which is how an admin who
        /// does not want to think about tiers puts one file in a folder and has it served.
        /// </summary>
        /// <remarks>
        /// This runs on filename alone, so <strong>any unrecognised suffix is treated as
        /// untiered</strong>. Only the suffixes the administrator defined are tiers;
        /// <c>Film - Low.mkv</c> or a typo like <c>Film - Hihg.mkv</c> matches no tier and arrives
        /// here, where it is served like any other single file. Forgiving on purpose - the admin
        /// plainly meant that file to be used.
        ///
        /// The cost of that forgiveness shows up only alongside a second video: the typo still
        /// matches no tier, two videos means this refuses to choose, and the item falls through to
        /// its own file with nothing naming what went wrong.
        ///
        /// Refusing to choose is the same instinct as the tiered search. With nothing saying which
        /// file is wanted, guessing hands somebody the wrong film, which is worse than falling
        /// through to the original.
        /// </remarks>
        private static string? FindUntieredInLocations(string[] locations, string folderName)
        {
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

                var videos = EnumerateVideos(folder).ToList();
                if (videos.Count == 1)
                {
                    return videos[0];
                }
            }

            return null;
        }

        private static IEnumerable<string> EnumerateVideos(string folder)
            => Directory.EnumerateFiles(folder)
                .Where(file => MimeTypes.GetMimeType(file).StartsWith("video/", StringComparison.OrdinalIgnoreCase));
    }
}
