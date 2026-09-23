using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Serialization;
using MediaBrowser.Model.Configuration;
using Xunit;

namespace Jellyfin.Model.Tests.Configuration
{
    /// <summary>
    /// Whether a server starts from the seeded tiers or from none depends on how the XML serializer
    /// treats a missing element, so it is pinned here.
    /// </summary>
    public static class DownloadOptionsSerializationTests
    {
        [Fact]
        public static void RoundTrips_TheTierList()
        {
            var options = new DownloadOptions
            {
                Enabled = true,
                Locations = ["/media/downloads"],
                Tiers =
                [
                    new DownloadTier { Id = "a", Suffix = "Large", Name = "1080p", Description = "For a laptop" },
                    new DownloadTier { Id = "b", Suffix = "Small", Name = "480p", Enabled = false }
                ],
                DefaultTierId = "a"
            };

            var restored = RoundTrip(options);

            Assert.Equal(["a", "b"], restored.Tiers.Select(tier => tier.Id));
            Assert.Equal(["Large", "Small"], restored.Tiers.Select(tier => tier.Suffix));
            Assert.Equal(["1080p", "480p"], restored.Tiers.Select(tier => tier.Name));
            Assert.Equal("For a laptop", restored.Tiers[0].Description);
            Assert.True(restored.Tiers[0].Enabled);
            Assert.False(restored.Tiers[1].Enabled);
            Assert.Equal("a", restored.DefaultTierId);
        }

        [Fact]
        public static void AFileWithNoTierElement_StartsFromTheSeed()
        {
            // A property initializer survives an absent element, so such a file starts from the seed.
            var options = Deserialize("""
                <?xml version="1.0" encoding="utf-8"?>
                <DownloadOptions xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
                  <Enabled>true</Enabled>
                  <Locations>
                    <string>/media/downloads</string>
                  </Locations>
                  <Behaviour>SeparateAction</Behaviour>
                </DownloadOptions>
                """);

            Assert.Equal(["High", "Standard"], DownloadTiers.GetTiers(options).Select(tier => tier.Suffix));
        }

        [Fact]
        public static void AFileWrittenBeforeAllowOriginal_KeepsItOn()
        {
            // A file saved before the option existed lacks the element, and must read it as on.
            var options = Deserialize("""
                <?xml version="1.0" encoding="utf-8"?>
                <DownloadOptions xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
                  <Enabled>true</Enabled>
                  <Behaviour>Substitute</Behaviour>
                </DownloadOptions>
                """);

            Assert.True(options.AllowOriginal);
            Assert.False(RoundTrip(new DownloadOptions { AllowOriginal = false }).AllowOriginal);
        }

        [Fact]
        public static void AnEmptyTierElement_IsKeptEmpty()
        {
            // A present but empty element overrides the seed: that is how "no tiers" is stored.
            var stored = Deserialize("""
                <?xml version="1.0" encoding="utf-8"?>
                <DownloadOptions xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
                  <Tiers />
                </DownloadOptions>
                """);

            Assert.Empty(DownloadTiers.GetTiers(stored));
        }

        [Fact]
        public static void AnEmptyTierList_SurvivesARoundTrip()
        {
            // Saving "no tiers" has to write the element rather than omit it, or the seed comes
            // back on the next restart.
            Assert.Empty(RoundTrip(new DownloadOptions { Tiers = [] }).Tiers);
        }

        private static DownloadOptions RoundTrip(DownloadOptions options)
        {
            var serializer = new XmlSerializer(typeof(DownloadOptions));

            using var writer = new StringWriter();
            serializer.Serialize(writer, options);

            return Deserialize(writer.ToString());
        }

        /// <summary>
        /// Reads the settings back through an <see cref="XmlReader"/> with DTD processing off, the
        /// safe way to deserialize XML even from a string literal.
        /// </summary>
        private static DownloadOptions Deserialize(string xml)
        {
            using var stringReader = new StringReader(xml);
            using var reader = XmlReader.Create(stringReader, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            });

            return (DownloadOptions)new XmlSerializer(typeof(DownloadOptions)).Deserialize(reader)!;
        }
    }
}
