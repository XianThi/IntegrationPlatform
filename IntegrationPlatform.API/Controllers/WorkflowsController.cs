using IntegrationPlatform.API.Services;
using IntegrationPlatform.Common.DTOs;
using IntegrationPlatform.Common.Enums;
using Microsoft.AspNetCore.Mvc;

namespace IntegrationPlatform.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WorkflowsController : ControllerBase
    {
        private readonly IWorkflowService _workflowService;
        private readonly ILogger<WorkflowsController> _logger;

        public WorkflowsController(IWorkflowService workflowService, ILogger<WorkflowsController> logger)
        {
            _workflowService = workflowService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<WorkflowDefinitionDto>>> GetAll([FromQuery] WorkflowStatus? status = null)
        {
            var workflows = await _workflowService.GetAllWorkflowsAsync(status);
            return Ok(workflows);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<WorkflowDefinitionDto>> GetById(Guid id)
        {
            var workflow = await _workflowService.GetWorkflowAsync(id);
            if (workflow == null)
                return NotFound();

            return Ok(workflow);
        }

        [HttpPost]
        public async Task<ActionResult<WorkflowDefinitionDto>> Create([FromBody] CreateWorkflowDto workflow)
        {
            try
            {
                var created = await _workflowService.CreateWorkflowAsync(workflow, User.Identity?.Name ?? "system");
                return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Workflow creation failed");
                return StatusCode(500, new { error = "Workflow oluşturulamadı." });
            }
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<WorkflowDefinitionDto>> Update(Guid id, [FromBody] UpdateWorkflowDto workflow)
        {
            try
            {
                var updated = await _workflowService.UpdateWorkflowAsync(id, workflow);
                if (updated == null)
                    return NotFound();

                return Ok(updated);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Workflow update failed");
                return StatusCode(500, new { error = "Workflow güncellenemedi." });
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var success = await _workflowService.DeleteWorkflowAsync(id);
            if (!success)
                return NotFound();

            return NoContent();
        }

        [HttpPost("{id}/start")]
        public async Task<ActionResult<WorkflowExecutionDto>> Start(Guid id, [FromQuery] Guid? nodeId = null)
        {
            try
            {
                var execution = await _workflowService.StartWorkflowAsync(id, nodeId);
                return Ok(execution);
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Workflow start failed");
                return StatusCode(500, new { error = "Workflow başlatılamadı." });
            }
        }

        [HttpPost("{id}/stop")]
        public async Task<IActionResult> Stop(Guid id)
        {
            var success = await _workflowService.StopWorkflowAsync(id);
            if (!success)
                return NotFound();

            return Ok();
        }

        [HttpPost("{id}/pause")]
        public async Task<IActionResult> Pause(Guid id)
        {
            var success = await _workflowService.PauseWorkflowAsync(id);
            if (!success)
                return NotFound();

            return Ok();
        }

        [HttpPost("{id}/resume")]
        public async Task<IActionResult> Resume(Guid id)
        {
            var success = await _workflowService.ResumeWorkflowAsync(id);
            if (!success)
                return NotFound();

            return Ok();
        }

        [HttpGet("{id}/executions")]
        public async Task<ActionResult<IEnumerable<WorkflowExecutionDto>>> GetExecutions(Guid id, [FromQuery] int limit = 10)
        {
            var executions = await _workflowService.GetWorkflowExecutionsAsync(id, limit);
            return Ok(executions);
        }

        [HttpPost("executions")]
        public async Task<ActionResult<WorkflowExecutionDto>> SaveExecution([FromBody] WorkflowExecutionDto execution)
        {
            try
            {
                var saved = await _workflowService.SaveExecutionAsync(execution);
                return Ok(saved);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Execution save failed");
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("{id}/statistics")]
        public async Task<ActionResult<Dictionary<string, object>>> GetStatistics(Guid id)
        {
            var stats = await _workflowService.GetWorkflowStatisticsAsync(id);
            return Ok(stats);
        }

        [HttpPost("{id}/validate")]
        public async Task<ActionResult<bool>> Validate(Guid id)
        {
            var workflow = await _workflowService.GetWorkflowAsync(id);
            if (workflow == null)
                return NotFound();

            var isValid = await _workflowService.ValidateWorkflowAsync(workflow);
            return Ok(isValid);
        }
    }
}
