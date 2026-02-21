using IntegrationPlatform.Common.Enums;
using IntegrationPlatform.Common.Interfaces.Plugins;
using IntegrationPlatform.Common.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IntegrationPlatform.Worker.Interfaces.Services
{
    /// <summary>
    /// Adapter'ları tip ve yöne göre getiren factory
    /// </summary>
    public interface IAdapterFactory
    {
        Task<ISourcePlugin> GetSourcePluginAsync(AdapterType type);
        Task<ITransformPlugin> GetTransformPluginAsync(AdapterType type);
        Task<IDestinationPlugin> GetDestinationPluginAsync(AdapterType type);
        Task<IPlugin> GetPluginAsync(string pluginId);
        IEnumerable<AdapterMetadata> GetAvailableAdapters();
    }
}
