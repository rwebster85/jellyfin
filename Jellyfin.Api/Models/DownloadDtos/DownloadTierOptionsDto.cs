using System.Collections.Generic;

namespace Jellyfin.Api.Models.DownloadDtos
{
    /// <summary>
    /// The download tiers available to a user, and the one they are on.
    /// </summary>
    public class DownloadTierOptionsDto
    {
        /// <summary>
        /// Gets or sets the tiers the admin has enabled, in the order they arranged them. A user may
        /// choose any of these and nothing else.
        /// </summary>
        public IReadOnlyList<DownloadTierInfoDto> Tiers { get; set; } = [];

        /// <summary>
        /// Gets or sets the id of the tier a user who has not chosen one gets, or <c>null</c> if the
        /// admin has enabled no tiers at all.
        /// </summary>
        public string? DefaultTierId { get; set; }

        /// <summary>
        /// Gets or sets the id of the tier this user chose, or <c>null</c> when they follow the
        /// default.
        /// </summary>
        public string? TierId { get; set; }
    }
}
