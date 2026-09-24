using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Api.Helpers
{
    /// <summary>
    /// Resolves which download version a user is served, describes it, and reads and writes the
    /// tier they chose.
    /// </summary>
    /// <param name="serverConfigurationManager">Instance of the <see cref="IServerConfigurationManager"/> interface.</param>
    /// <param name="displayPreferencesManager">Instance of the <see cref="IDisplayPreferencesManager"/> interface.</param>
    /// <param name="mediaEncoder">Instance of the <see cref="IMediaEncoder"/> interface.</param>
    /// <param name="mediaSourceManager">Instance of the <see cref="IMediaSourceManager"/> interface.</param>
    /// <param name="memoryCache">Instance of the <see cref="IMemoryCache"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{DownloadHelper}"/> interface.</param>
    public class DownloadHelper(
        IServerConfigurationManager serverConfigurationManager,
        IDisplayPreferencesManager displayPreferencesManager,
        IMediaEncoder mediaEncoder,
        IMediaSourceManager mediaSourceManager,
        IMemoryCache memoryCache,
        ILogger<DownloadHelper> logger)
    {
        /// <summary>
        /// Gets the download options. Read on each use, so a saved change applies without a restart.
        /// </summary>
        private DownloadOptions Options
            => serverConfigurationManager.GetConfiguration<DownloadOptions>(DownloadConfigurationStore.StoreKey);

        /// <summary>
        /// Finds the download version of a file at this user's tier, falling back to the other
        /// tiers. Always <c>null</c> while the feature is off.
        /// </summary>
        /// <param name="path">The path of the item being downloaded.</param>
        /// <param name="userId">The requesting user's id, or <see cref="Guid.Empty"/> for an API key.</param>
        /// <returns>The path of the download version, or <c>null</c> if there isn't one.</returns>
        public string? FindForUser(string? path, Guid userId)
        {
            var options = Options;

            return options.Enabled
                ? DownloadLocator.FindDownloadVersion(
                    options,
                    path,
                    DownloadPreferences.GetTier(displayPreferencesManager, userId),
                    logger)
                : null;
        }

        /// <summary>
        /// Finds the download version the plain download route should serve in place of the item's
        /// own file: only under <see cref="DownloadBehaviour.Substitute"/>, and not for a user who
        /// chose the original.
        /// </summary>
        /// <param name="path">The path of the item being downloaded.</param>
        /// <param name="userId">The requesting user's id, or <see cref="Guid.Empty"/> for an API key.</param>
        /// <returns>The path of the download version, or <c>null</c> if it should not substitute.</returns>
        /// <remarks>
        /// The optimised routes use <see cref="FindForUser"/> instead: they mean the optimised file
        /// whatever the behaviour or the user's choice.
        /// </remarks>
        public string? FindForPlainDownload(string? path, Guid userId)
        {
            var options = Options;

            return options.Behaviour == DownloadBehaviour.Substitute && !ChoseOriginal(options, userId)
                ? FindForUser(path, userId)
                : null;
        }

        /// <summary>
        /// Probes a download version and describes what it actually contains.
        /// </summary>
        /// <param name="item">The item the download version belongs to.</param>
        /// <param name="user">The requesting user, or <c>null</c> for an API key.</param>
        /// <param name="path">The path of the download version.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The probed <see cref="MediaSourceInfo"/>, or <c>null</c> if the file has gone.</returns>
        /// <remarks>
        /// Sets the user's default audio and subtitle streams as a playback request would, since a
        /// client playing the file offline starts on them. The probe is cached across users, so this
        /// works on a copy.
        /// </remarks>
        public async Task<MediaSourceInfo?> DescribeAsync(BaseItem item, User? user, string path, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(item);

            var probed = await DownloadProbeHelper.ProbeAsync(mediaEncoder, memoryCache, item.Id, path, cancellationToken)
                .ConfigureAwait(false);

            if (probed is null || user is null)
            {
                return probed;
            }

            // The same clone MediaInfoHelper makes before it sets anything on a shared source.
            var source = JsonSerializer.Deserialize<MediaSourceInfo>(JsonSerializer.SerializeToUtf8Bytes(probed)) ?? probed;
            mediaSourceManager.SetDefaultAudioAndSubtitleStreamIndices(item, source, user);

            return source;
        }

        /// <summary>
        /// Gets whether optimised downloads are on, and what the plain Download does - all a client
        /// needs to decide which download actions to offer.
        /// </summary>
        /// <returns>The feature switch and the behaviour.</returns>
        public (bool Enabled, DownloadBehaviour Behaviour) GetBehaviour()
        {
            var options = Options;

            return (options.Enabled, options.Behaviour);
        }

        /// <summary>
        /// Gets a value indicating whether users are offered the original file as a choice.
        /// </summary>
        /// <returns><c>true</c> when the original may be chosen.</returns>
        public bool OffersOriginal() => OffersOriginal(Options);

        /// <summary>
        /// Gets a value indicating whether a user chose the original file and it is still offered.
        /// A withdrawn choice is kept, and counts again once it is offered again.
        /// </summary>
        /// <param name="userId">The user id.</param>
        /// <returns><c>true</c> when this user's downloads are not substituted.</returns>
        public bool ChoseOriginal(Guid userId) => ChoseOriginal(Options, userId);

        /// <summary>
        /// Gets the tiers the admin has enabled, in order. Empty while the feature is off, since the
        /// seeded tiers would otherwise be offered on a server that never serves them.
        /// </summary>
        /// <returns>The enabled tiers.</returns>
        public IReadOnlyList<DownloadTier> GetEnabledTiers()
        {
            var options = Options;

            return options.Enabled ? DownloadTiers.GetEnabled(options) : [];
        }

        /// <summary>
        /// Gets the tier used by a user who has not chosen one.
        /// </summary>
        /// <returns>The default tier, or <c>null</c> if there is none to be had.</returns>
        public DownloadTier? GetDefaultTier()
        {
            var options = Options;

            return options.Enabled ? DownloadTiers.GetDefault(options) : null;
        }

        /// <summary>
        /// Gets the tier a user chose, as long as the admin still has it enabled.
        /// </summary>
        /// <param name="userId">The user id.</param>
        /// <returns>The tier in effect for this user, or <c>null</c> if they follow the default.</returns>
        public DownloadTier? GetUserTier(Guid userId)
            => ResolveEnabledTier(DownloadPreferences.GetTier(displayPreferencesManager, userId));

        /// <summary>
        /// Sets the tier a user chooses for themselves, or clears it so they follow the default.
        /// </summary>
        /// <param name="userId">The user id.</param>
        /// <param name="tierId">The tier's id, or <c>null</c> to follow the default.</param>
        public void SetUserTier(Guid userId, string? tierId)
            => DownloadPreferences.SetTier(displayPreferencesManager, userId, tierId);

        /// <summary>
        /// Matches a stored choice against the enabled tiers.
        /// </summary>
        /// <param name="stored">A tier id.</param>
        /// <returns>The tier, or <c>null</c> if it is not one the admin has enabled.</returns>
        public DownloadTier? ResolveEnabledTier(string? stored)
            => DownloadTiers.Find(GetEnabledTiers(), stored);

        private static bool OffersOriginal(DownloadOptions options)
            => options.Enabled && options.Behaviour == DownloadBehaviour.Substitute && options.AllowOriginal;

        private bool ChoseOriginal(DownloadOptions options, Guid userId)
            => OffersOriginal(options)
                && DownloadTiers.IsOriginal(DownloadPreferences.GetTier(displayPreferencesManager, userId));
    }
}
