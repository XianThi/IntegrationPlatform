using IntegrationPlatform.Common.DTOs;
using IntegrationPlatform.Common.Enums;

namespace IntegrationPlatform.Common.Interfaces
{
    public interface INodeService
    {
        Task<NodeDto> RegisterNodeAsync(NodeRegistrationDto registration);
        Task<bool> SendHeartbeatAsync(NodeHeartbeatDto heartbeat);
        Task<bool> UpdateNodeStatusAsync(Guid nodeId, NodeStatus status);
        Task<IEnumerable<WorkflowDefinitionDto>> GetAssignedWorkflowsAsync(Guid nodeId);
    }
}
