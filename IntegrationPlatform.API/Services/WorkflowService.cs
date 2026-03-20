using AutoMapper;
using IntegrationPlatform.API.Models;
using IntegrationPlatform.API.Repositories;
using IntegrationPlatform.Common.DTOs;
using IntegrationPlatform.Common.Enums;

namespace IntegrationPlatform.API.Services
{
    public class WorkflowService : IWorkflowService
    {
        private readonly IWorkflowRepository _workflowRepository;
        private readonly INodeService _nodeService;
        private readonly ILogger<WorkflowService> _logger;
        private readonly IMapper _mapper;

        public WorkflowService(
            IWorkflowRepository workflowRepository,
            INodeService nodeService,
            ILogger<WorkflowService> logger,
            IMapper mapper)
        {
            _workflowRepository = workflowRepository;
            _nodeService = nodeService;
            _logger = logger;
            _mapper = mapper;
        }

        public async Task<WorkflowDefinitionDto> CreateWorkflowAsync(CreateWorkflowDto workflowDto, string createdBy)
        {
            // Workflow adı benzersiz mi?
            var isValid = await _workflowRepository.ValidateWorkflowNameAsync(workflowDto.Name);
            if (!isValid)
            {
                throw new InvalidOperationException($"'{workflowDto.Name}' adında bir workflow zaten mevcut.");
            }
            // Step'leri sırala
            var steps = new List<WorkflowStep>();
            var tempIdToGuidMap = new Dictionary<string, Guid>();
            foreach (var stepDto in workflowDto.Steps.OrderBy(s => s.Order))
            {
                var stepId = Guid.NewGuid();
                steps.Add(new WorkflowStep
                {
                    Id = stepId,
                    Name = stepDto.Name,
                    AdapterType = stepDto.AdapterType,
                    Order = stepDto.Order,
                    Configuration = stepDto.Configuration,
                    OutputMapping = stepDto.OutputMapping,
                    InputMapping = stepDto.InputMapping,
                    EnableTesting = stepDto.EnableTesting,
                    DependsOn = new List<Guid>() // Şimdilik boş
                });

                // tempId -> Guid mapping'ini sakla
                if (!string.IsNullOrEmpty(stepDto.TempId))
                {
                    tempIdToGuidMap[stepDto.TempId] = stepId;
                }
            }
            // 2. dependsOn'ları dönüştür (tempId -> Guid)
            foreach (var step in steps)
            {
                var stepDto = workflowDto.Steps.First(s => s.TempId == GetTempIdForStep(step, workflowDto.Steps));

                if (stepDto.DependsOn != null && stepDto.DependsOn.Any())
                {
                    step.DependsOn = stepDto.DependsOn
                        .Where(tempId => tempIdToGuidMap.ContainsKey(tempId))
                        .Select(tempId => tempIdToGuidMap[tempId])
                        .ToList();
                }
            }
            var workflow = new WorkflowDefinition();
            workflow.Name = workflowDto.Name;
            workflow.Description = workflowDto.Description;
            workflow.IsActive = workflowDto.IsActive;
            workflow.GlobalVariables = workflowDto.GlobalVariables;
            workflow.Steps = steps;
            workflow.CreatedBy = createdBy;
            workflow.Id = Guid.NewGuid();
            workflow.Steps = steps;
            if (workflow.Steps != null)
            {
                for (int i = 0; i < workflow.Steps.Count; i++)
                {
                    workflow.Steps[i].Order = i + 1;
                }
            }

            var created = await _workflowRepository.CreateAsync(workflow);
            return _mapper.Map<WorkflowDefinitionDto>(created);
        }

