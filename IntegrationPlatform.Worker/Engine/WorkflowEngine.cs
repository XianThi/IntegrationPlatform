using IntegrationPlatform.Common.DTOs;
using IntegrationPlatform.Common.Enums;
using IntegrationPlatform.Common.Interfaces;
using IntegrationPlatform.Common.Interfaces.Plugins;
using IntegrationPlatform.Worker.Interfaces.Services;

namespace IntegrationPlatform.Worker.Engine
{
    public class WorkflowEngine : IWorkflowEngine
    {
        private readonly ILogger<WorkflowEngine> _logger;
        private readonly IPluginManager _pluginManager;
        private readonly Dictionary<Guid, WorkflowExecutionDto> _executions;
        private readonly Dictionary<Guid, Dictionary<Guid, object>> _stepResults; // workflowId -> (stepId -> result)
        private readonly IAdapterFactory _adapterFactory;


        public WorkflowEngine(ILogger<WorkflowEngine> logger, IPluginManager pluginManager, IAdapterFactory adapterFactory)
        {
            _logger = logger;
            _pluginManager = pluginManager;
            _executions = new Dictionary<Guid, WorkflowExecutionDto>();
            _stepResults = new Dictionary<Guid, Dictionary<Guid, object>>();
            _adapterFactory = adapterFactory;
        }

        public async Task<WorkflowExecutionDto> ExecuteWorkflowAsync(WorkflowDefinitionDto workflow, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Workflow çalıştırılıyor: {WorkflowName} ({WorkflowId})", workflow.Name, workflow.Id);

            var execution = new WorkflowExecutionDto
            {
                Id = Guid.NewGuid(),
                WorkflowId = workflow.Id,
                StartedAt = DateTime.UtcNow,
                Status = WorkflowStatus.Running,
                StepExecutions = new List<StepExecutionDto>()
            };

            _executions[execution.Id] = execution;
            _stepResults[workflow.Id] = new Dictionary<Guid, object>();

            try
            {
                // Step'leri sırayla çalıştır
                var sortedSteps = workflow.Steps.OrderBy(s => s.Order).ToList();

                foreach (var step in sortedSteps)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        execution.Status = WorkflowStatus.Stopped;
                        break;
                    }

                    var stepExecution = await ExecuteStepAsync(workflow, step, cancellationToken);
                    execution.StepExecutions.Add(stepExecution);

                    if (!stepExecution.IsSuccess)
                    {
                        execution.Status = WorkflowStatus.Failed;
                        execution.ErrorMessage = $"Step {step.Name} failed: {stepExecution.Error}";
                        break;
                    }
                }

                if (execution.Status == WorkflowStatus.Running)
                {
                    execution.Status = WorkflowStatus.Completed;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Workflow çalıştırılırken hata");
                execution.Status = WorkflowStatus.Failed;
                execution.ErrorMessage = ex.Message;
            }
            finally
            {
                execution.CompletedAt = DateTime.UtcNow;
                _stepResults.Remove(workflow.Id);
            }

            return execution;
        }

