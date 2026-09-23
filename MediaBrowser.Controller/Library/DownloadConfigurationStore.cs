using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Configuration;

namespace MediaBrowser.Controller.Library
{
    /// <summary>
    /// The download settings store, which checks an administrator's tier list before it is saved.
    /// </summary>
    public class DownloadConfigurationStore : ConfigurationStore, IValidatingConfiguration
    {
        /// <summary>
        /// The store's key, which is also the name of the file it is saved to -
        /// <c>optimised-downloads.xml</c> in the configuration directory.
        /// </summary>
        public const string StoreKey = "optimised-downloads";

        /// <summary>
        /// Initializes a new instance of the <see cref="DownloadConfigurationStore"/> class.
        /// </summary>
        public DownloadConfigurationStore()
        {
            Key = StoreKey;
            ConfigurationType = typeof(DownloadOptions);
        }

        /// <inheritdoc />
        /// <remarks>
        /// An <see cref="System.ArgumentException"/> here becomes a 400 on the configuration
        /// endpoint. Also assigns missing tier ids, on the object about to be written.
        /// </remarks>
        public void Validate(object oldConfig, object newConfig)
            => DownloadTiers.PrepareForSave((DownloadOptions)newConfig);
    }
}
