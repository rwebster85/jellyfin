using System;
using System.Collections.Generic;
using Jellyfin.Api.Helpers;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Configuration;
using Microsoft.Extensions.Caching.Memory;
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
        private static readonly Guid _userId = Guid.NewGuid();

        private readonly MemoryCache _cache = new(new MemoryCacheOptions());

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
        }

        private static DownloadOptions Options(DownloadBehaviour behaviour, bool enabled = true, bool allowOriginal = true)
            => new()
            {
                Enabled = enabled,
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
                Mock.Of<ILibraryManager>(),
                _cache);
        }
    }
}
