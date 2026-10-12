using System;
using System.Collections.Generic;
using System.IO;
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
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;

namespace Jellyfin.Api.Helpers
{
    /// <summary>
    /// Resolves which download version a user is served, opens and describes it, and reads and
    /// writes the tier they chose.
    /// </summary>
    /// <param name="serverConfigurationManager">Instance of the <see cref="IServerConfigurationManager"/> interface.</param>
    /// <param name="displayPreferencesManager">Instance of the <see cref="IDisplayPreferencesManager"/> interface.</param>
    /// <param name="mediaEncoder">Instance of the <see cref="IMediaEncoder"/> interface.</param>
    /// <param name="mediaSourceManager">Instance of the <see cref="IMediaSourceManager"/> interface.</param>
    /// <param name="memoryCache">Instance of the <see cref="IMemoryCache"/> interface.</param>
    /// <param name="downloadTranscoder">Instance of <see cref="DownloadTranscoder"/>.</param>
    public class DownloadHelper(
        IServerConfigurationManager serverConfigurationManager,
        IDisplayPreferencesManager displayPreferencesManager,
        IMediaEncoder mediaEncoder,
        IMediaSourceManager mediaSourceManager,
        IMemoryCache memoryCache,
        DownloadTranscoder downloadTranscoder)
    {
        /// <summary>
        /// Gets the download options. Read on each use, so a saved change applies without a restart.
        /// </summary>
        private DownloadOptions Options
            => serverConfigurationManager.GetConfiguration<DownloadOptions>(DownloadConfigurationStore.StoreKey);

        /// <summary>
        /// Finds where this user's copy of an item belongs: their tier, the locations copies are kept
        /// in, and the copy's path within them. Whether the copy exists yet is the caller's question.
        /// </summary>
        /// <param name="item">The item being downloaded. Its own id keys the copy, so each version of
        /// a film has its own.</param>
        /// <param name="userId">The requesting user's id, or <see cref="Guid.Empty"/> for an API key.</param>
        /// <returns>Where the copy belongs, or <c>null</c> when the feature is off, there is no tier to
        /// serve, or the item has no file.</returns>
        public DownloadCopyTarget? GetCopyTarget(BaseItem item, Guid userId)
        {
            ArgumentNullException.ThrowIfNull(item);

            var tier = GetUserTier(userId) ?? GetDefaultTier();
            if (tier is null || string.IsNullOrEmpty(item.Path))
            {
                return null;
            }

            var locations = DownloadStorage.GetLocations(Options, serverConfigurationManager.ApplicationPaths.DataPath);
            var relativePath = DownloadStorage.GetRelativePath(item.Id, tier.Id);

            return new DownloadCopyTarget(tier, locations, relativePath);
        }

        /// <summary>
        /// Finds where the copy the plain download route serves in place of the item's own file
        /// belongs: only under <see cref="DownloadBehaviour.Substitute"/>, and not for a user who
        /// chose the original.
        /// </summary>
        /// <param name="item">The item being downloaded.</param>
        /// <param name="userId">The requesting user's id, or <see cref="Guid.Empty"/> for an API key.</param>
        /// <returns>Where the copy belongs, or <c>null</c> if the item's own file should be served.</returns>
        /// <remarks>
        /// The optimised routes use <see cref="GetCopyTarget"/> instead: they mean the optimised copy
        /// whatever the behaviour or the user's choice.
        /// </remarks>
        public DownloadCopyTarget? GetCopyTargetForPlainDownload(BaseItem item, Guid userId)
        {
            var options = Options;
            var isSubstitute = options.Behaviour == DownloadBehaviour.Substitute;
            var userChoseOriginal = ChoseOriginal(options, userId);

            return isSubstitute && !userChoseOriginal
                ? GetCopyTarget(item, userId)
                : null;
        }

        /// <summary>
        /// Opens a copy to serve: the finished file if it is current, or else the copy as it is made,
        /// starting the encode or joining one already running.
        /// </summary>
        /// <param name="item">The item being downloaded.</param>
        /// <param name="target">Where the copy belongs.</param>
        /// <param name="httpContext">The request asking for the copy, needed to start an encode.</param>
        /// <returns>The copy, finished or growing.</returns>
        /// <exception cref="IOException">No location has room for a copy that has to be made.</exception>
        public async Task<DownloadCopy> OpenCopyAsync(BaseItem item, DownloadCopyTarget target, HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(item);
            ArgumentNullException.ThrowIfNull(target);

            // Named after the source, not the copy's own file, which is named by its ids.
            var tierName = string.IsNullOrWhiteSpace(target.Tier.Suffix) ? target.Tier.Name : target.Tier.Suffix;
            var name = Path.GetFileNameWithoutExtension(item.Path) + " - " + tierName + DownloadStorage.Extension;

            var finished = FindCurrentCopy(item, target);
            if (finished is not null)
            {
                return new DownloadCopy(name, finished, null);
            }

            var stream = await downloadTranscoder.OpenAsync(item, target.Tier.Id, target.Locations, target.RelativePath, httpContext)
                .ConfigureAwait(false);

            return new DownloadCopy(name, null, stream);
        }

        /// <summary>
        /// Describes what a finished copy actually contains.
        /// </summary>
        /// <param name="item">The item the copy belongs to.</param>
        /// <param name="user">The requesting user, or <c>null</c> for an API key.</param>
        /// <param name="target">Where the copy belongs.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The probed <see cref="MediaSourceInfo"/>, or <c>null</c> if the copy is not
        /// finished, is stale, or has gone.</returns>
        public async Task<MediaSourceInfo?> DescribeCopyAsync(BaseItem item, User? user, DownloadCopyTarget target, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(item);
            ArgumentNullException.ThrowIfNull(target);

            // A copy not made yet, or still being made, cannot be described.
            var path = FindCurrentCopy(item, target);

            return path is null
                ? null
                : await DescribeAsync(item, user, path, cancellationToken).ConfigureAwait(false);
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

        /// <summary>
        /// Finds a finished copy made from the item as it is now, with the tier's current settings.
        /// </summary>
        /// <returns>The copy's path, or <c>null</c> if it has to be made.</returns>
        private string? FindCurrentCopy(BaseItem item, DownloadCopyTarget target)
        {
            var path = DownloadStorage.FindFinished(target.Locations, target.RelativePath);

            return path is not null && downloadTranscoder.IsCurrent(path, item, target.Tier.Id) ? path : null;
        }

        /// <summary>
        /// Probes a copy and describes what it actually contains.
        /// </summary>
        /// <returns>The probed <see cref="MediaSourceInfo"/>, or <c>null</c> if the file has gone.</returns>
        /// <remarks>
        /// Sets the user's default audio and subtitle streams as a playback request would, since a
        /// client playing the file offline starts on them. The probe is cached across users, so this
        /// works on a copy.
        /// </remarks>
        private async Task<MediaSourceInfo?> DescribeAsync(BaseItem item, User? user, string path, CancellationToken cancellationToken)
        {
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
    }
}
