using IntegrationPlatform.Common.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IntegrationPlatform.API.Models
{
    [Table("Nodes")]
    public class Node
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        [StringLength(100)]
        public string NodeName { get; set; }

        [Required]
        [StringLength(100)]
        public string MachineName { get; set; }

        [StringLength(50)]
        public string IpAddress { get; set; }

        [StringLength(100)]
        public string OperatingSystem { get; set; }

        public string Version { get; set; }

        public int ProcessorCount { get; set; }

        public long TotalMemory { get; set; }

        [Column(TypeName = "jsonb")]
        public List<string> SupportedAdapters { get; set; } = new List<string>();

        public NodeStatus Status { get; set; }

        public DateTime LastHeartbeat { get; set; }

        public DateTime RegisteredAt { get; set; }

        [Column(TypeName = "jsonb")]
        public Dictionary<string, object> Metrics { get; set; } = new Dictionary<string, object>();

        // Navigation properties
        public virtual ICollection<WorkflowDefinition> AssignedWorkflows { get; set; }
    }
}
