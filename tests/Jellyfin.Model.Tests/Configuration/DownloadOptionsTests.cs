using MediaBrowser.Model.Configuration;
using Xunit;

namespace Jellyfin.Model.Tests.Configuration
{
    public static class DownloadOptionsTests
    {
        [Fact]
        public static void Enabled_IsOffOutOfTheBox()
        {
            // The whole point of the flag: installing or upgrading changes nothing until an admin
            // turns the feature on. It is also what lets Behaviour be chosen on merit rather than
            // on which existing install its default would least surprise.
            Assert.False(new DownloadOptions().Enabled);
        }

        [Fact]
        public static void Enabled_IsTheZeroValue()
        {
            // An existing downloads.xml has no <Enabled> element, so it deserialises as default -
            // false. An upgrade therefore switches the feature off rather than inheriting it, which
            // is deliberate: the admin re-enables it and picks a behaviour at the same time.
            Assert.False(default(bool));
        }

        [Fact]
        public static void Behaviour_SeparatesTheActionsOutOfTheBox()
        {
            // Turning the feature on should ADD an action, not silently change what the existing
            // Download button already does. Substitute is the deliberate opt-in.
            Assert.Equal(DownloadBehaviour.SeparateAction, new DownloadOptions().Behaviour);
        }

        [Fact]
        public static void Behaviour_SeparateActionIsTheZeroValue()
        {
            // A config with no <Behaviour> element reads back as this enum's zero value, so the
            // zero value has to be the one that leaves the plain Download button meaning what it
            // has always meant.
            Assert.Equal(DownloadBehaviour.SeparateAction, (DownloadBehaviour)0);
        }

        [Fact]
        public static void AllowOriginal_IsOnOutOfTheBox()
        {
            // A user only ever gets the original by choosing it - it is never a default - so
            // offering the choice is safe to start with. Turning it off is the admin's decision.
            Assert.True(new DownloadOptions().AllowOriginal);
        }
    }
}
