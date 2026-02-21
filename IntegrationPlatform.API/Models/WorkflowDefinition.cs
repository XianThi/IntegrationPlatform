using IntegrationPlatform.Common.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IntegrationPlatform.API.Models
{
    [Table("WorkflowDefinitions")]
    public class WorkflowDefinition
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        [StringLength(200)]
        public string Name { get; set; }

        [StringLength(500)]
        public string Description { get; set; }

        public Guid? AssignedNodeId { get; set; }

        public DateTime? StartTime { get; set; }

        public DateTime? EndTime { get; set; }

        public int? IntervalSeconds { get; set; } // Periyodik çalışma için

        public bool IsActive { get; set; }

        public WorkflowStatus Status { get; set; }

        [Column(TypeName = "jsonb")]
        public List<WorkflowStep> Steps { get; set; }

        [Column(TypeName = "jsonb")]
        public Dictionary<string, object> GlobalVariables { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }

        public string CreatedBy { get; set; }

        // Foreign keys
        [ForeignKey(nameof(AssignedNodeId))]
        public virtual Node AssignedNode { get; set; }

        // Navigation properties
        public virtual ICollection<WorkflowExecution> Executions { get; set; }
    }

    [Table("WorkflowSteps")]
    public class WorkflowStep
    {
        [Key]
        public Guid Id { get; set; }

        public Guid WorkflowDefinitionId { get; set; }

        [Required]
        [StringLength(200)]
        public string Name { get; set; }

        public AdapterType AdapterType { get; set; }

        public int Order { get; set; }

        [Column(TypeName = "jsonb")]
        public Dictionary<string, object> Configuration { get; set; }

        [Column(TypeName = "jsonb")]
        public List<Guid> DependsOn { get; set; }

        [Column(TypeName = "jsonb")]
        public Dictionary<string, string> OutputMapping { get; set; }

        [Column(TypeName = "jsonb")]
        public Dictionary<string, string> InputMapping { get; set; }

        public bool EnableTesting { get; set; }

        public string TestData { get; set; }

        // Navigation
        [ForeignKey(nameof(WorkflowDefinitionId))]
        public virtual WorkflowDefinition WorkflowDefinition { get; set; }
    }
}
