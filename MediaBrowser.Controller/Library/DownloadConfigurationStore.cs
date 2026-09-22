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
        /// Throwing an <see cref="System.ArgumentException"/> here is what turns a tier list that
        /// cannot be used into a 400 on the configuration endpoint, rather than a stored file whose
        /// behaviour nobody can explain later. The dashboard checks the same things before it
        /// submits, so what reaches this is an API caller or a hand-edited file.
        ///
        /// It also fills in any missing tier id, which works because the object handed over is the
        /// one about to be cached and written.
        /// </remarks>
        public void Validate(object oldConfig, object newConfig)
            => DownloadTiers.PrepareForSave((DownloadOptions)newConfig);
    }
}
