using IntegrationPlatform.Common.Enums;
using IntegrationPlatform.Common.Models;
using Microsoft.Extensions.Logging;

namespace IntegrationPlatform.Common.Interfaces.Plugins
{
    public interface IPlugin
    {
        string Id { get; }
        string Name { get; }
        string Description { get; }
        Version Version { get; }
        string Author { get; }
        /// <summary>
        /// Plugin tipi
        /// </summary>
        AdapterType Type { get; }

        /// <summary>
        /// Plugin yönü (Source/Transform/Destination)
        /// </summary>
        AdapterDirection Direction { get; }

        /// <summary>
        /// Plugin metadata'sını getir
        /// </summary>
        Task<AdapterMetadata> GetMetadataAsync();

        /// <summary>
        /// Plugin'i başlat
        /// </summary>
        Task<bool> InitializeAsync(PluginContext context);

        /// <summary>
        /// Plugin'i kapat
        /// </summary>
        Task<bool> ShutdownAsync();
    }

    public class Capability
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public Dictionary<string, object> Parameters { get; set; }
    }

    public class PluginContext
    {
        public Guid? WorkflowId { get; set; }
        public Guid? StepId { get; set; }
        public Dictionary<string, object> GlobalVariables { get; set; } = new Dictionary<string, object>();
        public ILogger? Logger { get; set; }
        public CancellationToken CancellationToken { get; set; }
    }
}
