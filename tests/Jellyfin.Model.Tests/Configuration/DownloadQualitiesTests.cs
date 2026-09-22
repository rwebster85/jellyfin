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
        public static void GetSearchOrder_DemotesATierTheAdminHasTurnedOff()
        {
            // The user chose Standard, then the admin unticked it. Their choice stops counting, so
            // the enabled order leads - but Standard is searched last rather than dropped, because
            // the only thing after it is the item's own file.
            var options = new DownloadOptions { Qualities = ["High"] };

            Assert.Equal(["High", "Standard"], DownloadQualities.GetSearchOrder(options, "Standard"));
        }

        [Fact]
        public static void GetSearchOrder_SearchesEverythingWhenNothingIsEnabled()
        {
            var options = new DownloadOptions { Qualities = [] };

            Assert.Equal(["High", "Standard"], DownloadQualities.GetSearchOrder(options, null));
        }

        [Fact]
        public static void GetEnabled_IsUnaffectedByTheSearchOrderChange()
        {
            // What a user may choose, and what the default is, still follow the ticks exactly.
            var options = new DownloadOptions { Qualities = ["Standard"] };

            Assert.Equal(["Standard"], DownloadQualities.GetEnabled(options));
            Assert.Equal("Standard", DownloadQualities.GetDefault(options));
        }
    }
}
