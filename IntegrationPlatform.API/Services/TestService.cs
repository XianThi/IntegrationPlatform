using IntegrationPlatform.API.Controllers;
using IntegrationPlatform.API.Repositories;
using IntegrationPlatform.Common.DTOs;
using IntegrationPlatform.Common.Enums;
using IntegrationPlatform.Common.Interfaces;
using IntegrationPlatform.Common.Interfaces.Plugins;
using IntegrationPlatform.Common.Models;
using System.Collections.Concurrent;
using System.Text.Json;

namespace IntegrationPlatform.API.Services
{
    public class TestService : ITestService
    {
        private readonly ILogger<TestService> _logger;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly ITestRepository _testRepository;

        public TestService(ILogger<TestService> logger, ITestRepository testRepository)
        {
            _logger = logger;
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            _testRepository = testRepository;
        }

        public async Task CreateTestRequestAsync(TestRequestDto request)
        {
            if (request.Id == null || request.Id == Guid.Empty)
            {
                request.Id = Guid.NewGuid();
            }
            await _testRepository.AddTestRequest(request.Id.ToString(), request);
            _logger.LogInformation("Test request oluşturuldu: {RequestId}", request.Id);
            await Task.CompletedTask;
        }

        public async Task<TestResultDto> WaitForResultAsync(Guid requestId, TimeSpan timeout)
        {
            var startTime = DateTime.UtcNow;

            while (DateTime.UtcNow - startTime < timeout)
            {
                if (await _testRepository.IsTestingFinish(requestId.ToString()))
                {
                    var result = await _testRepository.GetTestResponse(requestId.ToString());
                    if (result != null)
                    {
                        _logger.LogInformation("Test sonucu bulundu: {RequestId}, Success: {IsSuccess}",
                            requestId, true);
                        return new TestResultDto
                        {
                            RequestId = requestId,
                            IsSuccess = true,
                            Message = "Test completed successfully",
                            Result = result,
                            CompletedAt = DateTime.UtcNow
                        };
                    }
                }

                await Task.Delay(100);
            }

            return null;
        }

        public async Task<List<TestRequestDto>> GetPendingRequestsAsync(Guid nodeId)
        {
            // Sadece bu node'a atanmış veya atanmamış request'leri getir
            var requests = await _testRepository.GetPendingRequests();

            // İlk bulunan request'i bu node'a ata
            if (requests.Any())
            {
                var chosenRequest = requests.FirstOrDefault();
                if (chosenRequest != null)
                {
                    chosenRequest.AssignedNodeId = nodeId;
                    chosenRequest.Status = "Processing";
                    await _testRepository.UpdateRequest(chosenRequest.Id.ToString(), chosenRequest);
                }
                else
                {
                    _logger.LogInformation("Bu node için atanacak bekleyen test isteği bulunamadı: {NodeId}", nodeId);
                    return null;
                }
            }

            return requests;
        }

        public async Task UpdateRequestStatusAsync(Guid requestId, string status)
        {
            await _testRepository.UpdateRequestStatus(requestId.ToString(), status);
            await Task.CompletedTask;
        }

        public async Task SubmitResultAsync(TestResultDto result)
        {
            _logger.LogInformation("Test result alındı: {RequestId}, Success: {IsSuccess}",
                result.RequestId, result.IsSuccess);

            await _testRepository.AddTestResponse(result.RequestId.ToString(), result);
            await Task.CompletedTask;
        }
    }
}
