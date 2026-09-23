using MediaBrowser.Model.Configuration;
using Xunit;

namespace Jellyfin.Model.Tests.Configuration
{
    public static class DownloadOptionsTests
    {
        [Fact]
        public static void Enabled_IsOffOutOfTheBox()
        {
            // Installing or upgrading changes nothing until an admin turns the feature on.
            Assert.False(new DownloadOptions().Enabled);
        }

        [Fact]
        public static void Behaviour_SeparatesTheActionsOutOfTheBox()
        {
            // Turning the feature on adds an action rather than changing the Download button.
            Assert.Equal(DownloadBehaviour.SeparateAction, new DownloadOptions().Behaviour);
        }

        [Fact]
        public static void Behaviour_SeparateActionIsTheZeroValue()
        {
            // A file with no <Behaviour> element reads back as the zero value, so that must be the
            // one that leaves the Download button unchanged.
            Assert.Equal(DownloadBehaviour.SeparateAction, (DownloadBehaviour)0);
        }

        [Fact]
        public static void AllowOriginal_IsOnOutOfTheBox()
        {
            // Never anybody's default, so safe to offer from the start.
            Assert.True(new DownloadOptions().AllowOriginal);
        }
    }
}
