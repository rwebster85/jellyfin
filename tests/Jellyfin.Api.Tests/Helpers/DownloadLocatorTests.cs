using System;
using System.IO;
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

            // Enabling a tier decides what a user may choose, not which files may ever be served.
            // The only alternative here is the item's own file, which is far larger than the
            // rendition being refused - so treating the tick as a prohibition achieves the opposite
            // of what unticking it was for.
            Assert.Equal(
                Path.Combine(location, FolderName, "Avengers - Infinity War (2018) - BluRay 1080p - Standard.mkv"),
                FindWithQualities(["High"], null, location));
        }

        [Fact]
        public void FindDownloadVersion_PrefersAnEnabledTierOverADisabledOne()
        {
            var location = CreateLocation(
                "Avengers - Infinity War (2018) - BluRay 1080p - High.mkv",
                "Avengers - Infinity War (2018) - BluRay 1080p - Standard.mkv");

            Assert.Equal(
                Path.Combine(location, FolderName, "Avengers - Infinity War (2018) - BluRay 1080p - Standard.mkv"),
                FindWithQualities(["Standard"], null, location));
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
                FindWithQualities(["High", "Standard"], "Standard", location));
        }

        [Fact]
        public void FindDownloadVersion_FallsBackToTheOtherTierWhenTheChosenOneHasNoFile()
        {
            // The live case: every portable copy is High, and a user has asked for Standard.
            var location = CreateLocation("Avengers - Infinity War (2018) - BluRay 1080p - High.mkv");

            Assert.Equal(
                Path.Combine(location, FolderName, "Avengers - Infinity War (2018) - BluRay 1080p - High.mkv"),
                FindWithQualities(["High", "Standard"], "Standard", location));
        }

        [Fact]
        public void FindDownloadVersion_ServesATierTheAdminDisabledWhenItIsAllThereIs()
        {
            var location = CreateLocation("Avengers - Infinity War (2018) - BluRay 1080p - Standard.mkv");

            // The user's stored choice is stale - the admin has since turned Standard off - so it
            // stops counting as a preference. The file is still served, because the alternative is
            // the item's own file rather than nothing.
            Assert.Equal(
                Path.Combine(location, FolderName, "Avengers - Infinity War (2018) - BluRay 1080p - Standard.mkv"),
                FindWithQualities(["High"], "Standard", location));
        }

        [Fact]
        public void FindDownloadVersion_PrefersTheChosenTierOverTheLocationOrder()
        {
            // Quality-major: the user's tier wins wherever it sits, even in a later location.
            var first = CreateLocation("Avengers - Infinity War (2018) - BluRay 1080p - High.mkv");
            var second = CreateLocation("Avengers - Infinity War (2018) - BluRay 1080p - Standard.mkv");

            Assert.Equal(
                Path.Combine(second, FolderName, "Avengers - Infinity War (2018) - BluRay 1080p - Standard.mkv"),
                FindWithQualities(["High", "Standard"], "Standard", first, second));
        }

        [Fact]
        public void FindDownloadVersion_KeepsEachTiersFallbackInsideItsOwnTier()
        {
            // One file per tier, neither named after the source, so both are found by the
            // single-file fallback - which must not reach across the tier line.
            var location = CreateLocation(
                "Avengers - Infinity War (2018) - IMAX DV WEB-DL 2160p - High.mkv",
                "Avengers - Infinity War (2018) - IMAX DV WEB-DL 2160p - Standard.mkv");

            Assert.Equal(
                Path.Combine(location, FolderName, "Avengers - Infinity War (2018) - IMAX DV WEB-DL 2160p - Standard.mkv"),
                FindWithQualities(["High", "Standard"], "Standard", location));
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
                Path.Combine("Y:", "Movies", FolderName, SourceName));

        private static string? FindWithQualities(string[] qualities, string? preferred, params string[] locations)
            => DownloadLocator.FindDownloadVersion(
                new DownloadOptions { Locations = locations, Qualities = qualities },
                Path.Combine("Y:", "Movies", FolderName, SourceName),
                preferred);

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
