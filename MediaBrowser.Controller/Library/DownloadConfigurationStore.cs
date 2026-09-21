using System.Collections.Generic;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Configuration;

namespace MediaBrowser.Controller.Library
{
    /// <summary>
    /// Class DownloadConfigurationStore.
    /// </summary>
    public class DownloadConfigurationStore : IConfigurationFactory
    {
        /// <inheritdoc />
        public IEnumerable<ConfigurationStore> GetConfigurations()
        {
            return
            [
                new ConfigurationStore
                {
                    Key = "downloads",
                    ConfigurationType = typeof(DownloadOptions)
                }
            ];
        }
    }
}
