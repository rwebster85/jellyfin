using System;
using System.Collections.Generic;
using System.IO;
using Jellyfin.Api.Helpers;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Api.Tests.Helpers
{
    /// <summary>
    /// How the switches combine: <see cref="DownloadOptions.Enabled"/>, <see cref="DownloadOptions.Behaviour"/>,
    /// <see cref="DownloadOptions.AllowOriginal"/>, and the user's stored choice.
    /// </summary>
    public sealed class DownloadHelperTests : IDisposable
    {
        private const string FolderName = "Greyhound (2020)";

        private static readonly Guid _userId = Guid.NewGuid();

        private readonly string _root = Path.Combine(Path.GetTempPath(), "jf-download-helper-" + Guid.NewGuid().ToString("N"));
        private readonly string _itemPath = Path.Combine("/movies", FolderName, "Greyhound (2020) - DV 2160p.mkv");
        private readonly string _rendition;
        private readonly MemoryCache _cache = new(new MemoryCacheOptions());

        public DownloadHelperTests()
        {
            var folder = Path.Combine(_root, FolderName);
            Directory.CreateDirectory(folder);
            _rendition = Path.Combine(folder, "Greyhound (2020) - DV 2160p - High.mkv");
            File.WriteAllText(_rendition, string.Empty);
        }

        [Fact]
        public void FindForPlainDownload_SubstitutesUnderSubstitute()
            => Assert.Equal(_rendition, Create(Options(DownloadBehaviour.Substitute)).FindForPlainDownload(_itemPath, _userId));

        [Fact]
        public void FindForPlainDownload_ServesTheItemsOwnFileUnderSeparateAction()
            => Assert.Null(Create(Options(DownloadBehaviour.SeparateAction)).FindForPlainDownload(_itemPath, _userId));

        [Fact]
        public void FindForPlainDownload_ServesTheItemsOwnFileToAUserWhoChoseTheOriginal()
            => Assert.Null(Create(Options(DownloadBehaviour.Substitute), DownloadTiers.OriginalId).FindForPlainDownload(_itemPath, _userId));

        [Fact]
        public void FindForPlainDownload_SubstitutesAgainOnceTheOriginalIsWithdrawn()
        {
            // The stored choice is kept but stops counting, the same as a tier being disabled.
            var helper = Create(Options(DownloadBehaviour.Substitute, allowOriginal: false), DownloadTiers.OriginalId);

            Assert.Equal(_rendition, helper.FindForPlainDownload(_itemPath, _userId));
        }

        [Fact]
        public void FindForUser_IgnoresTheOriginalChoice()
        {
            // The optimised routes mean the optimised file, whatever the user chose.
            var helper = Create(Options(DownloadBehaviour.Substitute), DownloadTiers.OriginalId);

            Assert.Equal(_rendition, helper.FindForUser(_itemPath, _userId));
        }

        [Fact]
        public void FindForUser_FindsNothingWhileTheFeatureIsOff()
            => Assert.Null(Create(Options(DownloadBehaviour.Substitute, enabled: false)).FindForUser(_itemPath, _userId));

        [Theory]
        [InlineData(true, DownloadBehaviour.Substitute, true, true)]
        [InlineData(false, DownloadBehaviour.Substitute, true, false)]
        [InlineData(true, DownloadBehaviour.SeparateAction, true, false)]
        [InlineData(true, DownloadBehaviour.Substitute, false, false)]
        public void OffersOriginal_OnlyUnderSubstituteWithTheFeatureOnAndAllowed(
            bool enabled,
            DownloadBehaviour behaviour,
            bool allowOriginal,
            bool expected)
            => Assert.Equal(expected, Create(Options(behaviour, enabled, allowOriginal)).OffersOriginal());

        [Fact]
        public void ChoseOriginal_IsFalseWhereTheOriginalIsNotOffered()
            => Assert.False(Create(Options(DownloadBehaviour.SeparateAction), DownloadTiers.OriginalId).ChoseOriginal(_userId));

        [Fact]
        public void GetUserTier_ReportsATierTheAdminDisabledAsNoChoice()
        {
            var options = Options(DownloadBehaviour.Substitute);
            options.Tiers[1].Enabled = false;

            Assert.Null(Create(options, options.Tiers[1].Id).GetUserTier(_userId));
        }

        [Fact]
        public void GetEnabledTiers_IsEmptyWhileTheFeatureIsOff()
            => Assert.Empty(Create(Options(DownloadBehaviour.Substitute, enabled: false)).GetEnabledTiers());

        [Theory]
        [InlineData(true, DownloadBehaviour.Substitute)]
        [InlineData(false, DownloadBehaviour.SeparateAction)]
        public void GetBehaviour_ReportsTheSwitchAndTheBehaviourAsConfigured(bool enabled, DownloadBehaviour behaviour)
            => Assert.Equal((enabled, behaviour), Create(Options(behaviour, enabled)).GetBehaviour());

        public void Dispose()
        {
            _cache.Dispose();

            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        private DownloadOptions Options(DownloadBehaviour behaviour, bool enabled = true, bool allowOriginal = true)
            => new()
            {
                Enabled = enabled,
                Locations = [_root],
                Behaviour = behaviour,
                AllowOriginal = allowOriginal
            };

        private DownloadHelper Create(DownloadOptions options, string? storedChoice = null)
        {
            var configuration = new Mock<IServerConfigurationManager>();
            configuration
                .Setup(manager => manager.GetConfiguration(DownloadConfigurationStore.StoreKey))
                .Returns(options);

            var preferences = new Mock<IDisplayPreferencesManager>();
            preferences
                .Setup(manager => manager.ListCustomItemDisplayPreferences(_userId, Guid.Empty, DownloadPreferences.Client))
                .Returns(storedChoice is null
                    ? []
                    : new Dictionary<string, string?> { [DownloadPreferences.TierKey] = storedChoice });

            return new DownloadHelper(
                configuration.Object,
                preferences.Object,
                Mock.Of<IMediaEncoder>(),
                Mock.Of<IMediaSourceManager>(),
                _cache,
                NullLogger<DownloadHelper>.Instance);
        }
    }
}
