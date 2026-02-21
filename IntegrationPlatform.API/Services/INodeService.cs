using IntegrationPlatform.Common.DTOs;
using IntegrationPlatform.Common.Enums;

namespace IntegrationPlatform.API.Services
{
    public interface INodeService
    {
        Task<NodeDto> RegisterNodeAsync(NodeRegistrationDto registration, string ipAddress);
        Task<bool> ProcessHeartbeatAsync(NodeHeartbeatDto heartbeat);
        Task<NodeDto> GetNodeAsync(Guid id);
        Task<IEnumerable<NodeDto>> GetAllNodesAsync(NodeStatus? status = null);
        Task<bool> UpdateNodeStatusAsync(Guid id, NodeStatus status);
        Task<IEnumerable<WorkflowDefinitionDto>> GetAssignedWorkflowsAsync(Guid nodeId);
        Task<NodeDto> AssignWorkflowToNodeAsync(Guid nodeId, Guid workflowId);
        Task<NodeDto> GetOptimalNodeForWorkflowAsync(WorkflowDefinitionDto workflow);
    }
}
