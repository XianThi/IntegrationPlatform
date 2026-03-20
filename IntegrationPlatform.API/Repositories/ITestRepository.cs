using IntegrationPlatform.Common.DTOs;

namespace IntegrationPlatform.API.Repositories
{
    public interface ITestRepository
    {
        Task<bool> AddTestRequest(string requestId, TestRequestDto request);
        Task<bool> AddTestResponse(string requestId, TestResultDto result);
        Task<bool> IsTestingFinish (string requestId);
        Task<bool> UpdateRequestStatus(string requestId, string status);
        Task<bool> UpdateRequest(string requestId, TestRequestDto request);
        Task<List<TestRequestDto>> GetPendingRequests();
        Task<Dictionary<string, object>> GetTestRequest(string requestId);
        Task<Dictionary<string, object>> GetTestResponse(string requestId);
    }
}
