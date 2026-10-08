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
    /// A copy lives at <c>&lt;location&gt;/&lt;mirrored folder&gt;/&lt;versionId&gt;.&lt;tierId&gt;.mkv</c>,
    /// with its sidecar beside it as <c>.json</c>. The mirrored folder is the source's own folder on
    /// disk, from its library root's folder name down, so
    /// <c>Movies/The Northman (2022)/a1b2...e5f6.8d3a...4f50.mkv</c> for a film,
    /// <c>TV/Firefly (2002)/Season 01/...</c> for an episode, and <c>.../Featurettes/...</c> for an
    /// extra. The folders let an admin find an item's copies: the file name alone says which version
    /// and tier a copy is, so each version of a film has its own copies.
    /// <para>
    /// Built from names on disk, never from metadata or the library's name, so only a change to the
    /// source's path moves a copy, and that already makes the source a new item. It also means a
    /// copy's folder is always the last few folders of its source's path.
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
        /// Gets the folder a source's copies go in, relative to a location: the library root's folder
        /// name, then the source's folders beneath that root.
        /// </summary>
        /// <param name="libraryRoots">The folders of the source's library.</param>
        /// <param name="sourcePath">The source file's path.</param>
        /// <returns>The mirrored folder, or <c>null</c> if the source is not under any of the roots, or
        /// its root has no folder name, such as <c>/</c>.</returns>
        /// <remarks>
        /// The deepest root containing the source is used, should one library folder sit inside another.
        /// A root at the top of a Windows drive is named after its letter, <c>E:\</c> as <c>E</c>.
        /// </remarks>
        public static string? GetMirroredFolder(IEnumerable<string> libraryRoots, string sourcePath)
        {
            ArgumentNullException.ThrowIfNull(libraryRoots);
            ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

            var sourceFolder = Path.GetDirectoryName(sourcePath);
            if (string.IsNullOrEmpty(sourceFolder))
            {
                return null;
            }

            string? best = null;
            string? bestRoot = null;
            foreach (var root in libraryRoots)
            {
                if (string.IsNullOrWhiteSpace(root))
                {
                    continue;
                }

                // Compares as the platform does, so case matters on Linux and not on Windows.
                var relative = Path.GetRelativePath(root, sourceFolder);
                var isUnderRoot = !Path.IsPathRooted(relative)
                    && relative != ".."
                    && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
                if (isUnderRoot && (bestRoot is null || root.Length > bestRoot.Length))
                {
                    best = relative == "." ? string.Empty : relative;
                    bestRoot = root;
                }
            }

            if (bestRoot is null)
            {
                return null;
            }

            var trimmedRoot = Path.TrimEndingDirectorySeparator(bestRoot);
            var rootName = Path.GetFileName(trimmedRoot);
            if (string.IsNullOrEmpty(rootName))
            {
                rootName = trimmedRoot.Replace(Path.VolumeSeparatorChar.ToString(), string.Empty, StringComparison.Ordinal)
                    .Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }

            return string.IsNullOrEmpty(rootName) ? null : Path.Combine(rootName, best!);
        }

        /// <summary>
        /// Gets a copy's path relative to a location.
        /// </summary>
        /// <param name="mirroredFolder">The source's mirrored folder, from <see cref="GetMirroredFolder"/>.</param>
        /// <param name="versionId">The id of the version the copy is made from.</param>
        /// <param name="tierId">The tier's id.</param>
        /// <returns>The relative path.</returns>
        public static string GetRelativePath(string mirroredFolder, Guid versionId, string tierId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(mirroredFolder);
            ArgumentException.ThrowIfNullOrWhiteSpace(tierId);

            var fileName = versionId.ToString("N", CultureInfo.InvariantCulture) + "." + tierId + Extension;

            return Path.Combine(mirroredFolder, fileName);
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