        public async Task<WorkflowDefinitionDto> UpdateWorkflowAsync(Guid id, UpdateWorkflowDto updateDto)
        {
            // 1. Mevcut workflow'u getir
            var existingWorkflow = await _workflowRepository.GetByIdAsync(id);
            if (existingWorkflow == null)
                return null;

            // 2. Mapping için dictionary'ler
            var tempIdToGuidMap = new Dictionary<string, Guid>();
            var guidToStepMap = existingWorkflow.Steps.ToDictionary(s => s.Id, s => s);

            // 3. YENİ step'leri oluştur ve tempId mapping'ini yap
            var newSteps = new List<WorkflowStep>();

            foreach (var stepDto in updateDto.Steps.Where(s => !s.Id.HasValue))
            {
                var newStepId = Guid.NewGuid();
                newSteps.Add(new WorkflowStep
                {
                    Id = newStepId,
                    Name = stepDto.Name,
                    AdapterType = stepDto.AdapterType,
                    Order = stepDto.Order,
                    Configuration = stepDto.Configuration,
                    OutputMapping = stepDto.OutputMapping,
                    InputMapping = stepDto.InputMapping,
                    EnableTesting = stepDto.EnableTesting,
                    WorkflowDefinitionId = id,
                    DependsOn = new List<Guid>() // Şimdilik boş
                });

                if (!string.IsNullOrEmpty(stepDto.TempId))
                {
                    tempIdToGuidMap[stepDto.TempId] = newStepId;
                }
            }

            // 4. MEVCUT step'leri güncelle
            foreach (var stepDto in updateDto.Steps.Where(s => s.Id.HasValue))
            {
                if (guidToStepMap.TryGetValue(stepDto.Id.Value, out var existingStep))
                {
                    existingStep.Name = stepDto.Name;
                    existingStep.AdapterType = stepDto.AdapterType;
                    existingStep.Order = stepDto.Order;
                    existingStep.Configuration = stepDto.Configuration;
                    existingStep.OutputMapping = stepDto.OutputMapping;
                    existingStep.InputMapping = stepDto.InputMapping;
                    existingStep.EnableTesting = stepDto.EnableTesting;
                    // dependsOn'ları sonra çözümleyeceğiz
                }
            }

            // 5. TÜM step'leri birleştir (mevcut + yeni)
            var allSteps = existingWorkflow.Steps.Concat(newSteps).ToList();

            // 6. dependsOn'ları çözümle (EN KRİTİK ADIM)
            foreach (var stepDto in updateDto.Steps)
            {
                // Bu DTO hangi step'e ait?
                WorkflowStep targetStep;

                if (stepDto.Id.HasValue)
                {
                    // Mevcut step
                    targetStep = allSteps.First(s => s.Id == stepDto.Id.Value);
                }
                else
                {
                    // Yeni step
                    targetStep = allSteps.First(s => s.Id == tempIdToGuidMap[stepDto.TempId]);
                }

                // dependsOn'ları çözümle
                if (stepDto.DependsOn != null && stepDto.DependsOn.Any())
                {
                    targetStep.DependsOn = stepDto.DependsOn
                        .Select(dep => ResolveStepId(dep, guidToStepMap.Keys, tempIdToGuidMap))
                        .Where(id => id.HasValue)
                        .Select(id => id.Value)
                        .ToList();
                }
            }

            // 7. Silinen step'leri temizle
            var stepsToKeep = allSteps
                .Where(s => updateDto.Steps.Any(dto =>
                    (dto.Id.HasValue && dto.Id.Value == s.Id) ||
                    (!dto.Id.HasValue && tempIdToGuidMap.ContainsValue(s.Id))))
                .ToList();

            existingWorkflow.Steps = stepsToKeep;

            // 8. Workflow ana bilgilerini güncelle
            existingWorkflow.Name = updateDto.Name;
            existingWorkflow.Description = updateDto.Description;
            existingWorkflow.IsActive = updateDto.IsActive;
            existingWorkflow.GlobalVariables = updateDto.GlobalVariables;
            existingWorkflow.UpdatedAt = DateTime.UtcNow;

            // 9. Kaydet
            await _workflowRepository.UpdateAsync(existingWorkflow);

            return _mapper.Map<WorkflowDefinitionDto>(existingWorkflow);
        }

        private string GetTempIdForStep(WorkflowStep step, List<CreateWorkflowStepDto> stepDtos)
        {
            return stepDtos.FirstOrDefault(s => s.Name == step.Name && s.Order == step.Order)?.TempId;
        }
        private Guid? ResolveStepId(string idOrTempId, IEnumerable<Guid> existingGuids, Dictionary<string, Guid> tempIdMap)
        {
            // Önce Guid mi dene
            if (Guid.TryParse(idOrTempId, out var guid))
            {
                // Mevcut step'lerde var mı?
                if (existingGuids.Contains(guid))
                    return guid;
            }

            // tempId mapping'de var mı?
            if (tempIdMap.TryGetValue(idOrTempId, out var mappedGuid))
            {
                return mappedGuid;
            }

            // Bulunamadı - logla ama hata verme
            _logger.LogWarning("Step ID çözümlenemedi: {IdOrTempId}", idOrTempId);
            return null;
        }
        public async Task<bool> DeleteWorkflowAsync(Guid id)
        {
            return await _workflowRepository.DeleteAsync(id);
        }

        public async Task<WorkflowDefinitionDto> GetWorkflowAsync(Guid id)
        {
            var workflow = await _workflowRepository.GetByIdAsync(id);
            return _mapper.Map<WorkflowDefinitionDto>(workflow);
        }

        public async Task<IEnumerable<WorkflowDefinitionDto>> GetAllWorkflowsAsync(WorkflowStatus? status = null)
        {
            var workflows = await _workflowRepository.GetAllAsync(status);
            return _mapper.Map<IEnumerable<WorkflowDefinitionDto>>(workflows);
        }

