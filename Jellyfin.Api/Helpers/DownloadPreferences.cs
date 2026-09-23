using System;
using System.Collections.Generic;
using Jellyfin.Extensions;
using MediaBrowser.Controller;

namespace Jellyfin.Api.Helpers
{
    /// <summary>
    /// Reads and writes a user's own download tier.
    /// </summary>
    /// <remarks>
    /// Kept in <see cref="IDisplayPreferencesManager"/>'s custom item preferences, the server's
    /// per-user key/value store, under the feature's name rather than a client's - so one choice
    /// applies on every client.
    /// </remarks>
    public static class DownloadPreferences
    {
        /// <summary>
        /// The client name these preferences are stored under.
        /// </summary>
        public const string Client = "downloads";

        /// <summary>
        /// The key the chosen tier's id is stored under.
        /// </summary>
        public const string TierKey = "tier";

        /// <summary>
        /// Gets the tier a user chose for themselves.
        /// </summary>
        /// <param name="displayPreferencesManager">Instance of the <see cref="IDisplayPreferencesManager"/> interface.</param>
        /// <param name="userId">The user id.</param>
        /// <returns>The chosen tier's id, <see cref="MediaBrowser.Model.Configuration.DownloadTiers.OriginalId"/>
        /// if they chose the original file, or <c>null</c> if they have not chosen.</returns>
        public static string? GetTier(IDisplayPreferencesManager displayPreferencesManager, Guid userId)
        {
            ArgumentNullException.ThrowIfNull(displayPreferencesManager);

            if (userId.IsEmpty())
            {
                return null;
            }

            var preferences = displayPreferencesManager.ListCustomItemDisplayPreferences(userId, Guid.Empty, Client);

            return preferences.TryGetValue(TierKey, out var tier) && !string.IsNullOrEmpty(tier)
                ? tier
                : null;
        }

        /// <summary>
        /// Sets the tier a user chooses for themselves, or clears it so they follow the server default.
        /// </summary>
        /// <param name="displayPreferencesManager">Instance of the <see cref="IDisplayPreferencesManager"/> interface.</param>
        /// <param name="userId">The user id.</param>
        /// <param name="tier">The tier's id, <see cref="MediaBrowser.Model.Configuration.DownloadTiers.OriginalId"/>,
        /// or <c>null</c> to follow the default.</param>
        public static void SetTier(IDisplayPreferencesManager displayPreferencesManager, Guid userId, string? tier)
        {
            ArgumentNullException.ThrowIfNull(displayPreferencesManager);

            var preferences = new Dictionary<string, string?>();
            if (!string.IsNullOrEmpty(tier))
            {
                preferences[TierKey] = tier;
            }

            // The setter replaces everything stored for this user and client, so an empty dictionary
            // is how a choice is cleared.
            displayPreferencesManager.SetCustomItemDisplayPreferences(userId, Guid.Empty, Client, preferences);
        }
    }
}
