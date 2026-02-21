using IntegrationPlatform.Common.Interfaces.Plugins;
using IntegrationPlatform.Common.Models;
using System.Runtime.Loader;

namespace IntegrationPlatform.Common.Interfaces
{
    public interface IPluginManager
    {
        // Plugin yükleme
        Task<IEnumerable<IPlugin>> LoadPluginsAsync(string pluginsPath);
        Task<IPlugin> LoadPluginAsync(string assemblyPath);

        // Plugin tiplerine göre getirme
        Task<IEnumerable<ISourcePlugin>> GetSourcePluginsAsync();
        Task<IEnumerable<ITransformPlugin>> GetTransformPluginsAsync();
        Task<IEnumerable<IDestinationPlugin>> GetDestinationPluginsAsync();

        // Tip bazlı getirme
        Task<T> GetPluginAsync<T>(string pluginId) where T : IPlugin;
        Task<IPlugin> GetPluginAsync(string pluginId);

        // Hot reload
        Task<bool> ReloadPluginAsync(string pluginId);
        Task<bool> UnloadPluginAsync(string pluginId);

        // Plugin izolasyonu (AppDomain/AssemblyLoadContext)
        Task<PluginIsolationContext> CreateIsolatedContext(string pluginId);

        // Plugin metadata
        Task<AdapterMetadata> GetPluginMetadataAsync(string pluginId);

        // Olaylar
        event EventHandler<PluginLoadedEventArgs> PluginLoaded;
        event EventHandler<PluginUnloadedEventArgs> PluginUnloaded;
    }

    public class PluginIsolationContext : IDisposable
    {
        public string PluginId { get; set; }
        public AssemblyLoadContext LoadContext { get; set; }
        public IServiceProvider Services { get; set; }

        public void Dispose()
        {
            LoadContext?.Unload();
        }
    }
}
