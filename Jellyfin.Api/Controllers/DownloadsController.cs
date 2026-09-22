using System.ComponentModel.DataAnnotations;
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
        /// Gets the download quality tiers this user may choose from, and the one they are on.
        /// </summary>
        /// <response code="200">Download quality options returned.</response>
        /// <returns>The available tiers, the default, and this user's own choice.</returns>
        [HttpGet("Quality")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult<DownloadQualityOptionsDto> GetDownloadQuality()
        {
            return new DownloadQualityOptionsDto
            {
                Qualities = downloadHelper.GetEnabledQualities(),
                DefaultQuality = downloadHelper.GetDefaultQuality(),
                Quality = downloadHelper.GetUserQuality(User.GetUserId())
            };
        }

        /// <summary>
        /// Sets the download quality tier for this user, or clears it so they follow the default.
        /// </summary>
        /// <param name="downloadQualityDto">The tier to use.</param>
        /// <response code="204">Download quality updated.</response>
        /// <response code="400">The tier is not one the admin has enabled.</response>
        /// <response code="401">User context missing.</response>
        /// <returns>A <see cref="NoContentResult"/> indicating success.</returns>
        [HttpPost("Quality")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public ActionResult SetDownloadQuality([FromBody, Required] DownloadQualityDto downloadQualityDto)
        {
            var userId = User.GetUserId();
            if (userId.IsEmpty())
            {
                return Unauthorized();
            }

            if (downloadQualityDto is null)
            {
                return NoContent();
            }

            var quality = downloadQualityDto.Quality;

            if (!string.IsNullOrEmpty(quality))
            {
                // Match against the enabled tiers rather than trusting the body, and store the
                // canonical spelling so the filename suffix is never built from a client's casing.
                quality = downloadHelper.ResolveEnabledQuality(quality);

                if (quality is null)
                {
                    return BadRequest("Quality is not an enabled download tier");
                }
            }

            downloadHelper.SetUserQuality(userId, quality);

            return NoContent();
        }
    }
}
