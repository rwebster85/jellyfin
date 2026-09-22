using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Serialization;
using MediaBrowser.Model.Configuration;
using Xunit;

namespace Jellyfin.Model.Tests.Configuration
{
    /// <summary>
    /// The download settings are stored as XML, and whether a server starts from the seeded tiers
    /// or from none turns on what the serializer does with an element that is not in the file - so
    /// it is pinned here rather than assumed.
    /// </summary>
    public static class DownloadOptionsSerializationTests
    {
        [Fact]
        public static void RoundTrips_TheTierList()
        {
            var options = new DownloadOptions
            {
                Enabled = true,
                Locations = ["/mnt/portable"],
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
            // A property initializer survives an element that is absent from the file, which is
            // what lets a settings file written before tiers existed - or one hand-written from
            // scratch - come up with working examples rather than nothing at all.
            var options = Deserialize("""
                <?xml version="1.0" encoding="utf-8"?>
                <DownloadOptions xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
                  <Enabled>true</Enabled>
                  <Locations>
                    <string>/mnt/5TB1/Portable/Movies</string>
                  </Locations>
                  <Behaviour>SeparateAction</Behaviour>
                </DownloadOptions>
                """);

            Assert.Equal(["High", "Standard"], DownloadTiers.GetTiers(options).Select(tier => tier.Suffix));
        }

        [Fact]
        public static void AnEmptyTierElement_IsKeptEmpty()
        {
            // The other half of the same mechanism, and the one that makes "this server does not
            // use tiers" expressible: an element that IS present and empty overrides the seed, and
            // has to keep doing so across a restart.
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
        /// Reads the settings back, through an <see cref="XmlReader"/> rather than straight from a
        /// <see cref="TextReader"/>: the simpler overload leaves DTD processing and an
        /// <see cref="System.Xml.XmlResolver"/> available, which is how an XML parser is talked into
        /// fetching external entities or expanding one into a denial of service. Neither is a real
        /// risk for a string literal in a test, but the rule is worth honouring where the test would
        /// otherwise be read as the example of how to do it.
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
