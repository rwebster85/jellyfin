using System;
using System.Linq;
using MediaBrowser.Model.Configuration;
using Xunit;

namespace Jellyfin.Model.Tests.Configuration
{
    public static class DownloadTiersTests
    {
        [Fact]
        public static void GetTiers_StartsFromWorkingExamples()
        {
            // A server with no settings file gets a working pair, so an optimised copy dropped in works
            // without configuring anything.
            var tiers = DownloadTiers.GetTiers(new DownloadOptions());

            Assert.Equal(["High", "Standard"], tiers.Select(tier => tier.Suffix));
            Assert.Equal(["1080p", "720p"], tiers.Select(tier => tier.Name));
            Assert.All(tiers, tier => Assert.True(tier.Enabled));
        }

        [Fact]
        public static void GetDefault_StartsOnTheLargerSeededTier()
        {
            // Named outright by the seed rather than left to the first-enabled fall-back, so the
            // dashboard's Default column is not blank the first time it is opened.
            var options = new DownloadOptions();

            Assert.Equal("High", DownloadTiers.GetDefault(options)?.Suffix);
            Assert.Equal(DownloadTiers.SeedDefaultTierId, options.DefaultTierId);
        }

        [Fact]
        public static void PrepareForSave_AcceptsTheSeedUntouched()
        {
            // The seed has to be valid on its own terms: an admin who opens the page and saves
            // without changing anything must not get a 400.
            var options = new DownloadOptions();

            DownloadTiers.PrepareForSave(options);

            Assert.Equal(DownloadTiers.SeedDefaultTierId, options.DefaultTierId);
        }

        [Fact]
        public static void CreateSeedTiers_HandsOutTheSameIdsEveryTime()
        {
            // The seed is rebuilt on every read, so its ids must not change between reads.
            Assert.Equal(
                DownloadTiers.CreateSeedTiers().Select(tier => tier.Id),
                DownloadTiers.CreateSeedTiers().Select(tier => tier.Id));
        }

        [Fact]
        public static void CreateSeedTiers_IsOwnedByItsCaller()
        {
            // Every DownloadOptions gets its own seed, so editing one server's tiers cannot reach
            // into another instance's.
            var first = DownloadTiers.CreateSeedTiers();
            first[0].Suffix = "Edited";

            Assert.Equal("High", DownloadTiers.CreateSeedTiers()[0].Suffix);
        }

        [Fact]
        public static void GetTiers_IsEmptyWhenTheAdminDeletedEveryTier()
        {
            // An empty list means what it says. The seed only applies to a settings file that has
            // never been saved, which the serialization tests pin.
            var options = new DownloadOptions { Tiers = [] };

            Assert.Empty(DownloadTiers.GetTiers(options));
            Assert.Empty(DownloadTiers.GetSearchOrder(options, null));
        }

        [Fact]
        public static void GetTiers_IsUnaffectedByTheFeatureSwitch()
        {
            // The Enabled switch is DownloadHelper's concern, so the dashboard can still edit the
            // tiers while the feature is off.
            var options = new DownloadOptions { Enabled = false, Tiers = [Tier("a", "Max")] };

            Assert.Equal(["Max"], DownloadTiers.GetTiers(options).Select(tier => tier.Suffix));
        }

        [Fact]
        public static void GetTiers_DropsATierWithNoSuffix()
        {
            // An empty suffix would match nearly every file name. The write path refuses it; this
            // is the hand-edited file.
            var options = new DownloadOptions { Tiers = [Tier("a", " "), Tier("b", "Max")] };

            Assert.Equal(["Max"], DownloadTiers.GetTiers(options).Select(tier => tier.Suffix));
        }

        [Fact]
        public static void GetTiers_DropsADuplicateSuffixRatherThanShadowingTheFirst()
        {
            var options = new DownloadOptions { Tiers = [Tier("a", "Max"), Tier("b", "max")] };

            var tiers = DownloadTiers.GetTiers(options);

            Assert.Single(tiers);
            Assert.Equal("a", tiers[0].Id);
        }

        [Fact]
        public static void GetTiers_FallsBackToTheSuffixForATierWithNoName()
        {
            var options = new DownloadOptions { Tiers = [new DownloadTier { Id = "a", Suffix = " Max " }] };

            var tier = Assert.Single(DownloadTiers.GetTiers(options));

            Assert.Equal("Max", tier.Suffix);
            Assert.Equal("Max", tier.Name);
        }

        [Fact]
        public static void GetEnabled_KeepsTheOrderTheAdminArranged()
        {
            var options = new DownloadOptions
            {
                Tiers = [Tier("a", "Small"), Tier("b", "Large", enabled: false), Tier("c", "Medium")]
            };

            Assert.Equal(["Small", "Medium"], DownloadTiers.GetEnabled(options).Select(tier => tier.Suffix));
        }

