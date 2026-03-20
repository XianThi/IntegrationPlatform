using IntegrationPlatform.Common.DTOs;
using IntegrationPlatform.Common.Enums;
using IntegrationPlatform.Common.Interfaces;
using IntegrationPlatform.Common.Interfaces.Plugins;
using IntegrationPlatform.Worker.Interfaces.Services;
using Microsoft.Extensions.Options;

namespace IntegrationPlatform.Worker;
public class WorkerService : BackgroundService
{
    private readonly ILogger<WorkerService> _logger;
    private readonly IApiClientService _apiClient;
    private readonly IPluginManager _pluginManager;
    private readonly IWorkflowEngine _workflowEngine;
    private readonly WorkerSettings _settings;

    private NodeDto _currentNode;
    private Timer _heartbeatTimer;
    private readonly List<WorkflowDefinitionDto> _assignedWorkflows;
    private readonly Dictionary<Guid, CancellationTokenSource> _runningWorkflows;

    public WorkerService(
        ILogger<WorkerService> logger,
         IApiClientService apiClient,
        IPluginManager pluginManager,
        IWorkflowEngine workflowEngine,
        IOptions<WorkerSettings> settings)
    {
        _logger = logger;
        _apiClient = apiClient;
        _pluginManager = pluginManager;
        _workflowEngine = workflowEngine;
        _settings = settings.Value;

        _assignedWorkflows = new List<WorkflowDefinitionDto>();
        _runningWorkflows = new Dictionary<Guid, CancellationTokenSource>();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker Service baþlatýlýyor...");

        try
        {
            // 1. Plugin'leri yükle
            await LoadAdaptersAsync();

            // 2. API'ye register ol
            await RegisterNodeAsync();

            // 3. Heartbeat timer'ý baþlat
            StartHeartbeatTimer();

            // 4. Atanmýþ iþleri al ve çalýþtýr
            await LoadAndStartAssignedWorkflowsAsync();

            // 5. API'den yeni iþ atamalarýný dinle
            await ListenForWorkflowAssignmentsAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Worker Service baþlatýlýrken hata oluþtu");
            throw;
        }
    }

    private async Task LoadAdaptersAsync()
    {
        _logger.LogInformation("Plugin'ler yükleniyor...");

        // Plugin'leri yükle
        var plugins = await _pluginManager.LoadPluginsAsync(_settings.AdaptersPath);

        // Source plugin'leri listele
        var sourcePlugins = await _pluginManager.GetSourcePluginsAsync();
        foreach (var plugin in sourcePlugins)
        {
            _logger.LogInformation("  Source Plugin: {Name} v{Version}", plugin.Name, plugin.Version);

            // Metadata'sýný al
            var metadata = await plugin.GetMetadataAsync();
            _logger.LogDebug("    Schema: {Count} parametre", metadata.ConfigurationSchema.Count);
        }

        // Destination plugin'leri listele
        var destPlugins = await _pluginManager.GetDestinationPluginsAsync();
        foreach (var plugin in destPlugins)
        {
            _logger.LogInformation("  Destination Plugin: {Name} v{Version}", plugin.Name, plugin.Version);
        }

        // Plugin'leri API'ye bildir (node registration'da kullanýlacak)
        //_supportedAdapters = plugins.Select(p => p.Name).ToList();
    }

    private async Task RegisterNodeAsync()
    {
        _logger.LogInformation("API'ye register olunuyor...");

        // Plugin'leri al
        var plugins = await _pluginManager.GetSourcePluginsAsync();
        var transformPlugins = await _pluginManager.GetTransformPluginsAsync();
        var destinationPlugins = await _pluginManager.GetDestinationPluginsAsync();

        var allPlugins = plugins.Cast<IPlugin>()
            .Concat(transformPlugins)
            .Concat(destinationPlugins)
            .ToList();

        var registration = new NodeRegistrationDto
        {
            NodeName = _settings.NodeName ?? Environment.MachineName,
            MachineName = Environment.MachineName,
            OperatingSystem = Environment.OSVersion.ToString(),
            Version = GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0",
            ProcessorCount = Environment.ProcessorCount,
            TotalMemory = GetTotalMemory(),
            SupportedAdapters = allPlugins.Select(p => $"{p.Type}:{p.Name}").ToList()
        };

        _currentNode = await _apiClient.RegisterNodeAsync(registration);
        _settings.NodeId = _currentNode.Id.ToString();
        WriteNodeIdToFile(_currentNode.Id);
        _logger.LogInformation("Node baþarýyla register oldu. Node ID: {NodeId}", _currentNode.Id);
    }

    private async void StartHeartbeatTimer()
    {
        _heartbeatTimer = new Timer(async _ => await SendHeartbeatAsync(),
            null,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(_settings.HeartbeatIntervalSeconds));
    }
    private async Task SendHeartbeatAsync()
    {
        try
        {
            var heartbeat = new NodeHeartbeatDto
            {
                NodeId = _currentNode.Id,
                Status = _runningWorkflows.Any() ? NodeStatus.Busy : NodeStatus.Online,
                CurrentWorkload = _runningWorkflows.Count,
                CpuUsage = GetCpuUsage(),
                MemoryUsage = GetMemoryUsage(),
                RunningWorkflows = _runningWorkflows.Keys.ToList()
            };

            // ApiClientService kullan
            await _apiClient.SendHeartbeatAsync(heartbeat);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Heartbeat gönderilirken hata oluþtu");
        }
    }

