using MediaBrowser.Model.Configuration;
using Xunit;

namespace Jellyfin.Model.Tests.Configuration
{
    public static class DownloadQualitiesTests
    {
        [Fact]
        public static void GetEnabled_OrdersBestFirstWhateverTheStoredOrder()
        {
            var options = new DownloadOptions { Qualities = ["Standard", "High"] };

            Assert.Equal(["High", "Standard"], DownloadQualities.GetEnabled(options));
        }

        [Fact]
        public static void GetEnabled_DropsTiersItDoesNotKnow()
        {
            var options = new DownloadOptions { Qualities = ["Standard", "Ultra"] };

            Assert.Equal(["Standard"], DownloadQualities.GetEnabled(options));
        }

        [Fact]
        public static void GetEnabled_AcceptsTheToolsOwnLowercaseSpelling()
        {
            // tools/Portable names its tiers "high" and "standard"; the filename is capitalised.
            var options = new DownloadOptions { Qualities = ["high", "standard"] };

            Assert.Equal(["High", "Standard"], DownloadQualities.GetEnabled(options));
        }

        [Theory]
        [InlineData(new[] { "High", "Standard" }, "High")]
        [InlineData(new[] { "Standard" }, "Standard")]
        [InlineData(new[] { "High" }, "High")]
        public static void GetDefault_IsTheBestEnabledTier(string[] qualities, string expected)
        {
            Assert.Equal(expected, DownloadQualities.GetDefault(new DownloadOptions { Qualities = qualities }));
        }

        [Fact]
        public static void GetDefault_IsNullWhenNothingIsEnabled()
        {
            Assert.Null(DownloadQualities.GetDefault(new DownloadOptions { Qualities = [] }));
        }

        [Fact]
        public static void GetSearchOrder_PutsTheUsersOwnTierFirst()
        {
            var options = new DownloadOptions { Qualities = ["High", "Standard"] };

            Assert.Equal(["Standard", "High"], DownloadQualities.GetSearchOrder(options, "Standard"));
        }

        [Fact]
        public static void GetSearchOrder_IsTheDefaultOrderWithoutAChoice()
        {
            var options = new DownloadOptions { Qualities = ["High", "Standard"] };

            Assert.Equal(["High", "Standard"], DownloadQualities.GetSearchOrder(options, null));
        }

        [Fact]
        public static void GetSearchOrder_IgnoresATierTheAdminHasTurnedOff()
        {
            // The user chose Standard, then the admin unticked it. They follow the default rather
            // than getting nothing.
            var options = new DownloadOptions { Qualities = ["High"] };

            Assert.Equal(["High"], DownloadQualities.GetSearchOrder(options, "Standard"));
        }
    }
}
