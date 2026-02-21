using IntegrationPlatform.Common.DTOs;

namespace IntegrationPlatform.Worker.Interfaces.Services
{
    public interface IWorkflowExecutor
    {
        Task ExecuteAsync(WorkflowDefinitionDto workflow, CancellationToken cancellationToken);
        Task<bool> StopAsync(Guid workflowId);
    }
}
