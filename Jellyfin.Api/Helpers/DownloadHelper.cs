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

namespace Jellyfin.Api.Helpers
{
    /// <summary>
    /// The download feature's service layer: resolves the download version a given user should be
    /// served, describes it, and reads and writes the tier they chose.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="DownloadHelper"/> class.
    /// </remarks>
    /// <param name="serverConfigurationManager">Instance of the <see cref="IServerConfigurationManager"/> interface.</param>
    /// <param name="displayPreferencesManager">Instance of the <see cref="IDisplayPreferencesManager"/> interface.</param>
    /// <param name="mediaEncoder">Instance of the <see cref="IMediaEncoder"/> interface.</param>
    /// <param name="mediaSourceManager">Instance of the <see cref="IMediaSourceManager"/> interface.</param>
    /// <param name="memoryCache">Instance of the <see cref="IMemoryCache"/> interface.</param>
    public class DownloadHelper(
        IServerConfigurationManager serverConfigurationManager,
        IDisplayPreferencesManager displayPreferencesManager,
        IMediaEncoder mediaEncoder,
        IMediaSourceManager mediaSourceManager,
        IMemoryCache memoryCache)
    {
        /// <summary>
        /// Gets the download options the admin has configured.
        /// </summary>
        /// <remarks>
        /// Read on each use rather than cached: the configuration manager already holds the parsed
        /// object, and an admin saving the settings page has to take effect without a restart.
        /// </remarks>
        private DownloadOptions Options
            => serverConfigurationManager.GetConfiguration<DownloadOptions>(DownloadConfigurationStore.StoreKey);

        /// <summary>
        /// Finds the download version of a file at the tier this user chose, falling back to the
        /// other tiers when that one has no file.
        /// </summary>
        /// <param name="path">The path of the item being downloaded.</param>
        /// <param name="userId">The requesting user's id, or <see cref="Guid.Empty"/> for an API key.</param>
        /// <returns>The path of the download version, or <c>null</c> if there isn't one.</returns>
        /// <remarks>
        /// The single place that answers "is there an optimised file for this user", so it is also
        /// where <see cref="DownloadOptions.Enabled"/> is honoured: with the feature off there is
        /// never one, which leaves the plain route serving the item's own file and the
        /// <c>Download/Optimised</c> pair answering 404.
        /// </remarks>
        public string? FindForUser(string? path, Guid userId)
        {
            var options = Options;

            return options.Enabled
                ? DownloadLocator.FindDownloadVersion(
                    options,
                    path,
                    DownloadPreferences.GetTier(displayPreferencesManager, userId))
                : null;
        }

        /// <summary>
        /// Finds the download version the plain download route should serve in place of an item's
        /// own file, which is nothing at all when the admin has chosen to offer the optimised copy
        /// as a separate action instead.
        /// </summary>
        /// <param name="path">The path of the item being downloaded.</param>
        /// <param name="userId">The requesting user's id, or <see cref="Guid.Empty"/> for an API key.</param>
        /// <returns>The path of the download version, or <c>null</c> if it should not substitute.</returns>
        /// <remarks>
        /// Only the plain route asks this. The <c>Download/Optimised</c> pair means "the optimised
        /// file" whatever the setting says, so it goes on using <see cref="FindForUser"/>: the
        /// setting decides what the plain button does, not whether the optimised copy is reachable.
        ///
        /// A user who chose the original is the one exception, and it is checked here rather than
        /// in <see cref="FindForUser"/> for the same reason: choosing the original opts out of
        /// substitution, which is only something the plain route does.
        /// </remarks>
        public string? FindForPlainDownload(string? path, Guid userId)
        {
            var options = Options;

            return options.Behaviour == DownloadBehaviour.Substitute && !ChoseOriginal(options, userId)
                ? FindForUser(path, userId)
                : null;
        }

        /// <summary>
        /// Probes a download version and returns what it actually contains, so the server can hand
        /// over a rendition and its own <see cref="MediaSourceInfo"/> rather than the source's.
        /// </summary>
        /// <param name="item">The item the download version belongs to.</param>
        /// <param name="user">The requesting user, or <c>null</c> for an API key.</param>
        /// <param name="path">The path of the download version.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The probed <see cref="MediaSourceInfo"/>, or <c>null</c> if the file has gone.</returns>
        /// <remarks>
        /// The default audio and subtitle streams are chosen for this user, the way a playback
        /// request chooses them, because a client that stores this description to play the file
        /// offline has nothing else to go on - the Android app starts on the default audio stream
        /// it is given. The probe itself is cached and shared between users, so the choice is made
        /// on a copy.
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
        /// Gets a value indicating whether users are offered the original file as a choice alongside
        /// the tiers.
        /// </summary>
        /// <returns><c>true</c> when the original may be chosen.</returns>
        /// <remarks>
        /// Only under <see cref="DownloadBehaviour.Substitute"/>: see
        /// <see cref="DownloadOptions.AllowOriginal"/> for why the choice means nothing otherwise.
        /// </remarks>
        public bool OffersOriginal() => OffersOriginal(Options);

        /// <summary>
        /// Gets a value indicating whether a user has chosen the original file and may still have it.
        /// </summary>
        /// <param name="userId">The user id.</param>
        /// <returns><c>true</c> when this user's downloads are not substituted.</returns>
        /// <remarks>
        /// A choice of the original that the administrator has since stopped offering behaves exactly
        /// like no choice at all, the same as a tier that has been turned off: the stored value is
        /// kept, so offering it again brings it back.
        /// </remarks>
        public bool ChoseOriginal(Guid userId) => ChoseOriginal(Options, userId);

        /// <summary>
        /// Gets the tiers the admin has enabled, in the order they arranged them.
        /// </summary>
        /// <returns>The enabled tiers, or nothing at all while the feature is off.</returns>
        /// <remarks>
        /// <see cref="DownloadOptions.Enabled"/> is honoured here as well as in
        /// <see cref="FindForUser"/>, because this is what a user is offered rather than what they
        /// are served. With the feature off no optimised file is ever served, so offering a choice
        /// between tiers would be a control that cannot change anything - and the tiers are seeded,
        /// so a server that has never turned the feature on would still show two of them.
        /// </remarks>
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
        /// Gets the tier a user chose, as long as it is one the admin still has enabled.
        /// </summary>
        /// <param name="userId">The user id.</param>
        /// <returns>The tier in effect for this user, or <c>null</c> if they follow the default.</returns>
        /// <remarks>
        /// A tier the admin has since turned off behaves exactly like no choice at all, so it is
        /// reported as none: saying otherwise would have the client display a preference that
        /// changes nothing.
        /// </remarks>
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
        /// Matches a stored choice against the tiers a user may actually have.
        /// </summary>
        /// <param name="stored">A tier id.</param>
        /// <returns>The tier, or <c>null</c> if it is not one the admin has enabled.</returns>
        /// <remarks>
        /// The tier is returned rather than the id it was matched by, because the file name suffix
        /// is built from it and so must never come from a client's spelling.
        /// </remarks>
        public DownloadTier? ResolveEnabledTier(string? stored)
            => DownloadTiers.Find(GetEnabledTiers(), stored);

        private static bool OffersOriginal(DownloadOptions options)
            => options.Enabled && options.Behaviour == DownloadBehaviour.Substitute && options.AllowOriginal;

        private bool ChoseOriginal(DownloadOptions options, Guid userId)
            => OffersOriginal(options)
                && DownloadTiers.IsOriginal(DownloadPreferences.GetTier(displayPreferencesManager, userId));
    }
}
