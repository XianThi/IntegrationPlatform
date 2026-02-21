using IntegrationPlatform.Common.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IntegrationPlatform.API.Models
{
    [Table("WorkflowExecutions")]
    public class WorkflowExecution
    {
        [Key]
        public Guid Id { get; set; }

        public Guid WorkflowDefinitionId { get; set; }

        public Guid? NodeId { get; set; }

        public DateTime StartedAt { get; set; }

        public DateTime? CompletedAt { get; set; }

        public WorkflowStatus Status { get; set; }

        public string ErrorMessage { get; set; }

        [Column(TypeName = "jsonb")]
        public List<StepExecution> StepExecutions { get; set; }

        public long TotalRecordsProcessed { get; set; }

        public TimeSpan? TotalDuration { get; set; }

        // Foreign keys
        [ForeignKey(nameof(WorkflowDefinitionId))]
        public virtual WorkflowDefinition WorkflowDefinition { get; set; }

        [ForeignKey(nameof(NodeId))]
        public virtual Node ExecutedBy { get; set; }
    }

    [Table("StepExecutions")]
    public class StepExecution
    {
        [Key]
        public Guid Id { get; set; }

        public Guid WorkflowExecutionId { get; set; }

        public Guid StepId { get; set; }

        public DateTime StartedAt { get; set; }

        public DateTime? CompletedAt { get; set; }

        public bool IsSuccess { get; set; }

        public string Error { get; set; }

        public long ProcessedRecords { get; set; }

        [Column(TypeName = "jsonb")]
        public object OutputPreview { get; set; }

        [Column(TypeName = "jsonb")]
        public Dictionary<string, object> ExecutionMetrics { get; set; }

        // Navigation
        [ForeignKey(nameof(WorkflowExecutionId))]
        public virtual WorkflowExecution WorkflowExecution { get; set; }
    }
}
