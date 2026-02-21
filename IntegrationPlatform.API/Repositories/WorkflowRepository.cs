using IntegrationPlatform.API.Data;
using IntegrationPlatform.API.Models;
using IntegrationPlatform.Common.Enums;
using Microsoft.EntityFrameworkCore;

namespace IntegrationPlatform.API.Repositories
{
    public class WorkflowRepository : IWorkflowRepository
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<WorkflowRepository> _logger;

        public WorkflowRepository(ApplicationDbContext context, ILogger<WorkflowRepository> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<WorkflowDefinition> GetByIdAsync(Guid id)
        {
            return await _context.WorkflowDefinitions
                .Include(w => w.AssignedNode)
                .FirstOrDefaultAsync(w => w.Id == id);
        }

        public async Task<IEnumerable<WorkflowDefinition>> GetAllAsync(WorkflowStatus? status = null)
        {
            var query = _context.WorkflowDefinitions
                .Include(w => w.AssignedNode)
                .AsQueryable();

            if (status.HasValue)
            {
                query = query.Where(w => w.Status == status.Value);
            }

            return await query
                .OrderByDescending(w => w.CreatedAt)
                .ToListAsync();
        }

        public async Task<WorkflowDefinition> CreateAsync(WorkflowDefinition workflow)
        {
            workflow.Id = Guid.NewGuid();
            foreach (var step in workflow.Steps)
            {
                step.Id = Guid.NewGuid();
                step.WorkflowDefinitionId = workflow.Id;
            }
            workflow.CreatedAt = DateTime.UtcNow;
            workflow.Status = WorkflowStatus.Idle;
            _context.WorkflowDefinitions.Add(workflow);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Yeni workflow oluşturuldu: {WorkflowName} ({WorkflowId})",
                workflow.Name, workflow.Id);

            return workflow;
        }

        public async Task<WorkflowDefinition> UpdateAsync(WorkflowDefinition workflow)
        {
            workflow.UpdatedAt = DateTime.UtcNow;
            _context.WorkflowDefinitions.Update(workflow);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Workflow güncellendi: {WorkflowId}", workflow.Id);
            return workflow;
        }

        public async Task<bool> DeleteAsync(Guid id)
        {
            var workflow = await _context.WorkflowDefinitions.FindAsync(id);
            if (workflow == null)
            {
                return false;
            }

            _context.WorkflowDefinitions.Remove(workflow);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Workflow silindi: {WorkflowId}", id);
            return true;
        }

        public async Task<bool> UpdateStatusAsync(Guid id, WorkflowStatus status)
        {
            var workflow = await _context.WorkflowDefinitions.FindAsync(id);
            if (workflow == null)
            {
                return false;
            }

            workflow.Status = status;
            workflow.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Workflow status güncellendi: {WorkflowId} -> {Status}", id, status);
            return true;
        }

        public async Task<WorkflowExecution> AddExecutionAsync(WorkflowExecution execution)
        {
            try
            {
                execution.Id = Guid.NewGuid();

                if (execution.CompletedAt.HasValue)
                {
                    execution.TotalDuration = execution.CompletedAt.Value - execution.StartedAt;
                }

                execution.TotalRecordsProcessed = execution.StepExecutions?.Sum(s => s.ProcessedRecords) ?? 0;
                execution.ErrorMessage = "";
                _context.WorkflowExecutions.Add(execution);
                await _context.SaveChangesAsync();

                return execution;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Workflow execution eklenirken hata oluştu: {WorkflowId}", execution.WorkflowDefinitionId);
                throw;
            }
        }
        public async Task<IEnumerable<WorkflowExecution>> GetExecutionsAsync(Guid workflowId, int limit = 10)
        {
            return await _context.WorkflowExecutions
                .Where(e => e.WorkflowDefinitionId == workflowId)
                .OrderByDescending(e => e.StartedAt)
                .Take(limit)
                .ToListAsync();
        }

        public async Task<Dictionary<string, object>> GetWorkflowStatisticsAsync(Guid workflowId)
        {
            var executions = await _context.WorkflowExecutions
                .Where(e => e.WorkflowDefinitionId == workflowId)
                .ToListAsync();

            if (!executions.Any())
            {
                return new Dictionary<string, object>();
            }

            var stats = new Dictionary<string, object>
            {
                ["total_executions"] = executions.Count,
                ["successful_executions"] = executions.Count(e => e.Status == WorkflowStatus.Completed),
                ["failed_executions"] = executions.Count(e => e.Status == WorkflowStatus.Failed),
                ["average_duration_seconds"] = executions
                    .Where(e => e.TotalDuration.HasValue)
                    .Average(e => e.TotalDuration.Value.TotalSeconds),
                ["total_records_processed"] = executions.Sum(e => e.TotalRecordsProcessed),
                ["last_execution"] = executions.OrderByDescending(e => e.StartedAt).FirstOrDefault(),
                ["success_rate"] = executions.Any()
                    ? (double)executions.Count(e => e.Status == WorkflowStatus.Completed) / executions.Count() * 100
                    : 0
            };

            return stats;
        }

        public async Task<IEnumerable<WorkflowDefinition>> GetPendingWorkflowsAsync()
        {
            var now = DateTime.UtcNow;

            return await _context.WorkflowDefinitions
                .Where(w => w.IsActive && w.Status == WorkflowStatus.Idle)
                .Where(w => w.StartTime == null || w.StartTime <= now)
                .Where(w => w.EndTime == null || w.EndTime >= now)
                .Include(w => w.AssignedNode)
                .ToListAsync();
        }

        public async Task<bool> ValidateWorkflowNameAsync(string name, Guid? excludeId = null)
        {
            var query = _context.WorkflowDefinitions.Where(w => w.Name == name);

            if (excludeId.HasValue)
            {
                query = query.Where(w => w.Id != excludeId.Value);
            }

            return !await query.AnyAsync();
        }

        public async Task<IEnumerable<WorkflowDefinition>> GetWorkflowsByNodeIdAsync(Guid nodeId)
        {
            return await
                _context.WorkflowDefinitions
                    .Where(w => w.AssignedNodeId == nodeId).ToListAsync();
        }
    }
}