    private async Task LoadAndStartAssignedWorkflowsAsync()
    {
        _logger.LogInformation("Atanmýþ iþ akýþlarý yükleniyor...");

        // ApiClientService kullan
        var workflows = await _apiClient.GetAssignedWorkflowsAsync(_currentNode.Id);

        if (workflows != null)
        {
            _assignedWorkflows.AddRange(workflows);

            foreach (var workflow in workflows.Where(w => w.IsActive))
            {
                await StartWorkflowAsync(workflow);
            }
        }
    }

    private async Task ListenForWorkflowAssignmentsAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // ApiClientService ile long polling
                var newWorkflow = await _apiClient.GetWorkflowAssignmentAsync(
                    _currentNode.Id,
                    30, // timeout
                    stoppingToken);

                if (newWorkflow != null)
                {
                    _logger.LogInformation("Yeni iþ akýþý atandý: {WorkflowName} ({WorkflowId})",
                        newWorkflow.Name, newWorkflow.Id);

                    _assignedWorkflows.Add(newWorkflow);
                    await StartWorkflowAsync(newWorkflow);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ýþ atamalarý dinlenirken hata oluþtu");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }

    private async Task StartWorkflowAsync(WorkflowDefinitionDto workflow)
    {
        try
        {
            if (_runningWorkflows.ContainsKey(workflow.Id))
            {
                _logger.LogWarning("Ýþ akýþý zaten çalýþýyor: {WorkflowId}", workflow.Id);
                return;
            }

            var cts = new CancellationTokenSource();
            _runningWorkflows[workflow.Id] = cts;

            _logger.LogInformation("Ýþ akýþý baþlatýlýyor: {WorkflowName}", workflow.Name);

            // Ýþ akýþýný arka planda çalýþtýr
            _ = Task.Run(async () =>
            {
                try
                {
                    var execution = await _workflowEngine.ExecuteWorkflowAsync(workflow, cts.Token);

                    if (execution.Status == WorkflowStatus.Completed)
                    {
                        _logger.LogInformation("Ýþ akýþý baþarýyla tamamlandý: {WorkflowName}", workflow.Name);
                    }
                    else if (execution.Status == WorkflowStatus.Failed)
                    {
                        _logger.LogError("Ýþ akýþý baþarýsýz oldu: {WorkflowName}, Hata: {Error}",
                            workflow.Name, execution.ErrorMessage);
                    }
                    // Çalýþma sonucunu API'ye bildir
                    await ReportWorkflowCompletionAsync(execution);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ýþ akýþý çalýþtýrýlýrken hata: {WorkflowName}", workflow.Name);
                }
                finally
                {
                    _runningWorkflows.Remove(workflow.Id);
                    cts.Dispose();
                }
            }, cts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ýþ akýþý baþlatýlamadý: {WorkflowName}", workflow.Name);
        }
    }

    private async Task ReportWorkflowCompletionAsync(WorkflowExecutionDto execution)
    {
        try
        {
            // ApiClientService kullan
            await _apiClient.ReportWorkflowExecutionAsync(execution);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ýþ akýþý tamamlanma raporu gönderilemedi");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Worker Service durduruluyor...");

        // Tüm çalýþan iþ akýþlarýný durdur
        foreach (var (workflowId, cts) in _runningWorkflows)
        {
            _logger.LogInformation("Ýþ akýþý durduruluyor: {WorkflowId}", workflowId);
            cts.Cancel();
        }

        _heartbeatTimer?.Dispose();
        try
        {
            // Node'u offline olarak iþaretle
            await UpdateNodeStatusAsync(NodeStatus.Offline);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Node durdurulurken hata oluþtu");
        }
        await base.StopAsync(cancellationToken);
    }

    private async Task UpdateNodeStatusAsync(NodeStatus status)
    {
        try
        {
            if (_currentNode == null)
            {
                _logger.LogWarning("Node bilgisi mevcut deðil, status güncellenemiyor");
                return;
            }
            await _apiClient.UpdateNodeStatusAsync(_currentNode.Id, status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Node status güncellenemedi");
        }
    }

    // Yardýmcý metodlar
    private long GetTotalMemory()
    {
        // Cross-platform memory okuma
        return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
    }

    private float GetCpuUsage()
    {
        // Basit CPU kullanýmý hesaplama
        // Gerçek uygulamada PerformanceCounter veya System.Diagnostics kullanýlabilir
        return new Random().Next(0, 100);
    }

    private float GetMemoryUsage()
    {
        var process = System.Diagnostics.Process.GetCurrentProcess();
        return (float)(process.WorkingSet64 / (double)GetTotalMemory() * 100);
    }

    private void WriteNodeIdToFile(Guid nodeId)
    {
        try
        {
            var filePath = Path.Combine(AppContext.BaseDirectory, "node_id.txt");
            File.WriteAllText(filePath, nodeId.ToString());
            _logger.LogInformation("Node ID dosyaya yazýldý: {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Node ID dosyaya yazýlýrken hata oluþtu");
        }
    }
}
