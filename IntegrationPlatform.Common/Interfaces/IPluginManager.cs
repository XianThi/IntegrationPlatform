using IntegrationPlatform.Common.Interfaces.Plugins;
using IntegrationPlatform.Common.Models;
using System.Reflection;
using System.Runtime.Loader;

namespace IntegrationPlatform.Common.Interfaces
{
    public interface IPluginManager
    {
        // Plugin yükleme
        Task<IEnumerable<IPlugin>> LoadPluginsAsync(string pluginsPath);
        Task<List<IPlugin>> LoadPluginAsync(string assemblyPath);

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
        private string _pluginPath;

        public PluginIsolationContext(string pluginId, string pluginPath)
        {
            PluginId = pluginId;
            _pluginPath = Path.GetDirectoryName(pluginPath);
            LoadContext = new AssemblyLoadContext(pluginId, true);

            // Assembly resolve olayını yakala
            LoadContext.Resolving += OnAssemblyResolving;
        }

        private Assembly OnAssemblyResolving(AssemblyLoadContext context, AssemblyName assemblyName)
        {
            // Önce plugin dizininde ara
            var pluginAssemblyPath = Path.Combine(_pluginPath, $"{assemblyName.Name}.dll");

            if (File.Exists(pluginAssemblyPath))
            {
                try
                {
                    using var fs = new FileStream(pluginAssemblyPath, FileMode.Open, FileAccess.Read);
                    return context.LoadFromStream(fs);
                }
                catch (Exception ex)
                {
                    // Loglama yapılabilir
                    Console.WriteLine($"Assembly yüklenemedi: {pluginAssemblyPath}, Hata: {ex.Message}");
                }
            }

            // Ana uygulama dizininde ara
            var mainAppPath = Path.Combine(AppContext.BaseDirectory, $"{assemblyName.Name}.dll");
            if (File.Exists(mainAppPath))
            {
                try
                {
                    using var fs = new FileStream(mainAppPath, FileMode.Open, FileAccess.Read);
                    return context.LoadFromStream(fs);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Assembly yüklenemedi: {mainAppPath}, Hata: {ex.Message}");
                }
            }

            return null;
        }

        public void Dispose()
        {
            LoadContext?.Unload();
        }
    }
}
