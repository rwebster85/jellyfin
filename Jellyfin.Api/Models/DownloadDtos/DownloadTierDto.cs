namespace Jellyfin.Api.Models.DownloadDtos
{
    /// <summary>
    /// A user's chosen download tier.
    /// </summary>
    public class DownloadTierDto
    {
        /// <summary>
        /// Gets or sets the id of the tier to use, <c>original</c> for the original file, or
        /// <c>null</c> to follow the server default.
        /// </summary>
        public string? TierId { get; set; }
    }
}
