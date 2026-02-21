using IntegrationPlatform.API.Models;
using IntegrationPlatform.Common.DTOs;
using IntegrationPlatform.Common.Enums;

namespace IntegrationPlatform.API.Repositories
{
    public interface INodeRepository
    {
        Task<Node> GetByIdAsync(Guid id);
        Task<Node> GetByNameAsync(string name);
        Task<IEnumerable<Node>> GetAllAsync(NodeStatus? status = null);
        Task<Node> RegisterAsync(Node node);
        Task<bool> UpdateHeartbeatAsync(Guid id, NodeHeartbeatDto heartbeat);
        Task<bool> UpdateStatusAsync(Guid id, NodeStatus status);
        Task<IEnumerable<WorkflowDefinition>> GetAssignedWorkflowsAsync(Guid nodeId);
        Task<bool> AssignWorkflowToNodeAsync(Guid nodeId, Guid workflowId);
        Task<bool> RemoveWorkflowFromNodeAsync(Guid nodeId, Guid workflowId);
        Task<IEnumerable<Node>> GetAvailableNodesAsync(AdapterType? requiredAdapter = null);
        Task<bool> ExistsAsync(Guid id);
    }
}
