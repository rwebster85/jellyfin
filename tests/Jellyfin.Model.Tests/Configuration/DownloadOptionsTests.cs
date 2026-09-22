using MediaBrowser.Model.Configuration;
using Xunit;

namespace Jellyfin.Model.Tests.Configuration
{
    public static class DownloadOptionsTests
    {
        [Fact]
        public static void Behaviour_SubstitutesOutOfTheBox()
        {
            // The default has to stay Substitute: it is how the feature has always behaved, and it
            // is the only behaviour a client that builds its own download URL can benefit from.
            Assert.Equal(DownloadBehaviour.Substitute, new DownloadOptions().Behaviour);
        }

        [Fact]
        public static void Behaviour_SubstituteIsTheZeroValue()
        {
            // An existing downloads.xml has no <Behaviour> element at all, so it deserialises as
            // the enum's zero value. That has to be Substitute, or upgrading would silently change
            // what every plain download serves.
            Assert.Equal(DownloadBehaviour.Substitute, (DownloadBehaviour)0);
        }
    }
}
