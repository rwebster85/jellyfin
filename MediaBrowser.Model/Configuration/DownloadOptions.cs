#pragma warning disable CA1819 // XML serialization handles collections improperly, so we need to use arrays

namespace MediaBrowser.Model.Configuration
{
    /// <summary>
    /// Class DownloadOptions.
    /// </summary>
    public class DownloadOptions
    {
        /// <summary>
        /// Gets or sets the folders searched for pre-made download versions.
        /// </summary>
        public string[] Locations { get; set; } = [];

        /// <summary>
        /// Gets or sets the quality suffix a download version's filename must end with.
        /// </summary>
        public string Quality { get; set; } = "High";
    }
}
