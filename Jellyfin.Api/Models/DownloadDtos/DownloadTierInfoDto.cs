namespace Jellyfin.Api.Models.DownloadDtos
{
    /// <summary>
    /// A download tier as a user sees it.
    /// </summary>
    /// <remarks>
    /// The file name suffix is deliberately not here. It is the contract between the administrator
    /// and whatever produces the files, and nothing a user's client does with a tier needs it.
    /// </remarks>
    public class DownloadTierInfoDto
    {
        /// <summary>
        /// Gets or sets the tier's id, which is what a choice is made and stored by.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the label to show. Administrator-supplied, so it is not translated.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the longer explanation to show beneath the name, if the administrator wrote
        /// one.
        /// </summary>
        public string? Description { get; set; }
    }
}
