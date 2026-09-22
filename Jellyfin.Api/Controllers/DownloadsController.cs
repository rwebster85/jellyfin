using System.ComponentModel.DataAnnotations;
using System.Linq;
using Jellyfin.Api.Extensions;
using Jellyfin.Api.Helpers;
using Jellyfin.Api.Models.DownloadDtos;
using Jellyfin.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Api.Controllers
{
    /// <summary>
    /// Download preferences controller.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="DownloadsController"/> class.
    /// </remarks>
    /// <param name="downloadHelper">Instance of the <see cref="DownloadHelper"/>.</param>
    [Authorize]
    public class DownloadsController(DownloadHelper downloadHelper) : BaseJellyfinApiController
    {
        /// <summary>
        /// Gets the download tiers this user may choose from, and the one they are on.
        /// </summary>
        /// <response code="200">Download tier options returned.</response>
        /// <returns>The available tiers, the default, and this user's own choice.</returns>
        [HttpGet("Tier")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult<DownloadTierOptionsDto> GetDownloadTier()
        {
            return new DownloadTierOptionsDto
            {
                Tiers = [.. downloadHelper.GetEnabledTiers().Select(tier => new DownloadTierInfoDto
                {
                    Id = tier.Id,
                    Name = tier.Name,
                    Description = tier.Description
                })],
                DefaultTierId = downloadHelper.GetDefaultTier()?.Id,
                TierId = downloadHelper.GetUserTier(User.GetUserId())?.Id
            };
        }

        /// <summary>
        /// Sets the download tier for this user, or clears it so they follow the default.
        /// </summary>
        /// <param name="downloadTierDto">The tier to use.</param>
        /// <response code="204">Download tier updated.</response>
        /// <response code="400">The tier is not one the admin has enabled.</response>
        /// <response code="401">User context missing.</response>
        /// <returns>A <see cref="NoContentResult"/> indicating success.</returns>
        [HttpPost("Tier")]
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

            if (!string.IsNullOrEmpty(tierId))
            {
                // Match against the enabled tiers rather than trusting the body, and store the id
                // the server holds rather than the spelling that arrived.
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
    }
}
