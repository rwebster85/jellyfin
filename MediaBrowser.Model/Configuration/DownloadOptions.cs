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
        /// Gets or sets the quality tiers this server offers, best first.
        /// </summary>
        /// <remarks>
        /// Read through <see cref="DownloadTiers.GetTiers"/> rather than directly, which tidies a
        /// list that may have been edited by hand.
        ///
        /// The order carries the fall-back sequence - a tier with no file for an item is followed by
        /// the next one - but not the default, which <see cref="DefaultTierId"/> names outright.
        /// Reordering therefore changes what is tried second, not what a user who has never chosen
        /// receives.
        ///
        /// A server with no settings file yet starts from <see cref="DownloadTiers.CreateSeedTiers"/>
        /// - working examples matching what <c>tools/Portable</c> emits, rather than placeholders
        /// that would match nothing. An administrator who wants none deletes them, and an empty
        /// <c>&lt;Tiers /&gt;</c> element stays empty: the serializer only leaves this initial value
        /// in place when the element is absent altogether, which is a file that has never been
        /// saved.
        /// </remarks>
        public DownloadTier[] Tiers { get; set; } = DownloadTiers.CreateSeedTiers();

        /// <summary>
        /// Gets or sets the id of the tier a user gets when they have not chosen one, or <c>null</c>
        /// to use the first enabled tier.
        /// </summary>
        public string? DefaultTierId { get; set; }

        /// <summary>
        /// Gets or sets which download behaviour the server offers. Defaults to
        /// <see cref="DownloadBehaviour.SeparateAction"/>, so that switching the feature on adds an
        /// action rather than silently changing what the existing Download button does.
        /// </summary>
        public DownloadBehaviour Behaviour { get; set; } = DownloadBehaviour.SeparateAction;
    }
}