        private async Task<StepExecutionDto> ExecuteStepAsync(
            WorkflowDefinitionDto workflow,
            WorkflowStepDto step,
            CancellationToken cancellationToken)
        {
            _logger.LogDebug("Step çalıştırılıyor: {StepName} ({StepId})", step.Name, step.Id);

            var stepExecution = new StepExecutionDto
            {
                StepId = step.Id,
                StartedAt = DateTime.UtcNow
            };

            try
            {
                var direction = GetDirectionFromAdapterType(step.AdapterType, step.Configuration);
                // 1. Plugin'i bul
                var plugin = await GetPluginForStepAsync(step.AdapterType, direction);
                if (plugin == null)
                {
                    throw new Exception($"Plugin bulunamadı: {step.AdapterType}");
                }

                // 2. Önceki adımların çıktılarını topla
                var inputData = await GatherInputDataAsync(workflow.Id, step);

                // 3. Plugin tipine göre çalıştır
                object result = null;
                switch (direction)
                {
                    case AdapterDirection.Source:
                        result = await ExecuteSourceAsync(plugin as ISourcePlugin, step, inputData, cancellationToken);
                        break;

                    case AdapterDirection.Transform:
                        result = await ExecuteTransformAsync(plugin as ITransformPlugin, step, inputData, cancellationToken);
                        break;

                    case AdapterDirection.Destination:
                        result = await ExecuteDestinationAsync(plugin as IDestinationPlugin, step, inputData, cancellationToken);
                        break;

                    default:
                        throw new Exception($"Desteklenmeyen adapter tipi: {step.AdapterType} - {direction}");
                }

                // 4. Sonucu kaydet
                if (result != null)
                {
                    _stepResults[workflow.Id][step.Id] = result;

                    // Preview için ilk 3 kaydı al
                    if (result is IEnumerable<object> list)
                    {
                        stepExecution.ProcessedRecords = list.Count();
                        stepExecution.OutputPreview = System.Text.Json.JsonSerializer.Serialize(list.Take(3));
                    }
                    else
                    {
                        stepExecution.ProcessedRecords = 1;
                        stepExecution.OutputPreview = System.Text.Json.JsonSerializer.Serialize(result);
                    }
                }

                stepExecution.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Step çalıştırılırken hata: {StepName}", step.Name);
                stepExecution.IsSuccess = false;
                stepExecution.Error = ex.Message;
            }

            stepExecution.CompletedAt = DateTime.UtcNow;
            return stepExecution;
        }
        private AdapterDirection GetDirectionFromAdapterType(AdapterType adapterType, Dictionary<string, object> configuration)
        {
            return adapterType switch
            {
                // Rest SADECE source
                AdapterType.Rest => AdapterDirection.Source,

                // Json herşey olabilir - configuration'dan anlarız
                AdapterType.JsonFile => DetermineJsonDirection(configuration),
                AdapterType.JsonWriter => AdapterDirection.Destination,
                // Database source veya destination olabilir
                AdapterType.Database => configuration.ContainsKey("ConnectionString") && configuration.ContainsKey("Query")
                    ? AdapterDirection.Source
                    : AdapterDirection.Destination,

                _ => AdapterDirection.Transform
            };
        }

        private AdapterDirection DetermineJsonDirection(Dictionary<string, object> config)
        {
            // FilePath varsa ve Operation yoksa -> Source/Destination
            if (config.ContainsKey("FilePath"))
            {
                // WriteMode varsa Destination, yoksa Source
                return config.ContainsKey("WriteMode") ? AdapterDirection.Destination : AdapterDirection.Source;
            }

            // Operation varsa Transform
            if (config.ContainsKey("Operation"))
            {
                return AdapterDirection.Transform;
            }

            // Varsayılan
            return AdapterDirection.Transform;
        }
        //private async Task<IPlugin> GetPluginForStepAsync(WorkflowStepDto step)
        //{
        //    return step.AdapterType switch
        //    {
        //        AdapterType.Rest => await _pluginManager.GetPluginAsync<ISourcePlugin>("ETL.Adapter.Rest"),
        //        AdapterType.Json => await GetJsonPluginForDirectionAsync(step),
        //        _ => null
        //    };
        //}
        private async Task<IPlugin> GetPluginForStepAsync(AdapterType adapterType, AdapterDirection direction)
        {
            _logger.LogDebug("Plugin aranıyor: AdapterType={AdapterType}, Direction={Direction}",adapterType, direction);
            var pluginId = GetPluginId(adapterType);
            return direction switch
            {
                AdapterDirection.Source => await _pluginManager.GetPluginAsync<ISourcePlugin>(pluginId),
                AdapterDirection.Transform => await _pluginManager.GetPluginAsync<ITransformPlugin>(pluginId),
                AdapterDirection.Destination => await _pluginManager.GetPluginAsync<IDestinationPlugin>(pluginId),
                _ => throw new NotSupportedException($"Desteklenmeyen adapter tipi: {adapterType} - {direction}")
            };
        }
        private string GetPluginId(AdapterType adapterType)
        {
            return adapterType switch
            {
                AdapterType.Rest => "IntegrationPlatform.Adapters.Rest",
                AdapterType.JsonFile => "IntegrationPlatform.Adapters.Json",
                AdapterType.JsonWriter => "IntegrationPlatform.Adapters.Json.Writer",
                AdapterType.Database => "IntegrationPlatform.Adapters.Database",
                _ => throw new NotSupportedException($"Adapter tipi desteklenmiyor: {adapterType}")
            };
        }
        //private async Task<IPlugin> GetJsonPluginForDirectionAsync(WorkflowStepDto step)
        //{
        //    // Json plugin hepsini yapıyor, tek instance
        //    var plugin = await _pluginManager.GetPluginAsync<IPlugin>("ETL.Adapter.Json");

