using IntegrationPlatform.Common.Enums;
using IntegrationPlatform.Common.Interfaces;
using IntegrationPlatform.Common.Interfaces.Plugins;
using IntegrationPlatform.Common.Models;
using IntegrationPlatform.Worker.Interfaces.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IntegrationPlatform.Worker.Services
{
    public class AdapterFactory : IAdapterFactory
    {
        private readonly IPluginManager _pluginManager;
        private readonly ILogger<AdapterFactory> _logger;
        private Dictionary<AdapterType, string> _pluginIdMap;

        public AdapterFactory(IPluginManager pluginManager, ILogger<AdapterFactory> logger)
        {
            _pluginManager = pluginManager;
            _logger = logger;
            InitializePluginMap();
        }

        private void InitializePluginMap()
        {
            _pluginIdMap = new Dictionary<AdapterType, string>
            {
                // Source'lar
                { AdapterType.Rest, "IntegrationPlatform.Adapters.Rest" },
                { AdapterType.Database, "IntegrationPlatform.Adapters.Database" },
                { AdapterType.JsonFile, "IntegrationPlatform.Adapters.Json" },
                { AdapterType.CsvFile, "IntegrationPlatform.Adapters.Csv" },
                //{ AdapterType.XmlFile, "IntegrationPlatform.Adapters.Xml" },
                { AdapterType.ExcelFile, "IntegrationPlatform.Adapters.Excel" },
                { AdapterType.Ftp, "IntegrationPlatform.Adapters.Ftp" },
                
                // Transform'lar
                { AdapterType.DataMapper, "IntegrationPlatform.Adapters.Json" },
                { AdapterType.ScriptEngine, "IntegrationPlatform.Adapters.Script" },
                { AdapterType.Aggregator, "IntegrationPlatform.Adapters.Json" },
                { AdapterType.Filter, "IntegrationPlatform.Adapters.Json" },
                
                // Destination'lar
                { AdapterType.DatabaseWriter, "IntegrationPlatform.Adapters.Database" },
                { AdapterType.JsonWriter, "IntegrationPlatform.Adapters.Json" },
                { AdapterType.CsvWriter, "IntegrationPlatform.Adapters.Csv" },
                //{ AdapterType.XmlWriter, "IntegrationPlatform.Adapters.Xml" },
                { AdapterType.ExcelWriter, "IntegrationPlatform.Adapters.Excel" },
                { AdapterType.FtpWriter, "IntegrationPlatform.Adapters.Ftp" }
            };
        }

        public async Task<ISourcePlugin> GetSourcePluginAsync(AdapterType type)
        {
            var pluginId = GetPluginId(type);
            var plugin = await _pluginManager.GetPluginAsync<ISourcePlugin>(pluginId);

            if (plugin == null)
                throw new InvalidOperationException($"Source plugin bulunamadı: {type} (ID: {pluginId})");

            return plugin;
        }

        public async Task<ITransformPlugin> GetTransformPluginAsync(AdapterType type)
        {
            var pluginId = GetPluginId(type);
            var plugin = await _pluginManager.GetPluginAsync<ITransformPlugin>(pluginId);

            if (plugin == null)
                throw new InvalidOperationException($"Transform plugin bulunamadı: {type} (ID: {pluginId})");

            return plugin;
        }

        public async Task<IDestinationPlugin> GetDestinationPluginAsync(AdapterType type)
        {
            var pluginId = GetPluginId(type);
            var plugin = await _pluginManager.GetPluginAsync<IDestinationPlugin>(pluginId);

            if (plugin == null)
                throw new InvalidOperationException($"Destination plugin bulunamadı: {type} (ID: {pluginId})");

            return plugin;
        }

        public async Task<IPlugin> GetPluginAsync(string pluginId)
        {
            return await _pluginManager.GetPluginAsync<IPlugin>(pluginId);
        }

        public IEnumerable<AdapterMetadata> GetAvailableAdapters()
        {
            var adapters = new List<AdapterMetadata>();

            foreach (var kvp in _pluginIdMap)
            {
                var plugin = _pluginManager.GetPluginAsync<IPlugin>(kvp.Value).Result;
                if (plugin != null)
                {
                    adapters.Add(new AdapterMetadata
                    {
                        Id = plugin.Id,
                        Name = plugin.Name,
                        Description = plugin.Description,
                        Type = kvp.Key,
                        Direction = GetDirectionForType(kvp.Key),
                        Version = plugin.Version,
                        Author = plugin.Author
                    });
                }
            }

            return adapters;
        }

        private string GetPluginId(AdapterType type)
        {
            if (!_pluginIdMap.TryGetValue(type, out var pluginId))
                throw new NotSupportedException($"Desteklenmeyen adapter tipi: {type}");

            return pluginId;
        }

        private AdapterDirection GetDirectionForType(AdapterType type)
        {
            return type switch
            {
                // Source'lar
                AdapterType.Rest => AdapterDirection.Source,
                AdapterType.Database => AdapterDirection.Source,
                AdapterType.JsonFile => AdapterDirection.Source,
                AdapterType.CsvFile => AdapterDirection.Source,
                //AdapterType.XmlFile => AdapterDirection.Source,
                AdapterType.ExcelFile => AdapterDirection.Source,
                AdapterType.Ftp => AdapterDirection.Source,

                // Transform'lar
                AdapterType.DataMapper => AdapterDirection.Transform,
                AdapterType.ScriptEngine => AdapterDirection.Transform,
                AdapterType.Aggregator => AdapterDirection.Transform,
                AdapterType.Filter => AdapterDirection.Transform,

                // Destination'lar
                AdapterType.DatabaseWriter => AdapterDirection.Destination,
                AdapterType.JsonWriter => AdapterDirection.Destination,
                AdapterType.CsvWriter => AdapterDirection.Destination,
                //AdapterType.XmlWriter => AdapterDirection.Destination,
                AdapterType.ExcelWriter => AdapterDirection.Destination,
                AdapterType.FtpWriter => AdapterDirection.Destination,

                _ => AdapterDirection.Source
            };
        }
    }
}
