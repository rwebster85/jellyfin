using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Api.Helpers;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using Xunit;

namespace Jellyfin.Api.Tests.Helpers
{
    public sealed class DownloadProbeHelperTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "jf-probe-" + Guid.NewGuid().ToString("N"));
        private readonly MemoryCache _cache = new(new MemoryCacheOptions());
        private readonly Guid _itemId = Guid.NewGuid();

        [Fact]
        public async Task ProbeAsync_DescribesTheFileItWasGiven()
        {
            var path = CreateFile("Avengers - Infinity War (2018) - BluRay 1080p - High.mkv", "some bytes");
            var encoder = CreateEncoder();

            var result = await DownloadProbeHelper.ProbeAsync(encoder.Object, _cache, _itemId, path, CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal(path, result.Path);
            Assert.Equal(_itemId.ToString("N"), result.Id);
            Assert.Equal("Avengers - Infinity War (2018) - BluRay 1080p - High", result.Name);
            Assert.Equal(MediaProtocol.File, result.Protocol);
            Assert.False(result.IsRemote);
            Assert.Single(result.MediaStreams);
        }

        [Fact]
        public async Task ProbeAsync_FallsBackToTheFileLengthWhenTheProbeHasNoSize()
        {
            var path = CreateFile("size - High.mkv", "0123456789");
            var encoder = CreateEncoder();

            var result = await DownloadProbeHelper.ProbeAsync(encoder.Object, _cache, _itemId, path, CancellationToken.None);

            Assert.Equal(10, result?.Size);
        }

        [Fact]
        public async Task ProbeAsync_ProbesOnceForTheSameFile()
        {
            var path = CreateFile("cached - High.mkv", "some bytes");
            var encoder = CreateEncoder();

            await DownloadProbeHelper.ProbeAsync(encoder.Object, _cache, _itemId, path, CancellationToken.None);
            await DownloadProbeHelper.ProbeAsync(encoder.Object, _cache, _itemId, path, CancellationToken.None);

            encoder.Verify(e => e.GetMediaInfo(It.IsAny<MediaInfoRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ProbeAsync_ProbesAgainWhenTheFileChanges()
        {
            var path = CreateFile("remade - High.mkv", "some bytes");
            var encoder = CreateEncoder();

            await DownloadProbeHelper.ProbeAsync(encoder.Object, _cache, _itemId, path, CancellationToken.None);

            // A re-made portable copy: same path, different content.
            await File.WriteAllTextAsync(path, "some other bytes", TestContext.Current.CancellationToken);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));

            await DownloadProbeHelper.ProbeAsync(encoder.Object, _cache, _itemId, path, CancellationToken.None);

            encoder.Verify(e => e.GetMediaInfo(It.IsAny<MediaInfoRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Fact]
        public async Task ProbeAsync_ProbesPerItemSoAlternateVersionsGetTheirOwnSource()
        {
            var path = CreateFile("shared - High.mkv", "some bytes");
            var encoder = CreateEncoder();
            var otherItemId = Guid.NewGuid();

            var first = await DownloadProbeHelper.ProbeAsync(encoder.Object, _cache, _itemId, path, CancellationToken.None);
            var second = await DownloadProbeHelper.ProbeAsync(encoder.Object, _cache, otherItemId, path, CancellationToken.None);

            Assert.Equal(_itemId.ToString("N"), first?.Id);
            Assert.Equal(otherItemId.ToString("N"), second?.Id);
        }

        [Fact]
        public async Task ProbeAsync_ReturnsNullWhenTheFileHasGone()
        {
            var encoder = CreateEncoder();

            var result = await DownloadProbeHelper.ProbeAsync(
                encoder.Object,
                _cache,
                _itemId,
                Path.Combine(_root, "not there - High.mkv"),
                CancellationToken.None);

            Assert.Null(result);
            encoder.Verify(e => e.GetMediaInfo(It.IsAny<MediaInfoRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        public void Dispose()
        {
            _cache.Dispose();

            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }

            GC.SuppressFinalize(this);
        }

        private static Mock<IMediaEncoder> CreateEncoder()
        {
            var encoder = new Mock<IMediaEncoder>();
            encoder
                .Setup(e => e.GetMediaInfo(It.IsAny<MediaInfoRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new MediaInfo
                {
                    Container = "mkv",
                    MediaStreams = [new MediaStream { Index = 0, Type = MediaStreamType.Video, Codec = "hevc" }]
                });

            return encoder;
        }

        private string CreateFile(string fileName, string content)
        {
            Directory.CreateDirectory(_root);
            var path = Path.Combine(_root, fileName);
            File.WriteAllText(path, content);
            return path;
        }
    }
}
