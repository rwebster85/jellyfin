using System;
using System.Collections.Generic;
using System.Linq;

namespace MediaBrowser.Model.Configuration
{
    /// <summary>
    /// Reads the quality tiers out of the download options: which ones exist, which a user may
    /// choose, which one they get if they have not chosen, and the order they are searched in.
    /// </summary>
    /// <remarks>
    /// Every consumer goes through here rather than reading <see cref="DownloadOptions.Tiers"/>
    /// directly, because a stored tier list has to be tidied before it can be trusted.
    /// </remarks>
    public static class DownloadTiers
    {
        private const string HighSuffix = "High";

        private const string StandardSuffix = "Standard";

        /// <summary>
        /// The seed's ids are fixed rather than generated, so a server that has not saved its
        /// settings yet hands out the same ids on every read. A fresh id per read would move what a
        /// user's stored choice points at between one request and the next.
        /// </summary>
        private const string HighId = "8d3a6f1e7c4b4a2d9e5f1b0c2d3e4f50";

        private const string StandardId = "8d3a6f1e7c4b4a2d9e5f1b0c2d3e4f51";

        /// <summary>
        /// The tier the seed nominates as its default. Set outright rather than left to the
        /// "first enabled tier" fall-back, so that the dashboard's Default column has a button
        /// selected the first time an administrator opens it.
        /// </summary>
        public const string SeedDefaultTierId = HighId;

        /// <summary>
        /// The stored preference value meaning "give me the original file", which is a choice a user
        /// can make but not a tier (see <see cref="DownloadOptions.AllowOriginal"/>).
        /// </summary>
        /// <remarks>
        /// Stored where a tier id goes, so it has to be something no tier can be: every generated id
        /// is 32 hex characters, and <see cref="PrepareForSave"/> refuses it as a hand-written one.
        /// It is checked before a stored value is resolved against the tiers, never by
        /// <see cref="Find"/>, which only ever answers with a tier.
        /// </remarks>
        public const string OriginalId = "original";

        private const string HighName = "1080p";

        private const string StandardName = "720p";

        /// <summary>
        /// Creates the tiers a server starts from - working examples rather than placeholders, so
        /// that dropping a rendition into a download location works without configuring anything
        /// first.
        /// </summary>
        /// <returns>A fresh seed, which the caller owns.</returns>
        public static DownloadTier[] CreateSeedTiers() =>
        [
            new DownloadTier { Id = HighId, Suffix = HighSuffix, Name = HighName },
            new DownloadTier { Id = StandardId, Suffix = StandardSuffix, Name = StandardName }
        ];

        /// <summary>
        /// Gets the tiers this server has, in the order the administrator arranged them.
        /// </summary>
        /// <param name="options">The download options.</param>
        /// <returns>The tiers, tidied and in order. Empty when the administrator has defined none.</returns>
        /// <remarks>
        /// A suffix is trimmed, a tier without one is dropped, and a suffix already used is dropped
        /// rather than shadowing the first. The write path rejects all three (see
        /// <see cref="PrepareForSave"/>), so this only ever catches a hand-edited file - but an
        /// empty suffix would match nearly every file name, which is worth being certain about
        /// rather than trusting.
        ///
        /// An empty result means what it says: this server has no tiers, and only a download folder
        /// holding one plainly-named video is served.
        /// </remarks>
        public static IReadOnlyList<DownloadTier> GetTiers(DownloadOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            var tiers = new List<DownloadTier>(options.Tiers.Length);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var tier in options.Tiers)
            {
                var suffix = tier?.Suffix?.Trim();
                if (string.IsNullOrEmpty(suffix) || !seen.Add(suffix))
                {
                    continue;
                }

                var name = tier!.Name?.Trim();

                tiers.Add(new DownloadTier
                {
                    Id = tier.Id?.Trim() ?? string.Empty,
                    Suffix = suffix,
                    Name = string.IsNullOrEmpty(name) ? suffix : name,
                    Description = tier.Description,
                    Enabled = tier.Enabled
                });
            }