        public async Task<WorkflowExecutionDto> StartWorkflowAsync(Guid id, Guid? nodeId = null)
        {
            var workflow = await _workflowRepository.GetByIdAsync(id);
            if (workflow == null)
            {
                throw new KeyNotFoundException($"Workflow bulunamadı: {id}");
            }

            // Validasyon
            if (!await ValidateWorkflowAsync(_mapper.Map<WorkflowDefinitionDto>(workflow)))
            {
                throw new InvalidOperationException("Workflow validasyonu başarısız.");
            }

            // Node seçimi
            Guid targetNodeId;
            if (nodeId.HasValue)
            {
                targetNodeId = nodeId.Value;
            }
            else
            {
                var optimalNode = await _nodeService.GetOptimalNodeForWorkflowAsync(
                    _mapper.Map<WorkflowDefinitionDto>(workflow));

                if (optimalNode == null)
                {
                    throw new InvalidOperationException("Bu workflow için uygun node bulunamadı.");
                }

                targetNodeId = optimalNode.Id;
            }

            // Workflow'u node'a ata ve başlat
            await _nodeService.AssignWorkflowToNodeAsync(targetNodeId, id);
            await _workflowRepository.UpdateStatusAsync(id, WorkflowStatus.Running);

            // Execution kaydı oluştur
            var execution = new WorkflowExecution
            {
                WorkflowDefinitionId = id,
                NodeId = targetNodeId,
                StartedAt = DateTime.UtcNow,
                Status = WorkflowStatus.Running
            };

            var created = await _workflowRepository.AddExecutionAsync(execution);

            _logger.LogInformation("Workflow başlatıldı: {WorkflowId} on Node: {NodeId}", id, targetNodeId);

            return _mapper.Map<WorkflowExecutionDto>(created);
        }

        public async Task<bool> StopWorkflowAsync(Guid id)
        {
            return await _workflowRepository.UpdateStatusAsync(id, WorkflowStatus.Stopped);
        }

        public async Task<bool> PauseWorkflowAsync(Guid id)
        {
            return await _workflowRepository.UpdateStatusAsync(id, WorkflowStatus.Paused);
        }

        public async Task<bool> ResumeWorkflowAsync(Guid id)
        {
            return await _workflowRepository.UpdateStatusAsync(id, WorkflowStatus.Running);
        }

        public async Task<WorkflowExecutionDto> SaveExecutionAsync(WorkflowExecutionDto execution)
        {
            var executionEntity = _mapper.Map<WorkflowExecution>(execution);

            // İlgili workflow'un status'unu güncelle
            await _workflowRepository.UpdateStatusAsync(
                execution.WorkflowId,
                execution.Status);

            var created = await _workflowRepository.AddExecutionAsync(executionEntity);
            return _mapper.Map<WorkflowExecutionDto>(created);
        }

        public async Task<IEnumerable<WorkflowExecutionDto>> GetWorkflowExecutionsAsync(Guid workflowId, int limit = 10)
        {
            var executions = await _workflowRepository.GetExecutionsAsync(workflowId, limit);
            return _mapper.Map<IEnumerable<WorkflowExecutionDto>>(executions);
        }

        public async Task<Dictionary<string, object>> GetWorkflowStatisticsAsync(Guid workflowId)
        {
            return await _workflowRepository.GetWorkflowStatisticsAsync(workflowId);
        }

        public async Task<bool> ValidateWorkflowAsync(WorkflowDefinitionDto workflow)
        {
            if (string.IsNullOrWhiteSpace(workflow.Name))
                return false;

            if (workflow.Steps == null || !workflow.Steps.Any())
                return false;

            // Step sıralamasını kontrol et
            var steps = workflow.Steps.OrderBy(s => s.Order).ToList();
            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i].Order != i + 1)
                    return false;

                // Bağımlılık kontrolü
                if (steps[i].DependsOn != null)
                {
                    foreach (var depId in steps[i].DependsOn)
                    {
                        if (!steps.Any(s => s.Id == depId))
                            return false;
                    }
                }
            }

            // Zamanlama kontrolü
            if (workflow.StartTime.HasValue && workflow.EndTime.HasValue)
            {
                if (workflow.StartTime > workflow.EndTime)
                    return false;
            }

            return true;
        }

        public async Task<IEnumerable<WorkflowDefinitionDto>> GetAssignedWorkflowsAsync(Guid nodeId)
        {
            var workflows = await _workflowRepository.GetWorkflowsByNodeIdAsync(nodeId);
            return _mapper.Map<IEnumerable<WorkflowDefinitionDto>>(workflows);
        }
        public async Task<WorkflowExecutionDto> StartWorkflowAsync(Guid workflowId, Guid nodeId)
        {
            var workflow = await _workflowRepository.GetByIdAsync(workflowId);
            if (workflow == null)
                throw new KeyNotFoundException($"Workflow bulunamadı: {workflowId}");

            // Workflow'u node'a ata (zaten atanmış olabilir)
            if (workflow.AssignedNodeId != nodeId)
            {
                workflow.AssignedNodeId = nodeId;
                await _workflowRepository.UpdateAsync(workflow);
            }

            // Status'u Running yap
            await _workflowRepository.UpdateStatusAsync(workflowId, WorkflowStatus.Running);

            // Execution kaydı oluştur
            var execution = new WorkflowExecution
            {
                Id = Guid.NewGuid(),
                WorkflowDefinitionId = workflowId,
                NodeId = nodeId,
                StartedAt = DateTime.UtcNow,
                Status = WorkflowStatus.Running,
                StepExecutions = new List<StepExecution>()
            };

            var created = await _workflowRepository.AddExecutionAsync(execution);

            _logger.LogInformation("Workflow {WorkflowId} node {NodeId}'de başlatıldı", workflowId, nodeId);

            return _mapper.Map<WorkflowExecutionDto>(created);
        }
    }
}
