using IntegrationPlatform.Common.DTOs;

namespace IntegrationPlatform.Common.Interfaces
{
    public interface IWorkflowEngine
    {
        Task<WorkflowExecutionDto> ExecuteWorkflowAsync(WorkflowDefinitionDto workflow, CancellationToken cancellationToken = default);
        Task<bool> ValidateWorkflowAsync(WorkflowDefinitionDto workflow);
        Task<WorkflowExecutionDto> GetWorkflowStatusAsync(Guid workflowId);
        Task<bool> StopWorkflowAsync(Guid executionId);
        Task<bool> PauseWorkflowAsync(Guid executionId);
        Task<bool> ResumeWorkflowAsync(Guid executionId);
    }
}
