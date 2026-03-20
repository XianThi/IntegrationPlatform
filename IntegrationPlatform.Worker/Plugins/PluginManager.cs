using IntegrationPlatform.Common.Enums;
using IntegrationPlatform.Common.Interfaces;
using IntegrationPlatform.Common.Interfaces.Plugins;
using IntegrationPlatform.Common.Models;
using System.Runtime.Loader;

namespace IntegrationPlatform.Worker.Plugins
{
    public class PluginManager : IPluginManager
    {
        private readonly ILogger<PluginManager> _logger;
        private readonly Dictionary<string, IPlugin> _plugins;
        private readonly Dictionary<string, PluginIsolationContext> _isolatedContexts;
        private readonly string _pluginsPath;

        public event EventHandler<PluginLoadedEventArgs> PluginLoaded;
        public event EventHandler<PluginUnloadedEventArgs> PluginUnloaded;

        public PluginManager(ILogger<PluginManager> logger, IConfiguration configuration)
        {
            _logger = logger;
            _plugins = new Dictionary<string, IPlugin>();
            _isolatedContexts = new Dictionary<string, PluginIsolationContext>();
            _pluginsPath = configuration["Worker:AdaptersPath"] ?? Path.Combine(AppContext.BaseDirectory, "Plugins");
            _logger.LogInformation("Plugin yolu: {Path}", _pluginsPath);
            _logger.LogInformation("BaseDirectory: {BaseDir}", AppContext.BaseDirectory);

            // Dizin var mı kontrol et
            if (!Directory.Exists(_pluginsPath))
            {
                _logger.LogWarning("Plugin dizini bulunamadı, oluşturuluyor: {Path}", _pluginsPath);
                Directory.CreateDirectory(_pluginsPath);
            }
        }

        public async Task<IEnumerable<IPlugin>> LoadPluginsAsync(string pluginsPath = null)
        {
            var path = pluginsPath ?? _pluginsPath;
            _logger.LogInformation("Plugin'ler yükleniyor: {Path}", path);

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
                _logger.LogWarning("Plugin dizini oluşturuldu: {Path}", path);
                return Enumerable.Empty<IPlugin>();
            }

            var dllFiles = Directory.GetFiles(path, "*.dll");
            var loadedPlugins = new List<IPlugin>();

            foreach (var dll in dllFiles)
            {
                try
                {
                    _logger.LogInformation("Plugin yükleniyor: {Dll}", dll);
                    var plugins = await LoadPluginAsync(dll);
                    if (plugins != null && plugins.Any())
                    {
                        loadedPlugins.AddRange(plugins);
                        _logger.LogInformation("{Count} plugin yüklendi: {Dll}", plugins.Count, dll);

                        foreach (var plugin in plugins)
                        {
                            _logger.LogDebug("  - {PluginName} ({Direction})", plugin.Name, plugin.Direction);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Plugin bulunamadı: {Dll}", dll);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Plugin yüklenirken hata: {Dll}", dll);

                    PluginLoaded?.Invoke(this, new PluginLoadedEventArgs
                    {
                        AssemblyPath = dll,
                        IsSuccess = false,
                        ErrorMessage = ex.Message,
                        LoadedAt = DateTime.UtcNow
                    });
                }
            }

            _logger.LogInformation("Toplam {Count} plugin yüklendi", loadedPlugins.Count);

            // Direction'a göre grupla ve logla
            var grouped = loadedPlugins.GroupBy(p => p.Direction);
            foreach (var group in grouped)
            {
                _logger.LogInformation("  {Direction}: {Count} plugin", group.Key, group.Count());
            }

            return loadedPlugins;
        }

