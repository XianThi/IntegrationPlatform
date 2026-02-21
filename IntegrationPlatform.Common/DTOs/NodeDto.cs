using IntegrationPlatform.Common.Enums;

namespace IntegrationPlatform.Common.DTOs
{
    public class NodeRegistrationDto
    {
        public string NodeName { get; set; }
        public string MachineName { get; set; }
        public string OperatingSystem { get; set; }
        public string Version { get; set; }
        public int ProcessorCount { get; set; }
        public long TotalMemory { get; set; }
        public List<string> SupportedAdapters { get; set; }
    }

    public class NodeDto
    {
        public Guid Id { get; set; }
        public string NodeName { get; set; }
        public string MachineName { get; set; }
        public string IpAddress { get; set; }
        public NodeStatus Status { get; set; }
        public DateTime LastHeartbeat { get; set; }
        public DateTime RegisteredAt { get; set; }
        public Dictionary<string, object> Metrics { get; set; }
    }

    public class NodeHeartbeatDto
    {
        public Guid NodeId { get; set; }
        public NodeStatus Status { get; set; }
        public int CurrentWorkload { get; set; }
        public float CpuUsage { get; set; }
        public float MemoryUsage { get; set; }
        public List<Guid> RunningWorkflows { get; set; }
    }
}
