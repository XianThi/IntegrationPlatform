using IntegrationPlatform.Common.DTOs;
using IntegrationPlatform.Common.Enums;
using IntegrationPlatform.Common.Interfaces.Plugins;

namespace IntegrationPlatform.API.Services
{
    public interface ITestService
    {
        Task CreateTestRequestAsync(TestRequestDto request);
        Task<TestResultDto> WaitForResultAsync(Guid requestId, TimeSpan timeout);
        Task<List<TestRequestDto>> GetPendingRequestsAsync(Guid nodeId);
        Task UpdateRequestStatusAsync(Guid requestId, string status);
        Task SubmitResultAsync(TestResultDto result);
    }
}
