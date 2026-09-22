using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
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
    /// <param name="memoryCache">Instance of the <see cref="IMemoryCache"/> interface.</param>
    public class DownloadHelper(
        IServerConfigurationManager serverConfigurationManager,
        IDisplayPreferencesManager displayPreferencesManager,
        IMediaEncoder mediaEncoder,
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
            => serverConfigurationManager.GetConfiguration<DownloadOptions>("downloads");

        /// <summary>
        /// Finds the download version of a file at the tier this user chose, falling back to the
        /// other enabled tiers when that one has no file.
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
                    DownloadPreferences.GetQuality(displayPreferencesManager, userId))
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
        /// </remarks>
        public string? FindForPlainDownload(string? path, Guid userId) =>
            Options.Behaviour == DownloadBehaviour.Substitute ? FindForUser(path, userId) : null;

        /// <summary>
        /// Probes a download version and returns what it actually contains, so the server can hand
        /// over a rendition and its own <see cref="MediaSourceInfo"/> rather than the source's.
        /// </summary>
        /// <param name="itemId">The id of the item the download version belongs to.</param>
        /// <param name="path">The path of the download version.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The probed <see cref="MediaSourceInfo"/>, or <c>null</c> if the file has gone.</returns>
        public Task<MediaSourceInfo?> ProbeAsync(Guid itemId, string path, CancellationToken cancellationToken)
            => DownloadProbeHelper.ProbeAsync(mediaEncoder, memoryCache, itemId, path, cancellationToken);

        /// <summary>
        /// Gets the tiers the admin has enabled, best first.
        /// </summary>
        /// <returns>The enabled tiers.</returns>
        public IReadOnlyList<string> GetEnabledQualities() => DownloadQualities.GetEnabled(Options);

        /// <summary>
        /// Gets the tier used by a user who has not chosen one.
        /// </summary>
        /// <returns>The default tier, or <c>null</c> if the admin has enabled none.</returns>
        public string? GetDefaultQuality() => DownloadQualities.GetDefault(Options);

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
        public string? GetUserQuality(Guid userId)
            => ResolveEnabledQuality(DownloadPreferences.GetQuality(displayPreferencesManager, userId));

        /// <summary>
        /// Sets the tier a user chooses for themselves, or clears it so they follow the default.
        /// </summary>
        /// <param name="userId">The user id.</param>
        /// <param name="quality">The tier to store, or <c>null</c> to follow the default.</param>
        public void SetUserQuality(Guid userId, string? quality)
            => DownloadPreferences.SetQuality(displayPreferencesManager, userId, quality);

        /// <summary>
        /// Matches a tier against the enabled ones, returning it in its canonical spelling.
        /// </summary>
        /// <param name="quality">The tier to match, however it was spelled.</param>
        /// <returns>The canonical tier, or <c>null</c> if it is not one the admin has enabled.</returns>
        /// <remarks>
        /// The canonical spelling matters because the filename suffix is built from it, so it must
        /// never come from a client's casing.
        /// </remarks>
        public string? ResolveEnabledQuality(string? quality) => string.IsNullOrEmpty(quality)
            ? null
            : GetEnabledQualities().FirstOrDefault(enabled => string.Equals(enabled, quality, StringComparison.OrdinalIgnoreCase));
    }
}