        [Fact]
        public static void GetDefault_IsTheTierTheAdminNamed()
        {
            // Not the first row: reordering the table must not reassign users who never chose.
            var options = new DownloadOptions
            {
                Tiers = [Tier("a", "Large"), Tier("b", "Small")],
                DefaultTierId = "b"
            };

            Assert.Equal("Small", DownloadTiers.GetDefault(options)?.Suffix);
        }

        [Fact]
        public static void GetDefault_FallsBackToTheFirstEnabledTierWhenTheNamedOneCannotBeUsed()
        {
            var options = new DownloadOptions
            {
                Tiers = [Tier("a", "Large"), Tier("b", "Small", enabled: false)],
                DefaultTierId = "b"
            };

            Assert.Equal("Large", DownloadTiers.GetDefault(options)?.Suffix);
        }

        [Fact]
        public static void GetDefault_IsNullWhenNothingIsEnabled()
        {
            var options = new DownloadOptions { Tiers = [Tier("a", "Large", enabled: false)] };

            Assert.Null(DownloadTiers.GetDefault(options));
        }

        [Fact]
        public static void GetSearchOrder_PutsTheUsersOwnTierFirst()
        {
            var options = new DownloadOptions { Tiers = [Tier("a", "Large"), Tier("b", "Small")] };

            Assert.Equal(["Small", "Large"], DownloadTiers.GetSearchOrder(options, "b"));
        }

        [Fact]
        public static void GetSearchOrder_PutsTheDefaultFirstForAUserWhoHasNotChosen()
        {
            // The default can sit anywhere in the table, and still leads for a user who has not chosen.
            var options = new DownloadOptions
            {
                Tiers = [Tier("a", "Large"), Tier("b", "Small")],
                DefaultTierId = "b"
            };

            Assert.Equal(["Small", "Large"], DownloadTiers.GetSearchOrder(options, null));
        }

        [Fact]
        public static void GetSearchOrder_KeepsAChoiceWhenTheAdminRenamesTheSuffix()
        {
            // Renaming a tier's suffix keeps everyone who chose it.
            var options = new DownloadOptions { Tiers = [Tier("a", "Large"), Tier("b", "Tiny")] };

            Assert.Equal(["Tiny", "Large"], DownloadTiers.GetSearchOrder(options, "b"));
        }

        [Fact]
        public static void GetSearchOrder_IgnoresAChoiceThatNamesASuffixRatherThanAnId()
        {
            // Suffixes are editable, so a choice matched by name could come to mean another tier.
            var options = new DownloadOptions { Tiers = [Tier("a", "High"), Tier("b", "Standard")] };

            Assert.Equal(["High", "Standard"], DownloadTiers.GetSearchOrder(options, "Standard"));
        }

        [Fact]
        public static void GetSearchOrder_DemotesATierTheAdminHasTurnedOff()
        {
            // The user chose Small, then the admin disabled it: the default leads, and Small is
            // searched last rather than dropped.
            var options = new DownloadOptions
            {
                Tiers = [Tier("a", "Large"), Tier("b", "Small", enabled: false)]
            };

            Assert.Equal(["Large", "Small"], DownloadTiers.GetSearchOrder(options, "b"));
        }

        [Fact]
        public static void GetSearchOrder_SearchesEveryTierWhenNoneIsEnabled()
        {
            var options = new DownloadOptions
            {
                Tiers = [Tier("a", "Large", enabled: false), Tier("b", "Small", enabled: false)]
            };

            Assert.Equal(["Large", "Small"], DownloadTiers.GetSearchOrder(options, null));
        }

        [Fact]
        public static void GetEnabled_IsUnaffectedByTheSearchOrder()
        {
            // What a user may choose, and what the default is, still follow the ticks exactly.
            var options = new DownloadOptions
            {
                Tiers = [Tier("a", "Large", enabled: false), Tier("b", "Small")]
            };

            Assert.Equal(["Small"], DownloadTiers.GetEnabled(options).Select(tier => tier.Suffix));
            Assert.Equal("Small", DownloadTiers.GetDefault(options)?.Suffix);
        }

        [Fact]
        public static void PrepareForSave_GivesANewTierAnId()
        {
            var options = new DownloadOptions { Tiers = [new DownloadTier { Suffix = "Max" }] };

            DownloadTiers.PrepareForSave(options);

            Assert.NotEmpty(options.Tiers[0].Id);
        }

