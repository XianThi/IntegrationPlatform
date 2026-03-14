using IntegrationPlatform.Common.DTOs;
using IntegrationPlatform.Common.Enums;
using IntegrationPlatform.Worker.Interfaces.Services;
using System.Net.Http.Json;
using System.Text.Json;

namespace IntegrationPlatform.Worker.Services
{

    public class ApiClientService : IApiClientService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<ApiClientService> _logger;
        private readonly JsonSerializerOptions _jsonOptions;

        public ApiClientService(HttpClient httpClient, ILogger<ApiClientService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            _logger.LogDebug("HttpClient BaseAddress: {BaseAddress}", _httpClient.BaseAddress?.ToString() ?? "NULL!");

            if (_httpClient.BaseAddress == null)
            {
                _logger.LogWarning("BaseAddress NULL! API çağrıları patlayacak!");
            }
        }

        #region Node Operations

        /// <summary>
        /// Worker'ı API'ye kaydeder
        /// </summary>
        public async Task<NodeDto> RegisterNodeAsync(NodeRegistrationDto registration, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("API'ye kayıt gönderiliyor: {NodeName}", registration.NodeName);

                var response = await _httpClient.PostAsJsonAsync("api/nodes/register", registration, _jsonOptions, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var node = await response.Content.ReadFromJsonAsync<NodeDto>(_jsonOptions, cancellationToken);
                    _logger.LogInformation("Kayıt başarılı. Node ID: {NodeId}", node?.Id);
                    return node;
                }

                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Kayıt başarısız. Status: {StatusCode}, Error: {Error}",
                    response.StatusCode, error);

                throw new HttpRequestException($"Node registration failed: {response.StatusCode} - {error}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Node registration hatası");
                throw;
            }
        }

        /// <summary>
        /// Heartbeat gönderir
        /// </summary>
        public async Task<bool> SendHeartbeatAsync(NodeHeartbeatDto heartbeat, CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync("api/nodes/heartbeat", heartbeat, _jsonOptions, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    return true;
                }

                _logger.LogWarning("Heartbeat gönderilemedi. Status: {StatusCode}", response.StatusCode);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Heartbeat gönderilirken hata");
                return false;
            }
        }

        /// <summary>
        /// Node durumunu günceller
        /// </summary>
        public async Task<bool> UpdateNodeStatusAsync(Guid nodeId, NodeStatus status, CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _httpClient.PutAsJsonAsync($"api/nodes/{nodeId}/status", status, _jsonOptions, cancellationToken);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Node status güncellenirken hata");
                return false;
            }
        }

        #endregion

        #region Workflow Operations

        /// <summary>
        /// Node'a atanmış tüm workflow'ları getirir
        /// </summary>
        public async Task<List<WorkflowDefinitionDto>> GetAssignedWorkflowsAsync(Guid nodeId, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogDebug("Node {NodeId} için atanmış workflow'lar getiriliyor", nodeId);

                var response = await _httpClient.GetAsync($"api/nodes/{nodeId}/workflows", cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var workflows = await response.Content.ReadFromJsonAsync<List<WorkflowDefinitionDto>>(_jsonOptions, cancellationToken);
                    _logger.LogDebug("{Count} workflow bulundu", workflows?.Count ?? 0);
                    return workflows ?? new List<WorkflowDefinitionDto>();
                }

                _logger.LogWarning("Workflow'lar getirilemedi. Status: {StatusCode}", response.StatusCode);
                return new List<WorkflowDefinitionDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Workflow'lar getirilirken hata");
                return new List<WorkflowDefinitionDto>();
            }
        }

        /// <summary>
        /// Long polling ile yeni workflow atamalarını dinler
        /// </summary>
        public async Task<WorkflowDefinitionDto> GetWorkflowAssignmentAsync(Guid nodeId, int timeoutSeconds = 30, CancellationToken cancellationToken = default)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds + 5)); // Timeout + buffer

                var response = await _httpClient.GetAsync($"api/nodes/{nodeId}/assignments?timeout={timeoutSeconds}", cts.Token);

                if (response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                    {
                        return null; // Timeout, yeni atama yok
                    }

                    var workflow = await response.Content.ReadFromJsonAsync<WorkflowDefinitionDto>(_jsonOptions, cts.Token);
                    _logger.LogInformation("Yeni workflow ataması alındı: {WorkflowName} ({WorkflowId})",
                        workflow?.Name, workflow?.Id);

                    return workflow;
                }

                _logger.LogWarning("Workflow assignment hatası. Status: {StatusCode}", response.StatusCode);
                return null;
            }
            catch (OperationCanceledException)
            {
                _logger.LogTrace("Workflow assignment timeout");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Workflow assignment dinlenirken hata");
                return null;
            }
        }

        /// <summary>
        /// Workflow execution sonucunu API'ye raporlar
        /// </summary>
        public async Task<bool> ReportWorkflowExecutionAsync(WorkflowExecutionDto execution, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Workflow execution raporlanıyor: {WorkflowId}, Status: {Status}",
                    execution.WorkflowId, execution.Status);
                var response = await _httpClient.PostAsJsonAsync("api/workflows/executions", execution, _jsonOptions, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogDebug("Execution raporu başarıyla gönderildi");
                    return true;
                }

                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Execution raporu gönderilemedi. Status: {StatusCode}, Error: {Error}",
                    response.StatusCode, error);

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Execution raporlanırken hata");
                return false;
            }
        }

        #endregion

        #region Adapter Operations

        /// <summary>
        /// API'den desteklenen adapter'ların listesini getirir
        /// </summary>
        public async Task<List<string>> GetSupportedAdaptersAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _httpClient.GetAsync("api/adapters", cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var adapters = await response.Content.ReadFromJsonAsync<List<string>>(_jsonOptions, cancellationToken);
                    return adapters ?? new List<string>();
                }

                return new List<string>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Adapter listesi alınamadı");
                return new List<string>();
            }
        }

        #endregion

        #region Health Check

        /// <summary>
        /// API'nin sağlık durumunu kontrol eder
        /// </summary>
        public async Task<bool> CheckApiHealthAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _httpClient.GetAsync("health", cancellationToken);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        #endregion
    }
}
