using System;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using Jellyfin.Api.Helpers;
using MediaBrowser.Model.Configuration;
using Xunit;

namespace Jellyfin.Api.Tests.Helpers
{
    public sealed class DownloadLocatorTests : IDisposable
    {
        private const string FolderName = "Avengers - Infinity War (2018)";
        private const string SourceName = "Avengers - Infinity War (2018) - BluRay 1080p.mkv";

        private readonly string _root = Path.Combine(Path.GetTempPath(), "jf-download-" + Guid.NewGuid().ToString("N"));

        [Fact]
        public void FindDownloadVersion_PrefersVersionNamedAfterTheSource()
        {
            var location = CreateLocation(
                "Avengers - Infinity War (2018) - BluRay 1080p - High.mkv",
                "Avengers - Infinity War (2018) - IMAX DV WEB-DL 2160p - High.mkv");

            Assert.Equal(
                Path.Combine(location, FolderName, "Avengers - Infinity War (2018) - BluRay 1080p - High.mkv"),
                Find(location));
        }

        [Fact]
        public void FindDownloadVersion_FallsBackToTheOnlyVersionInTheFolder()
        {
            var location = CreateLocation("Avengers - Infinity War (2018) - IMAX DV WEB-DL 2160p - High.mkv");

            Assert.Equal(
                Path.Combine(location, FolderName, "Avengers - Infinity War (2018) - IMAX DV WEB-DL 2160p - High.mkv"),
                Find(location));
        }

        [Fact]
        public void FindDownloadVersion_RefusesToGuessBetweenTwoVersions()
        {
            var location = CreateLocation(
                "Avengers - Infinity War (2018) - IMAX DV WEB-DL 2160p - High.mkv",
                "Avengers - Infinity War (2018) - DV BluRay 2160p - High.mkv");

            Assert.Null(Find(location));
        }

        [Fact]
        public void FindDownloadVersion_StillServesATierTheAdminHasNotEnabled()
        {
            var location = CreateLocation("Avengers - Infinity War (2018) - BluRay 1080p - Standard.mkv");

            // Enabling a tier decides what a user may choose, not what may be served: the only
            // alternative is the item's own, larger, file.
            Assert.Equal(
                Path.Combine(location, FolderName, "Avengers - Infinity War (2018) - BluRay 1080p - Standard.mkv"),
                FindWithTiers(["High"], null, location));
        }

        [Fact]
        public void FindDownloadVersion_PrefersAnEnabledTierOverADisabledOne()
        {
            var location = CreateLocation(
                "Avengers - Infinity War (2018) - BluRay 1080p - High.mkv",
                "Avengers - Infinity War (2018) - BluRay 1080p - Standard.mkv");

            Assert.Equal(
                Path.Combine(location, FolderName, "Avengers - Infinity War (2018) - BluRay 1080p - Standard.mkv"),
                FindWithTiers(["Standard"], null, location));
        }

        [Fact]
        public void FindDownloadVersion_ServesAnUntieredFileWhenItIsTheOnlyOne()
        {
            // Tiers are opt-in: an admin who does not care about two sizes drops one plainly-named
            // file into the folder and it is served.
            var location = CreateLocation("Avengers - Infinity War (2018) - BluRay 1080p.mkv");

            Assert.Equal(
                Path.Combine(location, FolderName, "Avengers - Infinity War (2018) - BluRay 1080p.mkv"),
                Find(location));
        }

        [Fact]
        public void FindDownloadVersion_ServesAnUntieredFileWhateverItIsCalled()
        {
            var location = CreateLocation("portable copy.mkv");

            Assert.Equal(
                Path.Combine(location, FolderName, "portable copy.mkv"),
                Find(location));
        }

        [Fact]
        public void FindDownloadVersion_PrefersATieredFileOverAnUntieredOne()
        {
            var location = CreateLocation(
                "Avengers - Infinity War (2018) - BluRay 1080p - High.mkv",
                "something else entirely.mkv");

            Assert.Equal(
                Path.Combine(location, FolderName, "Avengers - Infinity War (2018) - BluRay 1080p - High.mkv"),
                Find(location));
        }

        [Fact]
        public void FindDownloadVersion_RefusesToGuessBetweenTwoUntieredFiles()
        {
            var location = CreateLocation("one.mkv", "two.mkv");

            // Handing somebody the wrong film is worse than falling through to the original.
            Assert.Null(Find(location));
        }

