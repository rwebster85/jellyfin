using System.Collections.Generic;
using MediaBrowser.Common.Configuration;

namespace MediaBrowser.Controller.Library
{
    /// <summary>
    /// Registers the download settings store. Discovered by interface, so it needs no registration
    /// of its own.
    /// </summary>
    public class DownloadConfigurationFactory : IConfigurationFactory
    {
        /// <inheritdoc />
        public IEnumerable<ConfigurationStore> GetConfigurations()
        {
            return [new DownloadConfigurationStore()];
        }
    }
}
