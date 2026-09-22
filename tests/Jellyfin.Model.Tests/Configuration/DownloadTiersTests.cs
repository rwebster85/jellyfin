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
            // A server with no settings file yet gets the pair tools/Portable emits, so dropping a
            // rendition in works without configuring anything. Placeholders would match nothing,
            // which is a silent no-op rather than a starting point.
            var tiers = DownloadTiers.GetTiers(new DownloadOptions());

            Assert.Equal(["High", "Standard"], tiers.Select(tier => tier.Suffix));
            Assert.Equal(["1080p", "720p"], tiers.Select(tier => tier.Name));
            Assert.All(tiers, tier => Assert.True(tier.Enabled));
        }

        [Fact]
        public static void CreateSeedTiers_HandsOutTheSameIdsEveryTime()
        {
            // A server that has not saved its settings builds the seed on every read. Fresh ids
            // each time would move what a user's stored choice points at between two requests.
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
            // DownloadTiers answers "what tiers does this configuration describe", which is a
            // different question from "may anyone have one". The switch is honoured by the service
            // layer (DownloadHelper), so that the dashboard can still show and edit the tier table
            // while the feature is off.
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
            // Not the first row. The default is an explicit choice now, so reordering the table
            // does not reassign every user who never picked one.
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
            // The default no longer has to sit at the top of the table, so a user who has not
            // chosen has to be pointed at it rather than at the first row.
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
            // The whole point of the id: an admin renaming a tier - because the files were renamed,
            // or the label was wrong - does not silently drop everyone who chose it.
            var options = new DownloadOptions { Tiers = [Tier("a", "Large"), Tier("b", "Tiny")] };

            Assert.Equal(["Tiny", "Large"], DownloadTiers.GetSearchOrder(options, "b"));
        }

        [Fact]
        public static void GetSearchOrder_IgnoresAChoiceThatNamesASuffixRatherThanAnId()
        {
            // A suffix is editable, so two tiers can swap suffixes over a server's life. Resolving
            // a stored choice by name would then hand the user a different tier than the one they
            // saved, which is worse than falling back to the default.
            var options = new DownloadOptions { Tiers = [Tier("a", "High"), Tier("b", "Standard")] };

            Assert.Equal(["High", "Standard"], DownloadTiers.GetSearchOrder(options, "Standard"));
        }

        [Fact]
        public static void GetSearchOrder_DemotesATierTheAdminHasTurnedOff()
        {
            // The user chose Small, then the admin disabled it. Their choice stops counting, so the
            // default leads - but Small is searched last rather than dropped, because the only
            // thing after it is the item's own file, which is larger than any rendition.
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
        public static void PrepareForSave_RefusesADefaultThatUsersCannotBeGiven()
        {
            // Pointing the default at a disabled tier behaves exactly like not setting it at all,
            // which is the kind of silent no-op worth refusing at the moment it is written.
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
