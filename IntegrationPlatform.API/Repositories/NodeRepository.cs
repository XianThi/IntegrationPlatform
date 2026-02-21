using IntegrationPlatform.API.Data;
using IntegrationPlatform.API.Models;
using IntegrationPlatform.Common.DTOs;
using IntegrationPlatform.Common.Enums;
using Microsoft.EntityFrameworkCore;

namespace IntegrationPlatform.API.Repositories
{
    public class NodeRepository : INodeRepository
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<NodeRepository> _logger;

        public NodeRepository(ApplicationDbContext context, ILogger<NodeRepository> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<Node> GetByIdAsync(Guid id)
        {
            return await _context.Nodes
                .Include(n => n.AssignedWorkflows)
                .FirstOrDefaultAsync(n => n.Id == id);
        }

        public async Task<Node> GetByNameAsync(string name)
        {
            return await _context.Nodes
                .FirstOrDefaultAsync(n => n.NodeName == name);
        }

        public async Task<IEnumerable<Node>> GetAllAsync(NodeStatus? status = null)
        {
            var query = _context.Nodes.AsQueryable();

            if (status.HasValue)
            {
                query = query.Where(n => n.Status == status.Value);
            }

            return await query
                .OrderByDescending(n => n.LastHeartbeat)
                .ToListAsync();
        }

        public async Task<Node> RegisterAsync(Node node)
        {
            node.Id = Guid.NewGuid();
            node.RegisteredAt = DateTime.UtcNow;
            node.LastHeartbeat = DateTime.UtcNow;
            node.Status = NodeStatus.Online;

            _context.Nodes.Add(node);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Yeni node kaydedildi: {NodeName} ({NodeId})", node.NodeName, node.Id);
            return node;
        }

        public async Task<bool> UpdateHeartbeatAsync(Guid id, NodeHeartbeatDto heartbeat)
        {
            var node = await _context.Nodes.FindAsync(id);
            if (node == null)
            {
                return false;
            }

            node.LastHeartbeat = DateTime.UtcNow;
            node.Status = heartbeat.Status;
            node.Metrics = new Dictionary<string, object>
            {
                ["cpu_usage"] = heartbeat.CpuUsage,
                ["memory_usage"] = heartbeat.MemoryUsage,
                ["workload"] = heartbeat.CurrentWorkload,
                ["running_workflows"] = heartbeat.RunningWorkflows
            };

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> UpdateStatusAsync(Guid id, NodeStatus status)
        {
            var node = await _context.Nodes.FindAsync(id);
            if (node == null)
            {
                return false;
            }

            node.Status = status;
            node.LastHeartbeat = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Node status güncellendi: {NodeId} -> {Status}", id, status);
            return true;
        }

        public async Task<IEnumerable<WorkflowDefinition>> GetAssignedWorkflowsAsync(Guid nodeId)
        {
            return await _context.WorkflowDefinitions
                .Where(w => w.AssignedNodeId == nodeId && w.IsActive)
                .OrderBy(w => w.CreatedAt)
                .ToListAsync();
        }

        public async Task<bool> AssignWorkflowToNodeAsync(Guid nodeId, Guid workflowId)
        {
            var workflow = await _context.WorkflowDefinitions.FindAsync(workflowId);
            if (workflow == null)
            {
                return false;
            }

            workflow.AssignedNodeId = nodeId;
            workflow.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Workflow {WorkflowId} node {NodeId}'ye atandı", workflowId, nodeId);
            return true;
        }

        public async Task<bool> RemoveWorkflowFromNodeAsync(Guid nodeId, Guid workflowId)
        {
            var workflow = await _context.WorkflowDefinitions
                .FirstOrDefaultAsync(w => w.Id == workflowId && w.AssignedNodeId == nodeId);

            if (workflow == null)
            {
                return false;
            }

            workflow.AssignedNodeId = null;
            workflow.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<IEnumerable<Node>> GetAvailableNodesAsync(AdapterType? requiredAdapter = null)
        {
            var query = _context.Nodes
                .Where(n => n.Status == NodeStatus.Online || n.Status == NodeStatus.Busy)
                .Where(n => n.LastHeartbeat > DateTime.UtcNow.AddMinutes(-5));

            if (requiredAdapter.HasValue)
            {
                // JSONB'de adapter desteğini kontrol et
                query = query.Where(n => EF.Functions.JsonContains(n.SupportedAdapters,
                    $"[{{\"type\":\"{requiredAdapter.Value}\"}}]"));
            }

            return await query
                .OrderBy(n => n.Metrics["workload"])
                .ToListAsync();
        }

        public async Task<bool> ExistsAsync(Guid id)
        {
            return await _context.Nodes.AnyAsync(n => n.Id == id);
        }

        public async Task<IEnumerable<WorkflowDefinition>> GetWorkflowsByNodeIdAsync(Guid nodeId)
        {
            return await _context.WorkflowDefinitions
                .Include(w => w.AssignedNode)
                .Where(w => w.AssignedNodeId == nodeId)
                .OrderBy(w => w.CreatedAt)
                .ToListAsync();
        }
    }
}
