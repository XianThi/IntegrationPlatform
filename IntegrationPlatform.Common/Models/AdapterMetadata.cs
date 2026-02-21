using IntegrationPlatform.Common.Enums;
using IntegrationPlatform.Common.Interfaces;
using IntegrationPlatform.Common.Interfaces.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IntegrationPlatform.Common.Models
{
    /// <summary>
    /// Adapter metadata'sı
    /// </summary>
    public class AdapterMetadata
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public AdapterType Type { get; set; }
        public AdapterDirection Direction { get; set; }
        public Version Version { get; set; }
        public string Author { get; set; }
        public Dictionary<string, ParameterDefinition> ConfigurationSchema { get; set; }
        public List<string> Tags { get; set; }
        public string Icon { get; set; }
        public string Category { get; set; }
        public List<string> SupportedFormats { get; set; } = new List<string>();
        public bool SupportsBatch { get; set; }
        public bool SupportsStreaming { get; set; }
        public long MaxBatchSize { get; set; } = 10000;
        public List<Capability> Capabilities { get; set; } = new List<Capability>();
        public Dictionary<string, Type> OutputTypes { get; set; } = new Dictionary<string, Type>();
        public Dictionary<string, Type> InputTypes { get; set; } = new Dictionary<string, Type>();
    }
}
