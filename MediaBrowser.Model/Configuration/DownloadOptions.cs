#pragma warning disable CA1819 // XML serialization handles collections improperly, so we need to use arrays

namespace MediaBrowser.Model.Configuration
{
    /// <summary>
    /// Class DownloadOptions.
    /// </summary>
    public class DownloadOptions
    {
        /// <summary>
        /// Gets or sets the folders searched for pre-made download versions.
        /// </summary>
        public string[] Locations { get; set; } = [];

        /// <summary>
        /// Gets or sets the quality tiers a download version may be served from, as names from
        /// <see cref="DownloadQualities"/>. A user chooses one of these for themselves; a user who
        /// has not chosen gets the first enabled tier, and a tier with no file for an item falls
        /// back to the others.
        /// </summary>
        public string[] Qualities { get; set; } = [DownloadQualities.High, DownloadQualities.Standard];

        /// <summary>
        /// Gets or sets which download behaviour the server offers. Defaults to
        /// <see cref="DownloadBehaviour.Substitute"/>, which is how the feature has always behaved,
        /// so an existing configuration reads back unchanged.
        /// </summary>
        public DownloadBehaviour Behaviour { get; set; } = DownloadBehaviour.Substitute;
    }
}
