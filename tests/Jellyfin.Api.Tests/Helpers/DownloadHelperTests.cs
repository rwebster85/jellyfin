using System;
using System.IO;
using Jellyfin.Api.Helpers;
using MediaBrowser.Model.Configuration;
using Xunit;

namespace Jellyfin.Api.Tests.Helpers
{
    public sealed class DownloadHelperTests : IDisposable
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
        public void FindDownloadVersion_IgnoresTheWrongQuality()
        {
            var location = CreateLocation("Avengers - Infinity War (2018) - BluRay 1080p - Standard.mkv");

            Assert.Null(Find(location));
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
            Assert.Null(DownloadHelper.FindDownloadVersion(new DownloadOptions { Locations = [_root] }, path));
        }

        [Fact]
        public void FindDownloadVersion_ReturnsNullWithoutLocations()
        {
            Assert.Null(DownloadHelper.FindDownloadVersion(new DownloadOptions(), Path.Combine(_root, FolderName, SourceName)));
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
            => DownloadHelper.FindDownloadVersion(
                new DownloadOptions { Locations = locations },
                Path.Combine("Y:", "Movies", FolderName, SourceName));

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
