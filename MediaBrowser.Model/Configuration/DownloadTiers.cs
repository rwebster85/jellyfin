using System;
using System.Collections.Generic;
using System.Linq;

namespace MediaBrowser.Model.Configuration
{
    /// <summary>
    /// Reads the tiers out of the download options: which exist, which a user may choose, the
    /// default, and the search order.
    /// </summary>
    /// <remarks>
    /// Consumers go through here rather than reading <see cref="DownloadOptions.Tiers"/> directly,
    /// because a hand-edited tier list has to be tidied before it can be trusted.
    /// </remarks>
    public static class DownloadTiers
    {
        private const string HighSuffix = "High";

        private const string StandardSuffix = "Standard";

        /// <summary>
        /// The seed's ids are fixed, not generated, so an unsaved server hands out the same ids on
        /// every read and a user's stored choice keeps pointing at the same tier.
        /// </summary>
        private const string HighId = "8d3a6f1e7c4b4a2d9e5f1b0c2d3e4f50";

        private const string StandardId = "8d3a6f1e7c4b4a2d9e5f1b0c2d3e4f51";

        /// <summary>
        /// The seed's default tier, named outright so the dashboard's Default column has a
        /// selection the first time it is opened.
        /// </summary>
        public const string SeedDefaultTierId = HighId;

        /// <summary>
        /// The stored choice meaning "the original file" - a choice, not a tier
        /// (see <see cref="DownloadOptions.AllowOriginal"/>).
        /// </summary>
        /// <remarks>
        /// Stored where a tier id goes, so no tier may use it: <see cref="PrepareForSave"/> refuses
        /// it. Check it with <see cref="IsOriginal"/> before resolving a stored value against the
        /// tiers.
        /// </remarks>
        public const string OriginalId = "original";

        private const string HighName = "1080p";

        private const string StandardName = "720p";

        /// <summary>
        /// Creates the tiers a server starts from: working examples, so a download folder works
        /// before anything is configured.
        /// </summary>
        /// <returns>A new array on every call, so changing it affects nothing else.</returns>
        public static DownloadTier[] CreateSeedTiers() =>
        [
            new DownloadTier { Id = HighId, Suffix = HighSuffix, Name = HighName },
            new DownloadTier { Id = StandardId, Suffix = StandardSuffix, Name = StandardName }
        ];

        /// <summary>
        /// Gets the tiers this server has, in the administrator's order.
        /// </summary>
        /// <param name="options">The download options.</param>
        /// <returns>The tiers, tidied and in order. Empty when the administrator has defined none.</returns>
        /// <remarks>
        /// Trims each suffix and drops a tier with no suffix or a duplicate one. Saving already
        /// refuses both; this guards a hand-edited file, where an empty suffix would match almost
        /// every file name.
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
        /// Gets the tier a user who has not chosen one gets: <see cref="DownloadOptions.DefaultTierId"/>,
        /// or the first enabled tier if that names none.
        /// </summary>
        /// <param name="options">The download options.</param>
        /// <returns>The default tier, or <c>null</c> if no tier is enabled.</returns>
        public static DownloadTier? GetDefault(DownloadOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            return ResolveDefault(GetEnabled(options), options.DefaultTierId);
        }

        /// <summary>
        /// Finds the tier a stored choice refers to, by id alone.
        /// </summary>
        /// <param name="tiers">The tiers to search.</param>
        /// <param name="stored">A tier id.</param>
        /// <returns>The tier, or <c>null</c> when nothing matches.</returns>
        /// <remarks>
        /// Never by suffix: suffixes are editable, so a choice matched by name could come to mean a
        /// different tier from the one it was saved against.
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
        /// Gets the tier suffixes to look for, in the order they should be tried: the user's tier or
        /// else the default, then the other enabled tiers, then the disabled ones.
        /// </summary>
        /// <param name="options">The download options.</param>
        /// <param name="preferred">The user's stored choice, or <c>null</c>.</param>
        /// <returns>The suffixes to try, in order. Empty when no tier is defined at all.</returns>
        /// <remarks>
        /// Disabled tiers are still searched: enabling decides what a user may choose, not what may
        /// be served, and the alternative is the item's own, larger, file.
        /// </remarks>
        public static IReadOnlyList<string> GetSearchOrder(DownloadOptions options, string? preferred)
        {
            var tiers = GetTiers(options);
            var enabled = tiers.Where(tier => tier.Enabled).ToList();

            // A choice the administrator has since disabled no longer counts, so the default leads.
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
        /// <exception cref="ArgumentException">A tier has no suffix, two share a suffix or an id, a
        /// tier uses <see cref="OriginalId"/>, or the default names a disabled tier.</exception>
        /// <remarks>
        /// The configuration store passes the object it is about to write, so ids assigned here are
        /// what get saved.
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
                    // Names no tier at all - what a caller sending its own tiers and no default
                    // looks like, since the property starts on the seed's id. Treated as unset.
                    defaultTierId = null;
                }
                else if (!named.Enabled)
                {
                    // A deliberate choice that would silently do nothing, so it is refused.
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
