using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MediaBrowser.Model.Configuration;

namespace Jellyfin.Api.Helpers
{
    /// <summary>
    /// Where transcoded download copies are kept: the folders, and a copy's path inside them.
    /// </summary>
    /// <remarks>
    /// A copy lives at <c>&lt;location&gt;/&lt;library&gt;/&lt;title&gt;/&lt;versionId&gt;.&lt;tierId&gt;.mkv</c>,
    /// for example <c>Movies/The Northman (2022)/a1b2...e5f6.8d3a...4f50.mkv</c>, with its sidecar
    /// beside it as <c>.json</c>. The library and title folders let an admin find a film's copies;
    /// the file name alone says which version and tier a copy is, so each version of a film has its
    /// own copies.
    /// </remarks>
    public static class DownloadStorage
    {
        /// <summary>
        /// The folder under the server's data path used when no locations are set.
        /// </summary>
        public const string DefaultFolderName = "downloads";

        /// <summary>
        /// The container every transcoded copy is written in.
        /// </summary>
        public const string Extension = ".mkv";

        /// <summary>
        /// The extension of the sidecar written beside a finished copy.
        /// </summary>
        public const string SidecarExtension = ".json";

        /// <summary>
        /// Gets the folders copies are kept in, in the admin's order of preference.
        /// </summary>
        /// <param name="options">The download options.</param>
        /// <param name="dataPath">The server's data path, for the default folder.</param>
        /// <returns>The admin's locations, or the default folder when none are set.</returns>
        public static IReadOnlyList<string> GetLocations(DownloadOptions options, string dataPath)
        {
            ArgumentNullException.ThrowIfNull(options);

            // A hand-edited file can hold blanks or the same folder twice.
            string[] locations = [.. (options.TranscodeLocations ?? [])
                .Where(location => !string.IsNullOrWhiteSpace(location))
                .Select(location => location.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)];

            return locations.Length > 0 ? locations : [Path.Combine(dataPath, DefaultFolderName)];
        }

        /// <summary>
        /// Gets a copy's path relative to a location.
        /// </summary>
        /// <param name="libraryName">The library's name, already a valid folder name.</param>
        /// <param name="titleFolder">The film's folder name, already a valid folder name.</param>
        /// <param name="versionId">The id of the version the copy is made from.</param>
        /// <param name="tierId">The tier's id.</param>
        /// <returns>The relative path.</returns>
        public static string GetRelativePath(string libraryName, string titleFolder, Guid versionId, string tierId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(libraryName);
            ArgumentException.ThrowIfNullOrWhiteSpace(titleFolder);
            ArgumentException.ThrowIfNullOrWhiteSpace(tierId);

            var fileName = versionId.ToString("N", CultureInfo.InvariantCulture) + "." + tierId + Extension;

            return Path.Combine(libraryName, titleFolder, fileName);
        }

        /// <summary>
        /// Gets the path of a copy's sidecar.
        /// </summary>
        /// <param name="copyPath">The copy's path.</param>
        /// <returns>The sidecar's path.</returns>
        public static string GetSidecarPath(string copyPath)
            => Path.ChangeExtension(copyPath, SidecarExtension);

        /// <summary>
        /// Finds a finished copy in any location. A copy is finished once its sidecar exists, since
        /// the sidecar is written last.
        /// </summary>
        /// <param name="locations">The locations, in order of preference.</param>
        /// <param name="relativePath">The copy's relative path.</param>
        /// <returns>The first finished copy's full path, or <c>null</c> if there is none.</returns>
        public static string? FindFinished(IEnumerable<string> locations, string relativePath)
        {
            ArgumentNullException.ThrowIfNull(locations);

            foreach (var location in locations)
            {
                var path = Path.Combine(location, relativePath);
                if (File.Exists(path) && File.Exists(GetSidecarPath(path)))
                {
                    return path;
                }
            }

            return null;
        }

        /// <summary>
        /// Chooses where a new copy is written: the first location with room for it.
        /// </summary>
        /// <param name="locations">The locations, in order of preference.</param>
        /// <param name="requiredBytes">The space the copy is expected to need, margin included.</param>
        /// <param name="getFreeSpace">Gets a location's free space in bytes, or a negative number if
        /// it is unknown.</param>
        /// <returns>The chosen location, or <c>null</c> if none has room.</returns>
        /// <remarks>
        /// A location whose free space is unknown is taken: refusing it would stop every download on
        /// a filesystem that does not report its space.
        /// </remarks>
        public static string? ChooseLocation(IEnumerable<string> locations, long requiredBytes, Func<string, long> getFreeSpace)
        {
            ArgumentNullException.ThrowIfNull(locations);
            ArgumentNullException.ThrowIfNull(getFreeSpace);

            foreach (var location in locations)
            {
                var freeSpace = getFreeSpace(location);
                if (freeSpace < 0 || freeSpace >= requiredBytes)
                {
                    return location;
                }
            }

            return null;
        }
    }
}
