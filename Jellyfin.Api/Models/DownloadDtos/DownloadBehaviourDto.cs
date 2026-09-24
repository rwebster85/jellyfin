using MediaBrowser.Model.Configuration;

namespace Jellyfin.Api.Models.DownloadDtos
{
    /// <summary>
    /// What a client needs to know to offer downloads: whether optimised downloads are on, and what
    /// the plain Download does. The rest of the settings - folder paths, tier suffixes - stay with
    /// the administrator.
    /// </summary>
    public class DownloadBehaviourDto
    {
        /// <summary>
        /// Gets or sets a value indicating whether optimised downloads are on.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Gets or sets what the plain Download does.
        /// </summary>
        public DownloadBehaviour Behaviour { get; set; }
    }
}
