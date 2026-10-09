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
    /// A copy lives at <c>&lt;location&gt;/&lt;id[..2]&gt;/&lt;versionId&gt;/&lt;tierId&gt;.mkv</c>,
    /// with its sidecar beside it as <c>.json</c>: trickplay's layout, keyed by the id of the version
    /// copied, so each version of a film has its own copies.
    /// <para>
    /// Built from ids alone, never from the source's names, so a path is always valid on the
    /// location's filesystem and always the same length. Only a change to the source's path moves a
    /// copy, and that already makes the source a new item.
    /// </para>
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
        /// Gets the folder a version's copies go in, relative to a location: trickplay's layout, the
        /// first two characters of the version's id, then the id.
        /// </summary>
        /// <param name="versionId">The version's id.</param>
        /// <returns>The folder, such as <c>a1/a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d</c>.</returns>
        public static string GetVersionFolder(Guid versionId)
        {
            var id = versionId.ToString("D", CultureInfo.InvariantCulture);

            return Path.Join(id[..2], id);
        }

        /// <summary>
        /// Gets a copy's path relative to a location: the version's folder, then the tier's id as the
        /// file name.
        /// </summary>
        /// <param name="versionId">The id of the version the copy is made from.</param>
        /// <param name="tierId">The tier's id.</param>
        /// <returns>The relative path.</returns>
        /// <remarks>
        /// Built from ids alone, so it is always valid on the location's filesystem and always the same
        /// length, whatever the source is called.
        /// </remarks>
        public static string GetRelativePath(Guid versionId, string tierId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tierId);

            return Path.Join(GetVersionFolder(versionId), tierId + Extension);
        }

        /// <summary>
        /// Gets the path of a copy's sidecar.
        /// </summary>
        /// <param name="copyPath">The copy's path.</param>
        /// <returns>The sidecar's path.</returns>
        public static string GetSidecarPath(string copyPath) => Path.ChangeExtension(copyPath, SidecarExtension);

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
        /// Chooses where a new copy is written: the first location with space for it.
        /// </summary>
        /// <param name="locations">The locations, in order of preference.</param>
        /// <param name="requiredBytes">The space the copy is expected to need, margin included.</param>
        /// <param name="getFreeSpace">Gets a location's free space in bytes, or a negative number if
        /// it is unknown.</param>
        /// <returns>The chosen location, or <c>null</c> if none has space.</returns>
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
