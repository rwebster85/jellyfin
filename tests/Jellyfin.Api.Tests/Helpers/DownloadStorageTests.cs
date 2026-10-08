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

        [Theory]
        [InlineData("Film (2000)/f.mkv", "Movies/Film (2000)")]
        [InlineData("Show (2002)/Season 01/e.mkv", "Movies/Show (2002)/Season 01")]
        [InlineData("Film (2000)/Featurettes/x.mkv", "Movies/Film (2000)/Featurettes")]
        [InlineData("f.mkv", "Movies")]
        [InlineData("..Film/f.mkv", "Movies/..Film")]
        public void GetMirroredFolder_IsTheRootsNameThenTheSourcesFoldersBeneathIt(string source, string expected)
        {
            var root = Path.Combine(_root, "Movies");

            Assert.Equal(
                Native(expected),
                DownloadStorage.GetMirroredFolder([root], Path.Combine(root, Native(source))));
        }

        [Theory]
        [InlineData("Other/Film (2000)/f.mkv")]
        [InlineData("Movies2/Film (2000)/f.mkv")]
        public void GetMirroredFolder_OutsideEveryRoot_IsNull(string source)
            => Assert.Null(DownloadStorage.GetMirroredFolder([Path.Combine(_root, "Movies")], Path.Combine(_root, Native(source))));

        [Fact]
        public void GetMirroredFolder_UsesTheDeepestRoot_AndIgnoresATrailingSeparator()
        {
            var outer = Path.Combine(_root, "Media") + Path.DirectorySeparatorChar;
            var inner = Path.Combine(_root, "Media", "Movies") + Path.DirectorySeparatorChar;

            Assert.Equal(
                Path.Combine("Movies", "Film (2000)"),
                DownloadStorage.GetMirroredFolder([outer, inner], Path.Combine(inner, "Film (2000)", "f.mkv")));
        }

        [Fact]
        public void GetCandidateFolders_IncludesTheMirroredFolder_ShortestFirst()
        {
            var root = Path.Combine(_root, "Movies");
            var source = Path.Combine(root, "Film (2000)", "Featurettes", "x.mkv");

            var candidates = DownloadStorage.GetCandidateFolders(source);

            Assert.Equal("Featurettes", candidates[0]);
            Assert.Equal(Path.Combine("Film (2000)", "Featurettes"), candidates[1]);
            Assert.Contains(DownloadStorage.GetMirroredFolder([root], source), candidates);
        }

        [Theory]
        [InlineData("a1b2c3d4e5f64a7b8c9d0e1f2a3b4c5d.8d3a.mkv", true)]
        [InlineData("a1b2c3d4e5f64a7b8c9d0e1f2a3b4c5d.8d3a.json", true)]
        [InlineData("BBC Film Night.mkv", false)]
        [InlineData("a1b2c3d4e5f64a7b8c9d0e1f2a3b4c5d", false)]
        public void TryGetVersionId_ReadsTheIdBeforeTheFirstDot(string fileName, bool expected)
        {
            Assert.Equal(expected, DownloadStorage.TryGetVersionId(Path.Combine("x", fileName), out var id));
            Assert.Equal(expected ? _versionId : Guid.Empty, id);
        }

        [Fact]
        public void GetRelativePath_StartsWithTheVersionPrefix()
        {
            Assert.StartsWith(
                DownloadStorage.GetVersionPrefix(_versionId),
                Path.GetFileName(DownloadStorage.GetRelativePath("Movies", _versionId, "8d3a")),
                StringComparison.Ordinal);
        }

        [Fact]
        public void GetRelativePath_IsTheMirroredFolderThenVersionAndTier()
            => Assert.Equal(
                Path.Combine("Movies", "The Northman (2022)", "a1b2c3d4e5f64a7b8c9d0e1f2a3b4c5d.8d3a.mkv"),
                DownloadStorage.GetRelativePath(Path.Combine("Movies", "The Northman (2022)"), _versionId, "8d3a"));

        [Fact]
        public void GetSidecarPath_ReplacesOnlyTheContainerExtension()
            => Assert.Equal(
                Path.Combine("x", "a1b2.8d3a.json"),
                DownloadStorage.GetSidecarPath(Path.Combine("x", "a1b2.8d3a.mkv")));

        [Fact]
        public void FindFinished_IgnoresACopyWithNoSidecar_AndFindsOneInALaterLocation()
        {
            var relative = DownloadStorage.GetRelativePath(Path.Combine("Movies", "Film (2000)"), _versionId, "8d3a");
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
            => Assert.Null(DownloadStorage.FindFinished([_root], Path.Combine("Movies", "Film (2000)", "x.y.mkv")));

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

        private static string Native(string path)
            => path.Replace('/', Path.DirectorySeparatorChar);

        private static void Touch(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, []);
        }
    }
}