        [Fact]
        public void FindDownloadVersion_IgnoresNonVideosWhenLookingForAnUntieredFile()
        {
            var location = CreateLocation("portable copy.mkv", "notes.txt", "cover.jpg");

            Assert.Equal(
                Path.Combine(location, FolderName, "portable copy.mkv"),
                Find(location));
        }

        [Fact]
        public void FindDownloadVersion_ServesEitherTierOutOfTheBox()
        {
            // Both tiers are enabled by default, so a Standard file is served without an admin
            // having touched the settings page.
            var location = CreateLocation("Avengers - Infinity War (2018) - BluRay 1080p - Standard.mkv");

            Assert.Equal(
                Path.Combine(location, FolderName, "Avengers - Infinity War (2018) - BluRay 1080p - Standard.mkv"),
                Find(location));
        }

        [Fact]
        public void FindDownloadVersion_ServesTheTierTheUserAskedFor()
        {
            var location = CreateLocation(
                "Avengers - Infinity War (2018) - BluRay 1080p - High.mkv",
                "Avengers - Infinity War (2018) - BluRay 1080p - Standard.mkv");

            Assert.Equal(
                Path.Combine(location, FolderName, "Avengers - Infinity War (2018) - BluRay 1080p - Standard.mkv"),
                FindWithTiers(["High", "Standard"], "id-Standard", location));
        }

        [Fact]
        public void FindDownloadVersion_FallsBackToTheOtherTierWhenTheChosenOneHasNoFile()
        {
            // Every copy is High, and the user asked for Standard.
            var location = CreateLocation("Avengers - Infinity War (2018) - BluRay 1080p - High.mkv");

            Assert.Equal(
                Path.Combine(location, FolderName, "Avengers - Infinity War (2018) - BluRay 1080p - High.mkv"),
                FindWithTiers(["High", "Standard"], "id-Standard", location));
        }

        [Fact]
        public void FindDownloadVersion_ServesATierTheAdminDisabledWhenItIsAllThereIs()
        {
            var location = CreateLocation("Avengers - Infinity War (2018) - BluRay 1080p - Standard.mkv");

            // The admin has since turned Standard off, so the stored choice stops counting - but
            // the file is still served, since the alternative is the item's own file.
            Assert.Equal(
                Path.Combine(location, FolderName, "Avengers - Infinity War (2018) - BluRay 1080p - Standard.mkv"),
                FindWithTiers(["High"], "id-Standard", location));
        }

        [Fact]
        public void FindDownloadVersion_PrefersTheChosenTierOverTheLocationOrder()
        {
            // The user's tier wins wherever it sits, even in a later location.
            var first = CreateLocation("Avengers - Infinity War (2018) - BluRay 1080p - High.mkv");
            var second = CreateLocation("Avengers - Infinity War (2018) - BluRay 1080p - Standard.mkv");

            Assert.Equal(
                Path.Combine(second, FolderName, "Avengers - Infinity War (2018) - BluRay 1080p - Standard.mkv"),
                FindWithTiers(["High", "Standard"], "id-Standard", first, second));
        }

        [Fact]
        public void FindDownloadVersion_KeepsEachTiersFallbackInsideItsOwnTier()
        {
            // One file per tier, neither named after the source. The "only file with this suffix"
            // rule finds each within its own tier, and must not pick the other tier's file.
            var location = CreateLocation(
                "Avengers - Infinity War (2018) - IMAX DV WEB-DL 2160p - High.mkv",
                "Avengers - Infinity War (2018) - IMAX DV WEB-DL 2160p - Standard.mkv");

            Assert.Equal(
                Path.Combine(location, FolderName, "Avengers - Infinity War (2018) - IMAX DV WEB-DL 2160p - Standard.mkv"),
                FindWithTiers(["High", "Standard"], "id-Standard", location));
        }

        [Fact]
        public void FindDownloadVersion_IgnoresSidecars()
        {
            var location = CreateLocation(
                "Avengers - Infinity War (2018) - BluRay 1080p - High.mkv.json",
                "Avengers - Infinity War (2018) - BluRay 1080p - High.eng.srt",
                "Avengers - Infinity War (2018) - BluRay 1080p - High.srt");

            Assert.Null(Find(location));
        }

