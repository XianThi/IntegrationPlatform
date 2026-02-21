using IntegrationPlatform.API.Models;
using IntegrationPlatform.Common.Enums;

namespace IntegrationPlatform.API.Repositories
{
    public interface IWorkflowRepository
    {
        Task<WorkflowDefinition> GetByIdAsync(Guid id);
        Task<IEnumerable<WorkflowDefinition>> GetAllAsync(WorkflowStatus? status = null);
        Task<WorkflowDefinition> CreateAsync(WorkflowDefinition workflow);
        Task<WorkflowDefinition> UpdateAsync(WorkflowDefinition workflow);
        Task<bool> DeleteAsync(Guid id);
        Task<bool> UpdateStatusAsync(Guid id, WorkflowStatus status);
        Task<WorkflowExecution> AddExecutionAsync(WorkflowExecution execution);
        Task<IEnumerable<WorkflowExecution>> GetExecutionsAsync(Guid workflowId, int limit = 10);
        Task<Dictionary<string, object>> GetWorkflowStatisticsAsync(Guid workflowId);
        Task<IEnumerable<WorkflowDefinition>> GetPendingWorkflowsAsync();
        Task<bool> ValidateWorkflowNameAsync(string name, Guid? excludeId = null);
        /// <summary>
        /// Node'a atanmış tüm workflow'ları getir
        /// </summary>
        Task<IEnumerable<WorkflowDefinition>> GetWorkflowsByNodeIdAsync(Guid nodeId);
    }
}
