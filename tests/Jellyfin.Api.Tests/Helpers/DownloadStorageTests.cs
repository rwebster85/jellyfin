using System;
using System.Collections.Generic;
using System.IO;
using Jellyfin.Api.Helpers;
using MediaBrowser.Model.Configuration;
using Xunit;

namespace Jellyfin.Api.Tests.Helpers
{
    /// <summary>
    /// Which folders hold transcoded copies, where a copy sits in one, and which folder a new copy goes in.
    /// </summary>
    public sealed class DownloadStorageTests : IDisposable
    {
        private static readonly Guid _versionId = Guid.Parse("a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d");

        private readonly string _root = Path.Combine(Path.GetTempPath(), "DownloadStorageTests-" + Guid.NewGuid().ToString("N"));

        [Fact]
        public void GetLocations_WithNoneSet_IsTheDownloadsFolderUnderTheDataPath()
            => Assert.Equal([Path.Combine("data", "downloads")], DownloadStorage.GetLocations(new DownloadOptions(), "data"));

        [Fact]
        public void GetLocations_KeepsTheAdminsOrder_AndDropsBlanksAndRepeats()
        {
            var options = new DownloadOptions { TranscodeLocations = ["/mnt/b", " ", "/mnt/a ", "/mnt/B"] };

            Assert.Equal(["/mnt/b", "/mnt/a"], DownloadStorage.GetLocations(options, "data"));
        }

        [Fact]
        public void GetVersionFolder_IsTrickplaysLayout()
            => Assert.Equal(
                Path.Combine("a1", "a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d"),
                DownloadStorage.GetVersionFolder(_versionId));

        [Fact]
        public void GetRelativePath_IsTheVersionFolderThenTheTier()
            => Assert.Equal(
                Path.Combine("a1", "a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d", "8d3a6f1e7c4b4a2d9e5f1b0c2d3e4f50.mkv"),
                DownloadStorage.GetRelativePath(_versionId, "8d3a6f1e7c4b4a2d9e5f1b0c2d3e4f50"));

        [Fact]
        public void GetSidecarPath_ReplacesOnlyTheContainerExtension()
            => Assert.Equal(
                Path.Combine("x", "a1b2.8d3a.json"),
                DownloadStorage.GetSidecarPath(Path.Combine("x", "a1b2.8d3a.mkv")));

        [Fact]
        public void FindFinished_IgnoresACopyWithNoSidecar_AndFindsOneInALaterLocation()
        {
            var relative = DownloadStorage.GetRelativePath(_versionId, "8d3a");
            var first = Path.Combine(_root, "first");
            var second = Path.Combine(_root, "second");

            // Still being written, or left by a failed encode: no sidecar yet.
            Touch(Path.Combine(first, relative));
            var finished = Path.Combine(second, relative);
            Touch(finished);
            Touch(DownloadStorage.GetSidecarPath(finished));

            Assert.Equal(finished, DownloadStorage.FindFinished([first, second], relative));
        }

        [Fact]
        public void FindFinished_WithNoCopy_IsNull()
            => Assert.Null(DownloadStorage.FindFinished([_root], DownloadStorage.GetRelativePath(_versionId, "8d3a")));

        [Theory]
        [InlineData(100, 500, "/a")]
        [InlineData(50, 500, "/b")]
        [InlineData(50, 60, null)]
        [InlineData(-1, 60, "/a")]
        public void ChooseLocation_TakesTheFirstWithRoom(long freeOnA, long freeOnB, string? expected)
        {
            var free = new Dictionary<string, long> { ["/a"] = freeOnA, ["/b"] = freeOnB };

            Assert.Equal(expected, DownloadStorage.ChooseLocation(["/a", "/b"], 100, location => free[location]));
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        private static void Touch(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, []);
        }
    }
}
