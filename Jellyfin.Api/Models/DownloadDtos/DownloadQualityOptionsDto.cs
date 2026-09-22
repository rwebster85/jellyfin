using System.Collections.Generic;

namespace Jellyfin.Api.Models.DownloadDtos
{
    /// <summary>
    /// The download quality tiers available to a user, and the one they are on.
    /// </summary>
    public class DownloadQualityOptionsDto
    {
        /// <summary>
        /// Gets or sets the tiers the admin has enabled, best first. A user may choose any of these
        /// and nothing else.
        /// </summary>
        public IReadOnlyList<string> Qualities { get; set; } = [];

        /// <summary>
        /// Gets or sets the tier a user who has not chosen one gets, or <c>null</c> if the admin has
        /// enabled no tiers at all.
        /// </summary>
        public string? DefaultQuality { get; set; }

        /// <summary>
        /// Gets or sets the tier this user chose, or <c>null</c> when they follow the default.
        /// </summary>
        public string? Quality { get; set; }
    }
}
