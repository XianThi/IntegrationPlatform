using IntegrationPlatform.Common.DTOs;
using IntegrationPlatform.Common.Interfaces;
using IntegrationPlatform.Common.Interfaces.Plugins;
using IntegrationPlatform.Common.Models;
using IntegrationPlatform.Worker.Interfaces.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace IntegrationPlatform.Worker.Services
{
    public class TestPollingService : BackgroundService
    {
        private readonly ILogger<TestPollingService> _logger;
        private readonly IApiClientService _apiClient;
        private readonly IPluginManager _pluginManager;
        private readonly IServiceProvider _serviceProvider;
        private readonly WorkerSettings _settings;
        private Guid nodeId;
        private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);

        public TestPollingService(
            ILogger<TestPollingService> logger,
            IApiClientService apiClient,
            IPluginManager pluginManager,
            IServiceProvider serviceProvider,
            WorkerSettings settings)
        {
            _logger = logger;
            _apiClient = apiClient;
            _pluginManager = pluginManager;
            _serviceProvider = serviceProvider;
            _settings = settings;
            nodeId = GetCurrentNodeId();
            _logger.LogInformation("Test Polling Service başlatılıyor, Node ID: {NodeId}", nodeId);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Test Polling Service başlatıldı");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // API'den bekleyen test isteklerini al
                    var pendingTests = await GetPendingTestRequestsAsync(stoppingToken);

                    foreach (var testRequest in pendingTests)
                    {
                        // Her test isteğini ayrı bir scope'ta işle
                        await ProcessTestRequestAsync(testRequest, stoppingToken);
                    }

                    await Task.Delay(_pollingInterval, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Test polling döngüsünde hata");
                    await Task.Delay(_pollingInterval, stoppingToken);
                }
            }
        }

        private async Task<List<TestRequestDto>> GetPendingTestRequestsAsync(CancellationToken cancellationToken)
        {
            try
            { 
                // API'den bu node'a atanmış bekleyen test isteklerini getir
                var client = _apiClient as ApiClientService;
                if (client != null)
                {
                    // ApiClientService'e yeni metod ekleyelim
                    return await client.GetPendingTestRequestsAsync(nodeId, cancellationToken);
                }

                return new List<TestRequestDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Bekleyen test istekleri alınamadı");
                return new List<TestRequestDto>();
            }
        }

        private async Task ProcessTestRequestAsync(TestRequestDto request, CancellationToken cancellationToken)
        {
            if (request.Id == null)
            {
                _logger.LogWarning("Test isteği ID'si null, atlanıyor");
                return;
            }
            _logger.LogInformation("Test isteği işleniyor: {RequestId}, Type: {TestType}",
                request.Id, request.TestType);

            try
            {
                // Status'u Processing'e güncelle
                await UpdateTestRequestStatusAsync((Guid)request.Id, "Processing");

                TestResultDto result = null;

                if (request.TestType == "Source")
                {
                    result = await ProcessSourceTestAsync(request, cancellationToken);
                }
                else if (request.TestType == "Destination")
                {
                    result = await ProcessDestinationTestAsync(request, cancellationToken);
                }

                if (result != null)
                {
                    // Sonucu API'ye gönder
                    await SubmitTestResultAsync(result, cancellationToken);
                    await UpdateTestRequestStatusAsync((Guid)request.Id, "Completed");
                    _logger.LogInformation("Test isteği tamamlandı: {RequestId}", request.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Test isteği işlenirken hata: {RequestId}", request.Id);

                var errorResult = new TestResultDto
                {
                    RequestId = (Guid)request.Id,
                    IsSuccess = false,
                    Message = ex.Message,
                    Errors = new List<string> { ex.Message },
                    CompletedAt = DateTime.UtcNow
                };

                await SubmitTestResultAsync(errorResult, cancellationToken);
                await UpdateTestRequestStatusAsync((Guid)request.Id, "Failed");
            }
        }

        private async Task<TestResultDto> ProcessSourceTestAsync(TestRequestDto request, CancellationToken cancellationToken)
        {
            var startTime = DateTime.UtcNow;

            ISourcePlugin plugin = null;

            if (!string.IsNullOrEmpty(request.PluginId))
            {
                plugin = await _pluginManager.GetPluginAsync<ISourcePlugin>(request.PluginId);
            }
            else if (!string.IsNullOrEmpty(request.AdapterType))
            {
                var plugins = await _pluginManager.GetSourcePluginsAsync();
                plugin = plugins.FirstOrDefault(p => p.Type.ToString() == request.AdapterType);
            }

            if (plugin == null)
            {
                return new TestResultDto
                {
                    RequestId = (Guid)request.Id,
                    IsSuccess = false,
                    Message = $"Plugin bulunamadı: {request.PluginId ?? request.AdapterType}",
                    StatusCode = 404,
                    ResponseTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds,
                    CompletedAt = DateTime.UtcNow
                };
            }

            // Plugin'in test metodunu çağır
            var result = await plugin.TestConnectionAsync(request.Configuration);

            return new TestResultDto
            {
                RequestId = (Guid)request.Id,
                IsSuccess = result.IsSuccess,
                Message = result.Message,
                Result = result.SampleData,
                StatusCode = result.StatusCode,
                ResponseTimeMs = result.ResponseTime.TotalMilliseconds,
                CompletedAt = DateTime.UtcNow
            };
        }

        private async Task<TestResultDto> ProcessDestinationTestAsync(TestRequestDto request, CancellationToken cancellationToken)
        {
            var startTime = DateTime.UtcNow;

            IDestinationPlugin plugin = null;

            if (!string.IsNullOrEmpty(request.PluginId))
            {
                plugin = await _pluginManager.GetPluginAsync<IDestinationPlugin>(request.PluginId);
            }
            else if (!string.IsNullOrEmpty(request.AdapterType))
            {
                var plugins = await _pluginManager.GetDestinationPluginsAsync();
                plugin = plugins.FirstOrDefault(p => p.Type.ToString() == request.AdapterType);
            }

            if (plugin == null)
            {
                return new TestResultDto
                {
                    RequestId = (Guid)request.Id,
                    IsSuccess = false,
                    Message = $"Plugin bulunamadı: {request.PluginId ?? request.AdapterType}",
                    StatusCode = 404,
                    ResponseTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds,
                    CompletedAt = DateTime.UtcNow
                };
            }

            //var result = await plugin.TestDestinationAsync(request.Configuration, request.TestData);
            var result = await plugin.TestDestinationAsync(request.Configuration);

            return new TestResultDto
            {
                RequestId = (Guid)request.Id,
                IsSuccess = result.IsSuccess,
                Message = result.Message,
                Result = result.Details,
                StatusCode = 200,
                ResponseTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds,
                Errors = result.Errors,
                CompletedAt = DateTime.UtcNow
            };
        }

        private async Task UpdateTestRequestStatusAsync(Guid requestId, string status)
        {
            try
            {
                var client = _apiClient as ApiClientService;
                if (client != null)
                {
                    await client.UpdateTestRequestStatusAsync(requestId, status);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Test request status güncellenemedi: {RequestId}", requestId);
            }
        }

        private async Task SubmitTestResultAsync(TestResultDto result, CancellationToken cancellationToken)
        {
            try
            {
                var client = _apiClient as ApiClientService;
                if (client != null)
                {
                    await client.SubmitTestResultAsync(result, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Test sonucu gönderilemedi: {RequestId}", result.RequestId);
            }
        }

        private Guid GetCurrentNodeId()
        {
            if(_settings.NodeId == null || _settings.NodeId == Guid.Empty.ToString())
            {
                _logger.LogWarning("Node ID yapılandırılmamış, varsayılan olarak boş GUID kullanılacak");
                return GetNodeIdFromFile();
            }
            return Guid.Parse(_settings.NodeId);
        }

        private Guid GetNodeIdFromFile()
        {
            var filePath = Path.Combine(AppContext.BaseDirectory, "node_id.txt");
            if (File.Exists(filePath))
            {
                var content = File.ReadAllText(filePath);
                if (Guid.TryParse(content, out Guid nodeId))
                {
                    return nodeId;
                }
            }
            return Guid.Empty;
        }
    }
}