            return tiers;
        }

        /// <summary>
        /// Gets the tiers a user may choose from, in order.
        /// </summary>
        /// <param name="options">The download options.</param>
        /// <returns>The enabled tiers.</returns>
        public static IReadOnlyList<DownloadTier> GetEnabled(DownloadOptions options)
            => [.. GetTiers(options).Where(tier => tier.Enabled)];

        /// <summary>
        /// Gets the tier a user who has not chosen one gets.
        /// </summary>
        /// <param name="options">The download options.</param>
        /// <returns>The default tier, or <c>null</c> if no tier is enabled.</returns>
        /// <remarks>
        /// <see cref="DownloadOptions.DefaultTierId"/> names it explicitly. A default naming no
        /// enabled tier - because it was disabled or deleted outside the dashboard - falls back to
        /// the first enabled tier rather than leaving users with nothing.
        /// </remarks>
        public static DownloadTier? GetDefault(DownloadOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            return ResolveDefault(GetEnabled(options), options.DefaultTierId);
        }

        /// <summary>
        /// Finds the tier a stored choice refers to, whether or not it is still enabled.
        /// </summary>
        /// <param name="tiers">The tiers to search.</param>
        /// <param name="stored">A tier id.</param>
        /// <returns>The tier, or <c>null</c> when nothing matches.</returns>
        /// <remarks>
        /// Matched on the id alone, never on the suffix. A suffix is editable, so two tiers can
        /// swap suffixes over a server's life - a stored value that resolved by name would then
        /// mean a different tier than the one it was saved against.
        /// </remarks>
        public static DownloadTier? Find(IReadOnlyList<DownloadTier> tiers, string? stored)
        {
            ArgumentNullException.ThrowIfNull(tiers);

            if (string.IsNullOrEmpty(stored))
            {
                return null;
            }

            var wanted = stored.Trim();

            return tiers.FirstOrDefault(tier => !string.IsNullOrEmpty(tier.Id) && string.Equals(tier.Id, wanted, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets the tier suffixes to look for, in the order they should be tried.
        /// </summary>
        /// <param name="options">The download options.</param>
        /// <param name="preferred">The user's stored choice, or <c>null</c>.</param>
        /// <returns>The suffixes to try, in order. Empty when no tier is defined at all.</returns>
        /// <remarks>
        /// The user's own tier leads when it is still one they may have; otherwise the server
        /// default does. That second part matters more than it used to: the default is now an
        /// explicit setting rather than whichever tier happens to sit at the top, so a user who has
        /// not chosen has to be pointed at it rather than at the first row.
        ///
        /// A tier the administrator has not enabled is still searched, last. Enabling a tier decides
        /// what a user may <em>choose</em>; it is not a rule about which files may ever be served,
        /// because the alternative to serving a rendition of an unenabled tier is serving the item's
        /// own file, which is larger than any rendition.
        /// </remarks>
        public static IReadOnlyList<string> GetSearchOrder(DownloadOptions options, string? preferred)
        {
            var tiers = GetTiers(options);
            var enabled = tiers.Where(tier => tier.Enabled).ToList();

            // Null when the user has not chosen, or chose a tier the administrator has since turned
            // off. Either way it stops being a preference and the server default leads.
            var chosen = Find(enabled, preferred) ?? ResolveDefault(enabled, options.DefaultTierId);

            var order = new List<string>(tiers.Count);

            if (chosen is not null)
            {
                order.Add(chosen.Suffix);
            }

            order.AddRange(enabled
                .Where(tier => !ReferenceEquals(tier, chosen))
                .Select(tier => tier.Suffix));

            order.AddRange(tiers
                .Where(tier => !tier.Enabled)
                .Select(tier => tier.Suffix));

            return order;
        }

        /// <summary>
        /// Gives every tier an id if it has none, and throws if the list cannot be used.
        /// </summary>
        /// <param name="options">The download options about to be stored.</param>
        /// <exception cref="ArgumentException">A tier has no suffix, two share a suffix or an id, or
        /// the default names no enabled tier.</exception>
        /// <remarks>
        /// Called from the configuration store, which hands over the object it is about to cache and
        /// write - so assigning an id here is what persists it. That is how a tier added by hand, or
        /// by an API caller that does not know about ids, gets one.
        ///
        /// The suffix is user input that feeds file name matching, so it is worth refusing rather
        /// than tidying away: an empty one would match nearly everything, and two tiers sharing a
        /// suffix means one of them can never be served and the administrator cannot see which.
        ///
        /// The default is checked for the same reason. Pointing it at a tier that is missing or
        /// disabled is indistinguishable, from the outside, from not setting it at all - the server
        /// carries on with the first enabled tier either way - so the moment to say so is now.
        /// </remarks>
        public static void PrepareForSave(DownloadOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            var suffixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var tier in options.Tiers)
            {
                if (tier is null)
                {
                    throw new ArgumentException("A download tier is missing.");
                }

                var suffix = tier.Suffix?.Trim();

                if (string.IsNullOrEmpty(suffix))
                {
                    throw new ArgumentException("Every download tier needs a suffix - it is what file names are matched on.");
                }

                if (!suffixes.Add(suffix))
                {
                    throw new ArgumentException($"Two download tiers use the suffix '{suffix}'. Each tier needs its own, or only one of them can ever be served.");
                }

                var id = tier.Id?.Trim();

                if (string.IsNullOrEmpty(id))
                {
                    id = Guid.NewGuid().ToString("N");
                }
                else if (IsOriginal(id))
                {
                    throw new ArgumentException($"'{OriginalId}' cannot be a download tier's id - it is what a user's saved choice of the original file is stored as.");
                }
                else if (!ids.Add(id))
                {
                    throw new ArgumentException($"Two download tiers share the id '{id}'. Ids are what a user's saved choice points at, so they cannot be reused.");
                }

                tier.Id = id;
                tier.Suffix = suffix;
                tier.Name = tier.Name?.Trim() ?? string.Empty;
            }

            var defaultTierId = options.DefaultTierId?.Trim();

            if (!string.IsNullOrEmpty(defaultTierId))
            {
                var named = Find(GetTiers(options), defaultTierId);

                if (named is null)
                {
                    // Names no tier at all, which is what leaving the field out looks like: the
                    // property starts on the seed's default, so a caller sending its own tiers and
                    // no default arrives here carrying an id that means nothing to them. Clearing
                    // it hands them the first enabled tier, which is what they asked for by saying
                    // nothing.
                    defaultTierId = null;
                }
                else if (!named.Enabled)
                {
                    // Naming a tier that is right there but switched off is different: it is a
                    // choice, and it behaves exactly like no choice at all, so it is worth refusing
                    // at the moment it is written rather than quietly doing something else.
                    throw new ArgumentException("The default download tier is disabled, so no user can be given it. Enable it, pick another, or leave the default unset.");
                }
            }

            options.DefaultTierId = defaultTierId;
        }

        /// <summary>
        /// Whether a stored choice is the original file rather than a tier.
        /// </summary>
        /// <param name="stored">A stored choice.</param>
        /// <returns><c>true</c> when it is <see cref="OriginalId"/>.</returns>
        public static bool IsOriginal(string? stored)
            => string.Equals(stored?.Trim(), OriginalId, StringComparison.OrdinalIgnoreCase);

        private static DownloadTier? ResolveDefault(IReadOnlyList<DownloadTier> enabled, string? defaultTierId)
            => Find(enabled, defaultTierId) ?? (enabled.Count > 0 ? enabled[0] : null);
    }
}