        [Fact]
        public void FindDownloadVersion_SkipsLocationsWithoutTheFolder()
        {
            var empty = Path.Combine(_root, "empty");
            Directory.CreateDirectory(empty);
            var location = CreateLocation("Avengers - Infinity War (2018) - BluRay 1080p - High.mkv");

            Assert.Equal(
                Path.Combine(location, FolderName, "Avengers - Infinity War (2018) - BluRay 1080p - High.mkv"),
                Find(empty, location));
        }

        [Fact]
        public void FindDownloadVersion_SkipsALocationThatCannotBeRead()
        {
            // An unreadable folder must not fail the search: the plain route would 500 rather than
            // fall back to the item's own file.
            var unreadable = CreateLocation("Avengers - Infinity War (2018) - BluRay 1080p - High.mkv");
            var readable = CreateLocation("Avengers - Infinity War (2018) - BluRay 1080p - Standard.mkv");
            var folder = Path.Combine(unreadable, FolderName);

            SetReadable(folder, false);
            try
            {
                if (IsReadable(folder))
                {
                    Assert.Skip("Could not make a folder unreadable here - running as root?");
                }

                Assert.Equal(
                    Path.Combine(readable, FolderName, "Avengers - Infinity War (2018) - BluRay 1080p - Standard.mkv"),
                    Find(unreadable, readable));
            }
            finally
            {
                SetReadable(folder, true);
            }
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void FindDownloadVersion_ReturnsNullWithoutAFolder(string? path)
        {
            Assert.Null(DownloadLocator.FindDownloadVersion(new DownloadOptions { Locations = [_root] }, path));
        }

        [Fact]
        public void FindDownloadVersion_ReturnsNullWithoutLocations()
        {
            Assert.Null(DownloadLocator.FindDownloadVersion(new DownloadOptions(), Path.Combine(_root, FolderName, SourceName)));
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }

            GC.SuppressFinalize(this);
        }

        private static string? Find(params string[] locations)
            => DownloadLocator.FindDownloadVersion(
                new DownloadOptions { Locations = locations },
                Path.Combine("/movies", FolderName, SourceName));

        /// <summary>
        /// Runs the lookup against the two tiers these tests use, enabled by membership of
        /// <paramref name="enabled"/> - the admin's ticks. A tier left out is still defined, and so
        /// is still searched, last.
        /// </summary>
        private static string? FindWithTiers(string[] enabled, string? preferred, params string[] locations)
            => DownloadLocator.FindDownloadVersion(
                new DownloadOptions
                {
                    Locations = locations,
                    Tiers = [Tier("High", enabled), Tier("Standard", enabled)]
                },
                Path.Combine("/movies", FolderName, SourceName),
                preferred);

        private static DownloadTier Tier(string suffix, string[] enabled)
            => new DownloadTier
            {
                Id = "id-" + suffix,
                Suffix = suffix,
                Name = suffix,
                Enabled = enabled.Contains(suffix, StringComparer.OrdinalIgnoreCase)
            };

        private static void SetReadable(string folder, bool readable)
        {
            if (OperatingSystem.IsWindows())
            {
                var info = new DirectoryInfo(folder);
                var security = info.GetAccessControl();
                var rule = new FileSystemAccessRule(
                    WindowsIdentity.GetCurrent().User!,
                    FileSystemRights.ListDirectory,
                    AccessControlType.Deny);

                if (readable)
                {
                    security.RemoveAccessRule(rule);
                }
                else
                {
                    security.AddAccessRule(rule);
                }

                info.SetAccessControl(security);
            }
            else
            {
                File.SetUnixFileMode(
                    folder,
                    readable ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute : UnixFileMode.None);
            }
        }

        private static bool IsReadable(string folder)
        {
            try
            {
                _ = Directory.EnumerateFiles(folder).Any();
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private string CreateLocation(params string[] fileNames)
        {
            var location = Path.Combine(_root, "location-" + Guid.NewGuid().ToString("N"));
            var folder = Path.Combine(location, FolderName);
            Directory.CreateDirectory(folder);
            foreach (var fileName in fileNames)
            {
                File.WriteAllText(Path.Combine(folder, fileName), string.Empty);
            }

            return location;
        }
    }
}
