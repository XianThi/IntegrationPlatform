using IntegrationPlatform.Common.Enums;

namespace IntegrationPlatform.Common.Interfaces.Plugins
{
    public class PluginLoadedEventArgs : EventArgs
    {
        public string PluginId { get; set; }
        public string PluginName { get; set; }
        public Version Version { get; set; }
        public AdapterType PluginType { get; set; }
        public DateTime LoadedAt { get; set; }
        public string AssemblyPath { get; set; }
        public bool IsSuccess { get; set; }
        public string ErrorMessage { get; set; }
    }

    public class PluginUnloadedEventArgs : EventArgs
    {
        public string PluginId { get; set; }
        public string PluginName { get; set; }
        public DateTime UnloadedAt { get; set; }
        public UnloadReason Reason { get; set; }
    }

    public enum UnloadReason
    {
        Manual = 1,
        Update = 2,
        Error = 3,
        Shutdown = 4
    }
}
