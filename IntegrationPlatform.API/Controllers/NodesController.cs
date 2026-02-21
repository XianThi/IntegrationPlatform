using IntegrationPlatform.API.Services;
using IntegrationPlatform.Common.DTOs;
using IntegrationPlatform.Common.Enums;
using Microsoft.AspNetCore.Mvc;

namespace IntegrationPlatform.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class NodesController : ControllerBase
    {
        private readonly INodeService _nodeService;
        private readonly IWorkflowService _workflowService;
        private readonly ILogger<NodesController> _logger;

        public NodesController(
            INodeService nodeService,
            IWorkflowService workflowService,
            ILogger<NodesController> logger)
        {
            _nodeService = nodeService;
            _workflowService = workflowService;
            _logger = logger;
        }

        [HttpPost("register")]
        public async Task<ActionResult<NodeDto>> Register([FromBody] NodeRegistrationDto registration)
        {
            try
            {
                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                var node = await _nodeService.RegisterNodeAsync(registration, ipAddress);
                return Ok(node);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Node registration failed");
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("heartbeat")]
        public async Task<IActionResult> Heartbeat([FromBody] NodeHeartbeatDto heartbeat)
        {
            var success = await _nodeService.ProcessHeartbeatAsync(heartbeat);
            if (!success)
                return NotFound();

            return Ok();
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<NodeDto>>> GetAll([FromQuery] NodeStatus? status = null)
        {
            var nodes = await _nodeService.GetAllNodesAsync(status);
            return Ok(nodes);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<NodeDto>> GetById(Guid id)
        {
            var node = await _nodeService.GetNodeAsync(id);
            if (node == null)
                return NotFound();

            return Ok(node);
        }

        [HttpPut("{id}/status")]
        public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] NodeStatus status)
        {
            var success = await _nodeService.UpdateNodeStatusAsync(id, status);
            if (!success)
                return NotFound();

            return Ok();
        }

        [HttpPost("{id}/workflows")]
        public async Task<IActionResult> AssignWorkflow(Guid id, [FromBody] Guid workflowId)
        {
            try
            {
                var node = await _nodeService.AssignWorkflowToNodeAsync(id, workflowId);
                if (node == null)
                    return NotFound();

                return Ok(node);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Workflow assignment failed");
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("available")]
        public async Task<ActionResult<IEnumerable<NodeDto>>> GetAvailableNodes([FromQuery] AdapterType? adapter = null)
        {
            var nodes = await _nodeService.GetAllNodesAsync(NodeStatus.Online);
            return Ok(nodes);
        }
        /// <summary>
        /// Long polling ile node'a atanmış workflow'ları dinle
        /// </summary>
        [HttpGet("{nodeId}/assignments")]
        public async Task<ActionResult<WorkflowDefinitionDto>> GetAssignments(
            Guid nodeId,
            [FromQuery] int timeout = 30)
        {
            try
            {
                _logger.LogDebug("Node {NodeId} için assignment isteği alındı, timeout: {timeout}s", nodeId, timeout);

                // Long polling için CancellationTokenSource
                using var cts = new CancellationTokenSource();
                cts.CancelAfter(TimeSpan.FromSeconds(timeout));

                // Node'u kontrol et
                var node = await _nodeService.GetNodeAsync(nodeId);
                if (node == null)
                {
                    return NotFound($"Node bulunamadı: {nodeId}");
                }

                // Timeout olana kadar veya yeni iş gelene kadar bekle
                while (!cts.Token.IsCancellationRequested)
                {
                    // Node'a atanmış PENDING işleri getir
                    var assignedWorkflows = await _workflowService.GetAssignedWorkflowsAsync(nodeId);

                    // Idle durumdaki işleri bul (çalışmaya hazır)
                    var pendingWorkflows = assignedWorkflows?
                        .Where(w => w.Status == WorkflowStatus.Idle && w.IsActive)
                        .ToList();

                    if (pendingWorkflows != null && pendingWorkflows.Any())
                    {
                        // İlk pending işi al ve status'unu Running yap
                        var workflowToRun = pendingWorkflows.First();

                        // Workflow'u başlat (status Running yap)
                        await _workflowService.StartWorkflowAsync(workflowToRun.Id, nodeId);

                        _logger.LogInformation("Node {NodeId} için yeni workflow atandı: {WorkflowId}",
                            nodeId, workflowToRun.Id);

                        return Ok(workflowToRun);
                    }

                    // 2 saniye bekle ve tekrar dene
                    try
                    {
                        await Task.Delay(2000, cts.Token);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                }

                // Timeout oldu, boş dön
                _logger.LogDebug("Node {NodeId} için timeout, yeni iş yok", nodeId);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Assignment hatası - NodeId: {NodeId}", nodeId);
                return StatusCode(500, new { error = "Assignment sırasında hata oluştu" });
            }
        }

        /// <summary>
        /// Node'a atanmış tüm workflow'ları getir (long polling değil)
        /// </summary>
        [HttpGet("{nodeId}/workflows")]
        public async Task<ActionResult<IEnumerable<WorkflowDefinitionDto>>> GetNodeWorkflows(Guid nodeId)
        {
            try
            {
                var workflows = await _nodeService.GetAssignedWorkflowsAsync(nodeId);
                return Ok(workflows);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Node workflow listesi alınamadı - NodeId: {NodeId}", nodeId);
                return StatusCode(500, new { error = "Workflow listesi alınamadı" });
            }
        }
    }
}