        public async Task<List<IPlugin>> LoadPluginAsync(string assemblyPath)
        {
            _logger.LogDebug("Plugin yükleniyor: {AssemblyPath}", assemblyPath);

            // İzole edilmiş context oluştur
            var context = new PluginIsolationContext(
                    Guid.NewGuid().ToString(),
                    assemblyPath
            );
            var loadedPlugins = new List<IPlugin>();
            try
            {
                using var fs = new FileStream(assemblyPath, FileMode.Open, FileAccess.Read);
                var assembly = context.LoadContext.LoadFromStream(fs);

                // IPlugin implementasyonlarını bul
                var pluginTypes = assembly.GetTypes()
                    .Where(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                foreach (var pluginType in pluginTypes)
                {
                    var constructor = pluginType.GetConstructor(Type.EmptyTypes);
                    if (constructor != null)
                    {
                        try
                        {
                            // Parametresiz constructor var
                            if (Activator.CreateInstance(pluginType, true) is IPlugin plugin)
                            {
                                var initContext = new PluginContext
                                {
                                    Logger = _logger,
                                    CancellationToken = CancellationToken.None
                                };

                                await plugin.InitializeAsync(initContext);
                                _plugins[plugin.Id] = plugin;
                                _isolatedContexts[plugin.Id] = context;
                                loadedPlugins.Add(plugin);
                                _logger.LogInformation("Plugin yüklendi: {PluginName} v{Version}",
                                    plugin.Name, plugin.Version);

                                PluginLoaded?.Invoke(this, new PluginLoadedEventArgs
                                {
                                    PluginId = plugin.Id,
                                    PluginName = plugin.Name,
                                    Version = plugin.Version,
                                    PluginType = plugin.Type,
                                    AssemblyPath = assemblyPath,
                                    IsSuccess = true,
                                    LoadedAt = DateTime.UtcNow
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Plugin oluşturulurken hata: {TypeName}", pluginType.FullName);
                        }
                    }
                    else
                    {
                        // Parametresiz constructor yok, farklı yöntem dene
                        _logger.LogWarning("Parametresiz constructor bulunamadı: {TypeName}", pluginType.FullName);

                        // Activator.CreateInstance overload'u ile dene
                        try
                        {
                            var plugin = CreatePluginInstance(pluginType);
                            if (plugin != null)
                            {
                                _plugins[plugin.Id] = plugin;
                                _isolatedContexts[plugin.Id] = context;
                                loadedPlugins.Add(plugin);
                                _logger.LogInformation("Plugin yüklendi: {PluginName} v{Version}",
                                    plugin.Name, plugin.Version);

                                PluginLoaded?.Invoke(this, new PluginLoadedEventArgs
                                {
                                    PluginId = plugin.Id,
                                    PluginName = plugin.Name,
                                    Version = plugin.Version,
                                    PluginType = plugin.Type,
                                    AssemblyPath = assemblyPath,
                                    IsSuccess = true,
                                    LoadedAt = DateTime.UtcNow
                                });
                            }
                        }
                        catch (MissingMethodException)
                        {
                            _logger.LogError("Plugin oluşturulamıyor: {TypeName}", pluginType.FullName);
                        }
                    }
                }
                if (!loadedPlugins.Any())
                {
                    // Hiç plugin yüklenemediyse context'i temizle
                    context.Dispose();
                }
                return loadedPlugins;
            }
            catch (Exception ex)
            {
                context.Dispose();
                _logger.LogError(ex, "Plugin yüklenirken hata");
                throw;
            }
        }
        private IPlugin CreatePluginInstance(Type pluginType)
        {
            // Parametresiz constructor dene
            var constructor = pluginType.GetConstructor(Type.EmptyTypes);
            if (constructor != null)
            {
                return Activator.CreateInstance(pluginType, true) as IPlugin;
            }

            // ILogger<T> constructor'ı dene
            var loggerConstructor = pluginType.GetConstructors()
                .FirstOrDefault(c =>
                    c.GetParameters().Length == 1 &&
                    c.GetParameters()[0].ParameterType.IsGenericType &&
                    c.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == typeof(ILogger<>));

            if (loggerConstructor != null)
            {
                var loggerType = typeof(ILogger<>).MakeGenericType(pluginType);
                var logger = _logger != null ?
                    Activator.CreateInstance(loggerType, _logger) :
                    null;

                return Activator.CreateInstance(pluginType, logger) as IPlugin;
            }

            return null;
        }
        public async Task<IEnumerable<ISourcePlugin>> GetSourcePluginsAsync()
        {
            return _plugins.Values
                .Where(p => p.Direction == AdapterDirection.Source)
                .Cast<ISourcePlugin>()
                .ToList();
        }

        public async Task<IEnumerable<ITransformPlugin>> GetTransformPluginsAsync()
        {
            return _plugins.Values
                .Where(p => p.Direction == AdapterDirection.Transform)
                .Cast<ITransformPlugin>()
                .ToList();
        }

        public async Task<IEnumerable<IDestinationPlugin>> GetDestinationPluginsAsync()
        {
            return _plugins.Values
                .Where(p => p.Direction == AdapterDirection.Destination)
                .Cast<IDestinationPlugin>()
                .ToList();
        }

        public async Task<T> GetPluginAsync<T>(string pluginId) where T : IPlugin
        {
            _logger.LogDebug("Plugin aranıyor: {PluginId} için {TypeName}", pluginId, typeof(T).Name);
            if (_plugins.TryGetValue(pluginId, out var plugin) && plugin is T typedPlugin)
            {
                return typedPlugin;
            }
            return default;
        }

        public async Task<IPlugin> GetPluginAsync(string pluginId)
        {
            return _plugins.TryGetValue(pluginId, out var plugin) ? plugin : null;
        }

        public async Task<bool> ReloadPluginAsync(string pluginId)
        {
            if (!_plugins.TryGetValue(pluginId, out var oldPlugin))
                return false;

            var assemblyPath = _isolatedContexts[pluginId]?.LoadContext?.Name;

            // Önce eski plugin'i kaldır
            await UnloadPluginAsync(pluginId);

            // Tekrar yükle
            if (!string.IsNullOrEmpty(assemblyPath))
            {
                var newPlugin = await LoadPluginAsync(assemblyPath);
                return newPlugin != null;
            }

            return false;
        }

        public async Task<bool> UnloadPluginAsync(string pluginId)
        {
            if (!_plugins.TryGetValue(pluginId, out var plugin))
                return false;

            try
            {
                await plugin.ShutdownAsync();

                if (_isolatedContexts.TryGetValue(pluginId, out var context))
                {
                    context.Dispose();
                    _isolatedContexts.Remove(pluginId);
                }

                _plugins.Remove(pluginId);

                PluginUnloaded?.Invoke(this, new PluginUnloadedEventArgs
                {
                    PluginId = pluginId,
                    PluginName = plugin.Name,
                    UnloadedAt = DateTime.UtcNow,
                    Reason = UnloadReason.Manual
                });

                _logger.LogInformation("Plugin kaldırıldı: {PluginName}", plugin.Name);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Plugin kaldırılırken hata: {PluginId}", pluginId);
                return false;
            }
        }

        public async Task<PluginIsolationContext> CreateIsolatedContext(string pluginId)
        {
            return _isolatedContexts.TryGetValue(pluginId, out var context) ? context : null;
        }

        public async Task<AdapterMetadata> GetPluginMetadataAsync(string pluginId)
        {
            var plugin = await GetPluginAsync(pluginId);
            return plugin != null ? await plugin.GetMetadataAsync() : null;
        }
    }
}
