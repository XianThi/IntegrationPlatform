using IntegrationPlatform.Common.DTOs;
using IntegrationPlatform.Common.Enums;

namespace IntegrationPlatform.API.Services
{
    public interface IWorkflowService
    {
        Task<WorkflowDefinitionDto> CreateWorkflowAsync(CreateWorkflowDto workflowDto, string createdBy);
        Task<WorkflowDefinitionDto> UpdateWorkflowAsync(Guid id, UpdateWorkflowDto workflowDto);
        Task<bool> DeleteWorkflowAsync(Guid id);
        Task<WorkflowDefinitionDto> GetWorkflowAsync(Guid id);
        Task<IEnumerable<WorkflowDefinitionDto>> GetAllWorkflowsAsync(WorkflowStatus? status = null);
        Task<WorkflowExecutionDto> StartWorkflowAsync(Guid id, Guid? nodeId = null);
        Task<bool> StopWorkflowAsync(Guid id);
        Task<bool> PauseWorkflowAsync(Guid id);
        Task<bool> ResumeWorkflowAsync(Guid id);
        Task<WorkflowExecutionDto> SaveExecutionAsync(WorkflowExecutionDto execution);
        Task<IEnumerable<WorkflowExecutionDto>> GetWorkflowExecutionsAsync(Guid workflowId, int limit = 10);
        Task<Dictionary<string, object>> GetWorkflowStatisticsAsync(Guid workflowId);
        Task<bool> ValidateWorkflowAsync(WorkflowDefinitionDto workflow);

        /// <summary>
        /// Node'a atanmış tüm workflow'ları getir
        /// </summary>
        Task<IEnumerable<WorkflowDefinitionDto>> GetAssignedWorkflowsAsync(Guid nodeId);

        /// <summary>
        /// Workflow'u belirli bir node'da başlat
        /// </summary>
        Task<WorkflowExecutionDto> StartWorkflowAsync(Guid workflowId, Guid nodeId);
    }
}
