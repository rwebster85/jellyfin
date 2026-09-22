#pragma warning disable CA1819 // XML serialization handles collections improperly, so we need to use arrays

namespace MediaBrowser.Model.Configuration
{
    /// <summary>
    /// Class DownloadOptions.
    /// </summary>
    public class DownloadOptions
    {
        /// <summary>
        /// Gets or sets a value indicating whether the downloads feature is on at all.
        /// </summary>
        /// <remarks>
        /// Off by default, so installing or upgrading changes nothing until an admin says otherwise.
        /// That is what lets <see cref="Behaviour"/> be chosen on merit rather than on which
        /// existing install it would least surprise.
        ///
        /// A non-empty <see cref="Locations"/> was already an implicit switch - nothing is ever
        /// found without one - but an explicit flag can be turned off without losing the configured
        /// paths, and says plainly in the file what an empty list only implies.
        /// </remarks>
        public bool Enabled { get; set; }

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
        /// <see cref="DownloadBehaviour.SeparateAction"/>, so that switching the feature on adds an
        /// action rather than silently changing what the existing Download button does.
        /// </summary>
        public DownloadBehaviour Behaviour { get; set; } = DownloadBehaviour.SeparateAction;
    }
}
