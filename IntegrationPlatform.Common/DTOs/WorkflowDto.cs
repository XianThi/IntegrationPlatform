using IntegrationPlatform.Common.Enums;
using System.Text.Json.Serialization;

namespace IntegrationPlatform.Common.DTOs
{
    public class WorkflowDefinitionDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public Guid? AssignedNodeId { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public int? IntervalSeconds { get; set; }
        public bool IsActive { get; set; }
        public WorkflowStatus Status { get; set; }
        public List<WorkflowStepDto> Steps { get; set; }
        public Dictionary<string, object> GlobalVariables { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class WorkflowStepDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public AdapterType AdapterType { get; set; }
        public int Order { get; set; }
        public Dictionary<string, object> Configuration { get; set; }
        public List<Guid> DependsOn { get; set; } // Önceki adımlar
        public Dictionary<string, string> OutputMapping { get; set; } // Çıktı eşleme
        public Dictionary<string, string> InputMapping { get; set; } // Girdi eşleme
        public bool EnableTesting { get; set; }
        public string? TestData { get; set; }
    }

    public class WorkflowExecutionDto
    {
        public Guid Id { get; set; }
        public Guid WorkflowId { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public WorkflowStatus Status { get; set; }
        public string? ErrorMessage { get; set; }
        public List<StepExecutionDto> StepExecutions { get; set; }
    }

    public class StepExecutionDto
    {
        public Guid StepId { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public bool IsSuccess { get; set; }
        public string? Error { get; set; }
        public long ProcessedRecords { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string OutputPreview { get; set; } = string.Empty;
    }

    public class UpdateWorkflowDto
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public List<UpdateWorkflowStepDto> Steps { get; set; }
        public bool IsActive { get; set; }
        public Dictionary<string, object> GlobalVariables { get; set; }
    }

    public class UpdateWorkflowStepDto
    {
        public Guid? Id { get; set; }  // Var olan adımlar için ID, yeni adımlar için null
        public string? TempId { get; set; }  // UI'dan gelen geçici ID
        public string Name { get; set; }
        public AdapterType AdapterType { get; set; }
        public int Order { get; set; }
        public Dictionary<string, object> Configuration { get; set; }
        public List<string> DependsOn { get; set; }  // Önceki adımların ID'leri
        public Dictionary<string, string> OutputMapping { get; set; }
        public Dictionary<string, string> InputMapping { get; set; }
        public bool EnableTesting { get; set; }
    }
    public class CreateWorkflowDto
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public List<CreateWorkflowStepDto> Steps { get; set; }
        public bool IsActive { get; set; }
        public Dictionary<string, object> GlobalVariables { get; set; }
    }
    public class CreateWorkflowStepDto
    {
        public string TempId { get; set; }  // UI'dan gelen geçici ID
        public string Name { get; set; }
        public AdapterType AdapterType { get; set; }
        public int Order { get; set; }
        public Dictionary<string, object> Configuration { get; set; }
        public List<string> DependsOn { get; set; }  // tempId'ler
        public Dictionary<string, string> OutputMapping { get; set; }
        public Dictionary<string, string> InputMapping { get; set; }
        public bool EnableTesting { get; set; }
    }
}
