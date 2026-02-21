using IntegrationPlatform.Common.DTOs;
using IntegrationPlatform.Common.Enums;

namespace IntegrationPlatform.Worker.Interfaces.Services
{
    /// <summary>
    /// Worker'ın API ile tüm iletişimini yöneten servis
    /// </summary>
    public interface IApiClientService
    {
        // Node işlemleri
        Task<NodeDto> RegisterNodeAsync(NodeRegistrationDto registration, CancellationToken cancellationToken = default);
        Task<bool> SendHeartbeatAsync(NodeHeartbeatDto heartbeat, CancellationToken cancellationToken = default);
        Task<bool> UpdateNodeStatusAsync(Guid nodeId, NodeStatus status, CancellationToken cancellationToken = default);

        // Workflow işlemleri
        Task<List<WorkflowDefinitionDto>> GetAssignedWorkflowsAsync(Guid nodeId, CancellationToken cancellationToken = default);
        Task<WorkflowDefinitionDto> GetWorkflowAssignmentAsync(Guid nodeId, int timeoutSeconds = 30, CancellationToken cancellationToken = default);
        Task<bool> ReportWorkflowExecutionAsync(WorkflowExecutionDto execution, CancellationToken cancellationToken = default);

        // Adapter işlemleri
        Task<List<string>> GetSupportedAdaptersAsync(CancellationToken cancellationToken = default);
    }
}
