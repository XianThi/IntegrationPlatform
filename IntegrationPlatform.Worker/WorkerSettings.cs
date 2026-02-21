namespace IntegrationPlatform.Worker
{
    public class WorkerSettings
    {
        public string ApiBaseUrl { get; set; } = "http://localhost:5000";
        public string NodeName { get; set; }
        public int HeartbeatIntervalSeconds { get; set; } = 30;
        public int WorkflowPollingIntervalSeconds { get; set; } = 60;
        public string AdaptersPath { get; set; } = "Adapters";
        public bool AutoRegisterPlugins { get; set; } = true;
        public int MaxConcurrentWorkflows { get; set; } = 5;
        public bool EnableMetricsCollection { get; set; } = true;
    }
}