        //    return step.Direction switch
        //    {
        //        AdapterDirection.Source => plugin as ISourcePlugin,
        //        AdapterDirection.Transform => plugin as ITransformPlugin,
        //        AdapterDirection.Destination => plugin as IDestinationPlugin,
        //        _ => null
        //    };
        //}

        private async Task<Dictionary<string, object>> GatherInputDataAsync(Guid workflowId, WorkflowStepDto step)
        {
            var input = new Dictionary<string, object>();

            if (step.DependsOn != null && step.DependsOn.Any())
            {
                var stepResults = _stepResults.GetValueOrDefault(workflowId) ?? new Dictionary<Guid, object>();

                foreach (var depId in step.DependsOn)
                {
                    if (stepResults.TryGetValue(depId, out var result))
                    {
                        input[$"step_{depId}"] = result;
                    }
                }
            }

            return input;
        }

        private async Task<object> ExecuteSourceAsync(
            ISourcePlugin plugin,
            WorkflowStepDto step,
            Dictionary<string, object> inputData,
            CancellationToken cancellationToken)
        {
            var context = new SourceContext
            {
                Configuration = step.Configuration,
                Parameters = step.InputMapping?.ToDictionary(
                    kvp => kvp.Key,
                    kvp => (object)kvp.Value) // string, object yapmak için
            };

            var result = await plugin.FetchAsync(context);
            return result.Data;
        }

        private async Task<object> ExecuteTransformAsync(
            ITransformPlugin plugin,
            WorkflowStepDto step,
            Dictionary<string, object> inputData,
            CancellationToken cancellationToken)
        {
            // Transform için input, dependsOn'dan gelir
            var input = inputData.Values.FirstOrDefault(); // İlk bağımlılığı kullan

            if (input == null)
                throw new Exception("Transform için input verisi bulunamadı");

            var context = new TransformContext
            {
                Configuration = step.Configuration,
                Mapping = step.OutputMapping // source -> target mapping
            };

            return await plugin.TransformAsync(input, context);
        }

        private async Task<object> ExecuteDestinationAsync(
            IDestinationPlugin plugin,
            WorkflowStepDto step,
            Dictionary<string, object> inputData,
            CancellationToken cancellationToken)
        {
            // Destination için input, dependsOn'dan gelir
            var input = inputData.Values.FirstOrDefault();

            if (input == null)
                throw new Exception("Destination için input verisi bulunamadı");

            // WriteMode'u parse et
            var writeMode = WriteMode.Append;
            if (step.Configuration.TryGetValue("WriteMode", out var modeObj))
            {
                Enum.TryParse<WriteMode>(modeObj?.ToString(), out writeMode);
            }

            var context = new DestinationContext
            {
                Configuration = step.Configuration,
                WriteMode = writeMode,
                FieldMappings = step.OutputMapping
            };

            var result = await plugin.WriteAsync(input, context);

            if (!result.IsSuccess)
            {
                var errors = string.Join(", ", result.Errors?.Select(e => e.ErrorMessage) ?? new List<string>());
                throw new Exception($"Yazma başarısız: {errors}");
            }

            return new { RecordsWritten = result.RecordsWritten };
        }

        public Task<WorkflowExecutionDto> GetWorkflowStatusAsync(Guid workflowId)
        {
            var execution = _executions.Values.LastOrDefault(e => e.WorkflowId == workflowId);
            return Task.FromResult(execution);
        }

        public Task<bool> ValidateWorkflowAsync(WorkflowDefinitionDto workflow)
        {
            // Basit validasyon
            if (workflow.Steps == null || !workflow.Steps.Any())
                return Task.FromResult(false);

            // Sıra kontrolü
            var orders = workflow.Steps.Select(s => s.Order).ToList();
            if (orders.Distinct().Count() != orders.Count)
                return Task.FromResult(false);

            return Task.FromResult(true);
        }

        public Task<bool> StopWorkflowAsync(Guid executionId)
        {
            if (_executions.TryGetValue(executionId, out var execution))
            {
                execution.Status = WorkflowStatus.Stopped;
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        public Task<bool> PauseWorkflowAsync(Guid executionId)
        {
            if (_executions.TryGetValue(executionId, out var execution))
            {
                execution.Status = WorkflowStatus.Paused;
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        public Task<bool> ResumeWorkflowAsync(Guid executionId)
        {
            if (_executions.TryGetValue(executionId, out var execution))
            {
                execution.Status = WorkflowStatus.Running;
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }
    }
}
