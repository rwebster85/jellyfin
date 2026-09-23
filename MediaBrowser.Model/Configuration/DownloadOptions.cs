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
        /// - working examples rather than placeholders that would match nothing. An administrator who wants none deletes them, and an empty
        /// <c>&lt;Tiers /&gt;</c> element stays empty: the serializer only leaves this initial value
        /// in place when the element is absent altogether, which is a file that has never been
        /// saved.
        /// </remarks>
        public DownloadTier[] Tiers { get; set; } = DownloadTiers.CreateSeedTiers();

        /// <summary>
        /// Gets or sets the id of the tier a user gets when they have not chosen one.
        /// </summary>
        /// <remarks>
        /// Starts as the seed's own default, so a server that has never saved its settings already
        /// names one rather than relying on the fall-back below to pick the same tier silently.
        ///
        /// Empty, or naming a tier that is missing or disabled, still falls back to the first
        /// enabled tier: an administrator editing this file by hand should not be able to leave
        /// users with nothing. The dashboard is stricter and will not save without a default,
        /// because there the column is right in front of them.
        /// </remarks>
        public string? DefaultTierId { get; set; } = DownloadTiers.SeedDefaultTierId;

        /// <summary>
        /// Gets or sets which download behaviour the server offers. Defaults to
        /// <see cref="DownloadBehaviour.SeparateAction"/>, so that switching the feature on adds an
        /// action rather than silently changing what the existing Download button does.
        /// </summary>
        public DownloadBehaviour Behaviour { get; set; } = DownloadBehaviour.SeparateAction;

        /// <summary>
        /// Gets or sets a value indicating whether a user may choose to receive the original file
        /// rather than an optimised copy.
        /// </summary>
        /// <remarks>
        /// Only meaningful under <see cref="DownloadBehaviour.Substitute"/>, where the plain Download
        /// is the only way to download and would otherwise always hand over the optimised copy.
        /// Under <see cref="DownloadBehaviour.SeparateAction"/> the plain Download already serves the
        /// original, so there is nothing to opt out of - and an Optimised Download that honoured the
        /// choice would serve the original under a label that says otherwise.
        ///
        /// A permission rather than a tier, so it is not in <see cref="Tiers"/>: by quality the
        /// original would sort to the top of that list and by fall-back it is the very end, and one
        /// list cannot mean both. It is never anybody's default either - a user gets it only by
        /// choosing it. On by default for that reason; an administrator who does not want
        /// full-size files pulled over the internet turns it off, and users who had chosen it go
        /// back to the default tier.
        /// </remarks>
        public bool AllowOriginal { get; set; } = true;
    }
}
