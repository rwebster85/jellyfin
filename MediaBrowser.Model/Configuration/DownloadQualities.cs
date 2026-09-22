using System;
using System.Collections.Generic;
using System.Linq;

namespace MediaBrowser.Model.Configuration
{
    /// <summary>
    /// The quality tiers a download version can be made at.
    /// </summary>
    /// <remarks>
    /// These names are a contract with the tool that produces the files: a rendition is named
    /// <c>&lt;source stem&gt; - &lt;Tier&gt;.&lt;ext&gt;</c>, so renaming a tier here without renaming it
    /// there makes every download silently fall through to the original.
    /// </remarks>
    public static class DownloadQualities
    {
        /// <summary>
        /// The larger tier - 1080p.
        /// </summary>
        public const string High = "High";

        /// <summary>
        /// The smaller tier - 720p.
        /// </summary>
        public const string Standard = "Standard";

        /// <summary>
        /// Gets every known tier, best first. This order is what makes the default and the
        /// fall-back predictable: the first enabled tier is the server's default, and a request
        /// for a tier with no file tries the rest in this order.
        /// </summary>
        public static IReadOnlyList<string> All { get; } = [High, Standard];

        /// <summary>
        /// Gets the tiers an admin has enabled, in <see cref="All"/>'s order and with anything
        /// unrecognised dropped.
        /// </summary>
        /// <param name="options">The download options.</param>
        /// <returns>The enabled tiers.</returns>
        public static IReadOnlyList<string> GetEnabled(DownloadOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            return [.. All.Where(quality => options.Qualities.Contains(quality, StringComparer.OrdinalIgnoreCase))];
        }

        /// <summary>
        /// Gets the tier used by a user who has not chosen one - the best tier the admin enabled.
        /// </summary>
        /// <param name="options">The download options.</param>
        /// <returns>The default tier, or <c>null</c> if the admin has enabled none.</returns>
        public static string? GetDefault(DownloadOptions options)
        {
            var enabled = GetEnabled(options);

            return enabled.Count > 0 ? enabled[0] : null;
        }

        /// <summary>
        /// Gets the tiers to look for, in the order they should be tried: the user's own tier
        /// first when it is one the admin enabled, then the rest of the enabled tiers, and finally
        /// the tiers the admin has not enabled.
        /// </summary>
        /// <param name="options">The download options.</param>
        /// <param name="preferred">The user's chosen tier, or <c>null</c>.</param>
        /// <returns>The tiers to try, in order.</returns>
        /// <remarks>
        /// A tier the admin has not enabled is still searched, last. Enabling a tier decides what a
        /// user may <em>choose</em> and what the default is; it is not a rule about which files may
        /// ever be served. The alternative to serving a rendition of an unenabled tier is serving
        /// the item's own file, which is larger than any rendition - so treating the tick as a
        /// prohibition achieves the opposite of what unticking it was for.
        /// </remarks>
        public static IReadOnlyList<string> GetSearchOrder(DownloadOptions options, string? preferred)
        {
            var enabled = GetEnabled(options);

            // Null when the user has not chosen, or chose a tier the admin has since turned off.
            // Either way it stops being a preference and the enabled order stands.
            var chosen = string.IsNullOrEmpty(preferred)
                ? null
                : enabled.FirstOrDefault(quality => string.Equals(quality, preferred, StringComparison.OrdinalIgnoreCase));

            var order = new List<string>(All.Count);

            if (chosen is not null)
            {
                order.Add(chosen);
            }

            order.AddRange(enabled.Where(quality => !string.Equals(quality, chosen, StringComparison.Ordinal)));
            order.AddRange(All.Where(quality => !enabled.Contains(quality, StringComparer.OrdinalIgnoreCase)));

            return order;
        }
    }
}
