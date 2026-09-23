using System;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Api.Attributes;
using Jellyfin.Api.Extensions;
using Jellyfin.Api.Helpers;
using Jellyfin.Api.Models.DownloadDtos;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Extensions;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.IO;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Activity;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Api.Controllers
{
    /// <summary>
    /// Downloads controller: an item's download, its optimised download version, and each user's
    /// download tier.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
    /// <param name="activityManager">Instance of the <see cref="IActivityManager"/> interface.</param>
    /// <param name="localization">Instance of the <see cref="ILocalizationManager"/> interface.</param>
    /// <param name="downloadHelper">Instance of the <see cref="DownloadHelper"/>.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{DownloadsController}"/> interface.</param>
    [Route("")]
    public class DownloadsController(
        ILibraryManager libraryManager,
        IUserManager userManager,
        IActivityManager activityManager,
        ILocalizationManager localization,
        DownloadHelper downloadHelper,
        ILogger<DownloadsController> logger) : BaseJellyfinApiController
    {
        /// <summary>
        /// Downloads item media - an optimised download version in its place when the server is set
        /// to substitute one.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <response code="200">Media downloaded.</response>
        /// <response code="404">Item not found.</response>
        /// <returns>A <see cref="FileResult"/> containing the media stream.</returns>
        /// <exception cref="ArgumentException">User can't download or item can't be downloaded.</exception>
        [HttpGet("Items/{itemId}/Download")]
        [Authorize(Policy = Policies.Download)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesFile("video/*", "audio/*")]
        public async Task<ActionResult> GetDownload([FromRoute, Required] Guid itemId)
        {
            var (item, user) = GetDownloadableItem(itemId);
            if (item is null)
            {
                return NotFound();
            }

            if (user is not null)
            {
                await LogDownloadAsync(item, user).ConfigureAwait(false);
            }

            var downloadVersion = downloadHelper.FindForPlainDownload(item.Path, user?.Id ?? Guid.Empty);
            if (downloadVersion is not null)
            {
                logger.LogInformation("Serving download version {DownloadVersion} in place of {Path}", downloadVersion, item.Path);
            }

            return ServeDownload(item, downloadVersion ?? item.Path);
        }

        /// <summary>
        /// Gets the media info of the file <c>Items/{itemId}/Download</c> would serve in place of the
        /// item's own, if it would substitute one.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <response code="200">Media info of the file that would be served instead.</response>
        /// <response code="204">The item's own file would be served, so its own media info describes it.</response>
        /// <response code="404">Item not found.</response>
        /// <returns>The <see cref="MediaSourceInfo"/> of the substituted version, or no content.</returns>
        /// <exception cref="ArgumentException">User can't download or item can't be downloaded.</exception>
        /// <remarks>
        /// For a client that stores a description of what it downloaded: under substitution the
        /// plain route can serve a file with another container and other tracks.
        /// </remarks>
        [HttpGet("Items/{itemId}/Download/MediaInfo")]
        [Authorize(Policy = Policies.Download)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<MediaSourceInfo>> GetDownloadMediaInfo(
            [FromRoute, Required] Guid itemId,
            CancellationToken cancellationToken)
        {
            var (item, user) = GetDownloadableItem(itemId);
            if (item is null)
            {
                return NotFound();
            }

            var downloadVersion = downloadHelper.FindForPlainDownload(item.Path, user?.Id ?? Guid.Empty);
            if (downloadVersion is null)
            {
                return NoContent();
            }

            var mediaSource = await downloadHelper.DescribeAsync(item, user, downloadVersion, cancellationToken)
                .ConfigureAwait(false);

            return mediaSource is null ? NoContent() : mediaSource;
        }

        /// <summary>
        /// Downloads the optimised version of an item's media.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <response code="200">Media downloaded.</response>
        /// <response code="404">Item not found, or it has no optimised version.</response>
        /// <returns>A <see cref="FileResult"/> containing the optimised media stream.</returns>
        /// <exception cref="ArgumentException">User can't download or item can't be downloaded.</exception>
        [HttpGet("Items/{itemId}/Download/Optimised")]
        [Authorize(Policy = Policies.Download)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesFile("video/*", "audio/*")]
        public async Task<ActionResult> GetOptimisedDownload([FromRoute, Required] Guid itemId)
        {
            var (item, user) = GetDownloadableItem(itemId);
            if (item is null)
            {
                return NotFound();
            }

            // No fall-through to the item's own file: this route means the optimised one.
            var downloadVersion = downloadHelper.FindForUser(item.Path, user?.Id ?? Guid.Empty);
            if (downloadVersion is null)
            {
                return NotFound();
            }

            if (user is not null)
            {
                await LogDownloadAsync(item, user).ConfigureAwait(false);
            }

            logger.LogInformation("Serving download version {DownloadVersion} for {Path}", downloadVersion, item.Path);

            return ServeDownload(item, downloadVersion);
        }

        /// <summary>
        /// Gets the media info of the file <c>Items/{itemId}/Download/Optimised</c> would serve.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <response code="200">Media info returned.</response>
        /// <response code="404">Item not found, or it has no optimised version.</response>
        /// <returns>The <see cref="MediaSourceInfo"/> of the optimised version.</returns>
        /// <exception cref="ArgumentException">User can't download or item can't be downloaded.</exception>
        [HttpGet("Items/{itemId}/Download/Optimised/MediaInfo")]
        [Authorize(Policy = Policies.Download)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<MediaSourceInfo>> GetOptimisedDownloadMediaInfo(
            [FromRoute, Required] Guid itemId,
            CancellationToken cancellationToken)
        {
            var (item, user) = GetDownloadableItem(itemId);
            if (item is null)
            {
                return NotFound();
            }

            var downloadVersion = downloadHelper.FindForUser(item.Path, user?.Id ?? Guid.Empty);
            if (downloadVersion is null)
            {
                return NotFound();
            }

            var mediaSource = await downloadHelper.DescribeAsync(item, user, downloadVersion, cancellationToken)
                .ConfigureAwait(false);

            return mediaSource is null ? NotFound() : mediaSource;
        }

        /// <summary>
        /// Gets the download tiers this user may choose from, and the one they are on.
        /// </summary>
        /// <response code="200">Download tier options returned.</response>
        /// <returns>The available tiers, the default, and this user's own choice.</returns>
        [HttpGet("Downloads/Tier")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult<DownloadTierOptionsDto> GetDownloadTier()
        {
            var userId = User.GetUserId();

            return new DownloadTierOptionsDto
            {
                Tiers = [.. downloadHelper.GetEnabledTiers().Select(tier => new DownloadTierInfoDto
                {
                    Id = tier.Id,
                    Name = tier.Name,
                    Description = tier.Description
                })],
                DefaultTierId = downloadHelper.GetDefaultTier()?.Id,
                OriginalAvailable = downloadHelper.OffersOriginal(),
                TierId = downloadHelper.ChoseOriginal(userId)
                    ? DownloadTiers.OriginalId
                    : downloadHelper.GetUserTier(userId)?.Id
            };
        }

        /// <summary>
        /// Sets the download tier for this user, or clears it so they follow the default.
        /// </summary>
        /// <param name="downloadTierDto">The tier to use.</param>
        /// <response code="204">Download tier updated.</response>
        /// <response code="400">The tier is not one the admin has enabled, or the original file was
        /// chosen and is not offered.</response>
        /// <response code="401">User context missing.</response>
        /// <returns>A <see cref="NoContentResult"/> indicating success.</returns>
        [HttpPost("Downloads/Tier")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public ActionResult SetDownloadTier([FromBody, Required] DownloadTierDto downloadTierDto)
        {
            var userId = User.GetUserId();
            if (userId.IsEmpty())
            {
                return Unauthorized();
            }

            if (downloadTierDto is null)
            {
                return NoContent();
            }

            var tierId = downloadTierDto.TierId;

            // The original is a choice, not a tier, so it is checked before the tiers are searched.
            if (DownloadTiers.IsOriginal(tierId))
            {
                if (!downloadHelper.OffersOriginal())
                {
                    return BadRequest("The original file is not offered on this server");
                }

                tierId = DownloadTiers.OriginalId;
            }
            else if (!string.IsNullOrEmpty(tierId))
            {
                // Store the server's own id for the tier, not the spelling that arrived.
                var tier = downloadHelper.ResolveEnabledTier(tierId);
                if (tier is null)
                {
                    return BadRequest("TierId is not an enabled download tier");
                }

                tierId = tier.Id;
            }

            downloadHelper.SetUserTier(userId, tierId);

            return NoContent();
        }

        private (BaseItem? Item, User? User) GetDownloadableItem(Guid itemId)
        {
            var userId = User.GetUserId();
            var user = userId.IsEmpty() ? null : userManager.GetUserById(userId);
            var item = libraryManager.GetItemById<BaseItem>(itemId, user);
            if (item is null)
            {
                return (null, user);
            }

            var canDownload = user is not null ? item.CanDownload(user) : item.CanDownload();
            if (!canDownload)
            {
                throw new ArgumentException("Item does not support downloading");
            }

            return (item, user);
        }

        private PhysicalFileResult ServeDownload(BaseItem item, string filePath)
        {
            // Quotes are valid in linux. They'll possibly cause issues here.
            var filename = Path.GetFileName(filePath)?.Replace("\"", string.Empty, StringComparison.Ordinal);

            if (item.IsFileProtocol)
            {
                // PhysicalFile does not work well with symlinks at the moment.
                var resolved = FileSystemHelper.ResolveLinkTarget(filePath, returnFinalTarget: true);
                if (resolved is not null && resolved.Exists)
                {
                    filePath = resolved.FullName;
                }
            }

            return PhysicalFile(filePath, MimeTypes.GetMimeType(filePath), filename, true);
        }

        private async Task LogDownloadAsync(BaseItem item, User user)
        {
            try
            {
                await activityManager.CreateAsync(new ActivityLog(
                    string.Format(CultureInfo.InvariantCulture, localization.GetServerLocalizedString("UserDownloadingItemWithValues"), user.Username, item.Name),
                    "UserDownloadingContent",
                    User.GetUserId())
                {
                    ShortOverview = string.Format(CultureInfo.InvariantCulture, localization.GetServerLocalizedString("AppDeviceValues"), User.GetClient(), User.GetDevice()),
                    ItemId = item.Id.ToString("N", CultureInfo.InvariantCulture)
                }).ConfigureAwait(false);
            }
            catch
            {
                // Logged at lower levels
            }
        }
    }
}
