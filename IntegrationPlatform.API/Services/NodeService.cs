using AutoMapper;
using IntegrationPlatform.API.Models;
using IntegrationPlatform.API.Repositories;
using IntegrationPlatform.Common.DTOs;
using IntegrationPlatform.Common.Enums;

namespace IntegrationPlatform.API.Services
{
    public class NodeService : INodeService
    {
        private readonly INodeRepository _nodeRepository;
        private readonly IWorkflowRepository _workflowRepository;
        private readonly ILogger<NodeService> _logger;
        private readonly IMapper _mapper;

        public NodeService(
            INodeRepository nodeRepository,
            IWorkflowRepository workflowRepository,
            ILogger<NodeService> logger,
            IMapper mapper)
        {
            _nodeRepository = nodeRepository;
            _workflowRepository = workflowRepository;
            _logger = logger;
            _mapper = mapper;
        }

        public async Task<NodeDto> RegisterNodeAsync(NodeRegistrationDto registration, string ipAddress)
        {
            var existingNode = await _nodeRepository.GetByNameAsync(registration.NodeName);
            if (existingNode != null)
            {
                // Node zaten varsa, bilgilerini güncelle
                existingNode.IpAddress = ipAddress;
                existingNode.OperatingSystem = registration.OperatingSystem;
                existingNode.Version = registration.Version;
                existingNode.ProcessorCount = registration.ProcessorCount;
                existingNode.TotalMemory = registration.TotalMemory;
                existingNode.SupportedAdapters = registration.SupportedAdapters;
                existingNode.Status = NodeStatus.Online;
                existingNode.LastHeartbeat = DateTime.UtcNow;

                await _nodeRepository.UpdateHeartbeatAsync(existingNode.Id, new NodeHeartbeatDto
                {
                    NodeId = existingNode.Id,
                    Status = NodeStatus.Online,
                    CpuUsage = 0,
                    MemoryUsage = 0
                });

                return _mapper.Map<NodeDto>(existingNode);
            }

            var node = new Node
            {
                NodeName = registration.NodeName,
                MachineName = registration.MachineName,
                IpAddress = ipAddress,
                OperatingSystem = registration.OperatingSystem,
                Version = registration.Version,
                ProcessorCount = registration.ProcessorCount,
                TotalMemory = registration.TotalMemory,
                SupportedAdapters = registration.SupportedAdapters
            };

            var created = await _nodeRepository.RegisterAsync(node);
            return _mapper.Map<NodeDto>(created);
        }

        public async Task<bool> ProcessHeartbeatAsync(NodeHeartbeatDto heartbeat)
        {
            return await _nodeRepository.UpdateHeartbeatAsync(heartbeat.NodeId, heartbeat);
        }

        public async Task<NodeDto> GetNodeAsync(Guid id)
        {
            var node = await _nodeRepository.GetByIdAsync(id);
            return _mapper.Map<NodeDto>(node);
        }

        public async Task<IEnumerable<NodeDto>> GetAllNodesAsync(NodeStatus? status = null)
        {
            var nodes = await _nodeRepository.GetAllAsync(status);
            return _mapper.Map<IEnumerable<NodeDto>>(nodes);
        }

        public async Task<bool> UpdateNodeStatusAsync(Guid id, NodeStatus status)
        {
            return await _nodeRepository.UpdateStatusAsync(id, status);
        }

        public async Task<IEnumerable<WorkflowDefinitionDto>> GetAssignedWorkflowsAsync(Guid nodeId)
        {
            var workflows = await _nodeRepository.GetAssignedWorkflowsAsync(nodeId);
            return _mapper.Map<IEnumerable<WorkflowDefinitionDto>>(workflows);
        }

        public async Task<NodeDto> AssignWorkflowToNodeAsync(Guid nodeId, Guid workflowId)
        {
            var success = await _nodeRepository.AssignWorkflowToNodeAsync(nodeId, workflowId);
            if (!success)
                return null;

            return await GetNodeAsync(nodeId);
        }

        public async Task<NodeDto> GetOptimalNodeForWorkflowAsync(WorkflowDefinitionDto workflow)
        {
            // Workflow için gereken adapter'ları bul
            var requiredAdapters = workflow.Steps?
                .Select(s => s.AdapterType)
                .Distinct()
                .ToList() ?? new List<AdapterType>();

            // Uygun node'ları bul
            var availableNodes = await _nodeRepository.GetAllAsync(NodeStatus.Online);

            // Adapter desteğine göre filtrele
            var compatibleNodes = availableNodes
                .Where(n => n.SupportedAdapters != null)
                .Where(n => !requiredAdapters.Any() || requiredAdapters.All(ra =>
                    n.SupportedAdapters.Contains(ra.ToString())))
                .ToList();

            if (!compatibleNodes.Any())
            {
                return null;
            }

            // En az yüklü node'u seç (basit load balancing)
            var selectedNode = compatibleNodes
                .OrderBy(n => n.Metrics != null && n.Metrics.ContainsKey("workload")
                    ? Convert.ToInt32(n.Metrics["workload"])
                    : 0)
                .First();

            return _mapper.Map<NodeDto>(selectedNode);
        }
    }
}
