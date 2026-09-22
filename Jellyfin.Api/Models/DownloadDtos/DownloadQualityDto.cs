namespace Jellyfin.Api.Models.DownloadDtos
{
    /// <summary>
    /// A user's chosen download quality tier.
    /// </summary>
    public class DownloadQualityDto
    {
        /// <summary>
        /// Gets or sets the tier to use, or <c>null</c> to follow the server default.
        /// </summary>
        public string? Quality { get; set; }
    }
}
