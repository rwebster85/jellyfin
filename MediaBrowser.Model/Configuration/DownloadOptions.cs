#pragma warning disable CA1819 // XML serialization handles collections improperly, so we need to use arrays

namespace MediaBrowser.Model.Configuration
{
    /// <summary>
    /// The optimised downloads settings.
    /// </summary>
    public class DownloadOptions
    {
        /// <summary>
        /// Gets or sets a value indicating whether optimised downloads are on. Off by default, so
        /// installing or upgrading changes nothing until an admin turns it on.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Gets or sets the folders searched for pre-made download versions.
        /// </summary>
        public string[] Locations { get; set; } = [];

        /// <summary>
        /// Gets or sets the tiers this server offers, in fall-back order.
        /// </summary>
        /// <remarks>
        /// Read through <see cref="DownloadTiers.GetTiers"/>, which tidies a hand-edited list.
        /// A file without a <c>&lt;Tiers&gt;</c> element starts from the seed (the built-in High and Standard tiers); an empty element
        /// means no tiers. The serializer only keeps this initial value when the element is absent.
        /// </remarks>
        public DownloadTier[] Tiers { get; set; } = DownloadTiers.CreateSeedTiers();

        /// <summary>
        /// Gets or sets the id of the tier a user gets when they have not chosen one.
        /// </summary>
        /// <remarks>
        /// Empty, or naming a missing or disabled tier, falls back to the first enabled tier.
        /// </remarks>
        public string? DefaultTierId { get; set; } = DownloadTiers.SeedDefaultTierId;

        /// <summary>
        /// Gets or sets what the plain Download does. Defaults to
        /// <see cref="DownloadBehaviour.SeparateAction"/>, so turning the feature on adds an action
        /// rather than changing the existing one.
        /// </summary>
        public DownloadBehaviour Behaviour { get; set; } = DownloadBehaviour.SeparateAction;

        /// <summary>
        /// Gets or sets a value indicating whether a user may choose the original file instead of
        /// an optimised copy. Only offered under <see cref="DownloadBehaviour.Substitute"/>.
        /// </summary>
        /// <remarks>
        /// A permission rather than a tier: it is never anybody's default, so it is on by default.
        /// Under <see cref="DownloadBehaviour.SeparateAction"/> the plain Download already serves
        /// the original, and an Optimised Download honouring the choice would mislabel the file.
        /// </remarks>
        public bool AllowOriginal { get; set; } = true;
    }
}
