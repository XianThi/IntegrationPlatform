using IntegrationPlatform.Common.Enums;
using IntegrationPlatform.Common.Interfaces.Plugins;
using System.Text.Json;

namespace IntegrationPlatform.API.Services
{
    public class TestService : ITestService
    {
        private readonly ILogger<TestService> _logger;
        private readonly JsonSerializerOptions _jsonOptions;

        public TestService(ILogger<TestService> logger)
        {
            _logger = logger;
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
        }

        public async Task<SourceTestResult> TestSourceAsync(AdapterType adapterType, Dictionary<string, object> configuration)
        {
            try
            {
                // Worker'lardan birine test isteği gönder
                // veya direkt plugin çağrısı yap - şimdilik mock data dön

                _logger.LogInformation("Test isteği alındı: {AdapterType}", adapterType);

                // Mock test sonucu
                return adapterType switch
                {
                    AdapterType.Rest => new SourceTestResult
                    {
                        IsSuccess = true,
                        Message = "REST API test başarılı",
                        SampleData = new[] { new { id = 1, name = "Test" } },
                        ResponseTime = TimeSpan.FromMilliseconds(123),
                        StatusCode = 200
                    },

                    AdapterType.JsonFile => new SourceTestResult
                    {
                        IsSuccess = true,
                        Message = "JSON test başarılı",
                        SampleData = new[] { new { id = 1, value = "test" } },
                        ResponseTime = TimeSpan.FromMilliseconds(45),
                        StatusCode = 200
                    },

                    AdapterType.Database => new SourceTestResult
                    {
                        IsSuccess = true,
                        Message = "Database test başarılı",
                        SampleData = new[] { new { id = 1, name = "Test" } },
                        ResponseTime = TimeSpan.FromMilliseconds(67),
                        StatusCode = 200
                    },

                    _ => new SourceTestResult
                    {
                        IsSuccess = false,
                        Message = "Desteklenmeyen adapter tipi",
                        StatusCode = 400
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Test hatası");
                return new SourceTestResult
                {
                    IsSuccess = false,
                    Message = ex.Message,
                    StatusCode = 500
                };
            }
        }

        public async Task<DestinationTestResult> TestDestinationAsync(AdapterType adapterType, Dictionary<string, object> configuration)
        {
            try
            {
                _logger.LogInformation("Destination test isteği: {AdapterType}", adapterType);

                return new DestinationTestResult
                {
                    IsSuccess = true,
                    Message = "Destination test başarılı",
                    CanWrite = true,
                    Details = new Dictionary<string, object>
                    {
                        ["provider"] = "mock",
                        ["test_time"] = DateTime.UtcNow
                    }
                };
            }
            catch (Exception ex)
            {
                return new DestinationTestResult
                {
                    IsSuccess = false,
                    Message = ex.Message,
                    Errors = new List<string> { ex.Message }
                };
            }
        }
    }
}