        [Fact]
        public static void PrepareForSave_KeepsAnIdThatAlreadyExists()
        {
            // Users' stored choices point at it, so it must survive every save.
            var options = new DownloadOptions { Tiers = [Tier("a", "Max")] };

            DownloadTiers.PrepareForSave(options);

            Assert.Equal("a", options.Tiers[0].Id);
        }

        [Fact]
        public static void PrepareForSave_TrimsTheSuffix()
        {
            var options = new DownloadOptions { Tiers = [new DownloadTier { Id = "a", Suffix = " Max " }] };

            DownloadTiers.PrepareForSave(options);

            Assert.Equal("Max", options.Tiers[0].Suffix);
        }

        [Fact]
        public static void PrepareForSave_RefusesATierWithNoSuffix()
        {
            var options = new DownloadOptions { Tiers = [new DownloadTier { Id = "a", Name = "Max" }] };

            Assert.Throws<ArgumentException>(() => DownloadTiers.PrepareForSave(options));
        }

        [Fact]
        public static void PrepareForSave_RefusesTwoTiersSharingASuffix()
        {
            // One of them could never be served, and nothing would say which.
            var options = new DownloadOptions { Tiers = [Tier("a", "Max"), Tier("b", "max")] };

            Assert.Throws<ArgumentException>(() => DownloadTiers.PrepareForSave(options));
        }

        [Fact]
        public static void PrepareForSave_RefusesTwoTiersSharingAnId()
        {
            var options = new DownloadOptions { Tiers = [Tier("a", "Max"), Tier("a", "Min")] };

            Assert.Throws<ArgumentException>(() => DownloadTiers.PrepareForSave(options));
        }

        [Fact]
        public static void PrepareForSave_ClearsADefaultThatNamesNoTier()
        {
            // A caller sending its own tiers and no default still carries the seed's id; treat it as
            // unset rather than refuse it.
            var options = new DownloadOptions { Tiers = [Tier("a", "Max")] };

            DownloadTiers.PrepareForSave(options);

            Assert.Null(options.DefaultTierId);
            Assert.Equal("Max", DownloadTiers.GetDefault(options)?.Suffix);
        }

        [Fact]
        public static void PrepareForSave_RefusesADefaultThatUsersCannotBeGiven()
        {
            // A deliberate default that would silently do nothing is refused.
            var options = new DownloadOptions
            {
                Tiers = [Tier("a", "Large"), Tier("b", "Small", enabled: false)],
                DefaultTierId = "b"
            };

            Assert.Throws<ArgumentException>(() => DownloadTiers.PrepareForSave(options));
        }

        [Fact]
        public static void PrepareForSave_AcceptsADefaultThatIsEnabled()
        {
            var options = new DownloadOptions
            {
                Tiers = [Tier("a", "Large"), Tier("b", "Small")],
                DefaultTierId = "b"
            };

            DownloadTiers.PrepareForSave(options);

            Assert.Equal("b", options.DefaultTierId);
        }

        [Fact]
        public static void PrepareForSave_AcceptsNoDefaultAtAll()
        {
            var options = new DownloadOptions { Tiers = [Tier("a", "Large")] };

            DownloadTiers.PrepareForSave(options);

            Assert.Null(options.DefaultTierId);
        }

        [Theory]
        [InlineData("original")]
        [InlineData("Original")]
        [InlineData(" original ")]
        public static void IsOriginal_MatchesTheSentinelHoweverItArrives(string stored)
            => Assert.True(DownloadTiers.IsOriginal(stored));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(DownloadTiers.SeedDefaultTierId)]
        [InlineData("originals")]
        public static void IsOriginal_IsFalseForAnythingElse(string? stored)
            => Assert.False(DownloadTiers.IsOriginal(stored));

        [Fact]
        public static void PrepareForSave_RefusesATierUsingTheOriginalId()
        {
            // A user who chose a tier with that id would get the original file instead.
            var options = new DownloadOptions { Tiers = [Tier("Original", "Large")] };

            Assert.Throws<ArgumentException>(() => DownloadTiers.PrepareForSave(options));
        }

        [Fact]
        public static void GetSearchOrder_TreatsTheOriginalAsNoTier()
        {
            // The optimised routes still search for a user who chose the original: the stored value
            // "original" matches no tier, so the default leads.
            var options = new DownloadOptions
            {
                Tiers = [Tier("a", "Large"), Tier("b", "Small")],
                DefaultTierId = "b"
            };

            Assert.Equal(["Small", "Large"], DownloadTiers.GetSearchOrder(options, DownloadTiers.OriginalId));
        }

        private static DownloadTier Tier(string id, string suffix, bool enabled = true)
            => new DownloadTier
            {
                Id = id,
                Suffix = suffix,
                Name = suffix,
                Enabled = enabled
            };
    }
}
