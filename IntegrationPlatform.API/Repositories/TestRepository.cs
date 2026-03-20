using IntegrationPlatform.API.Data;
using IntegrationPlatform.API.Models;
using IntegrationPlatform.Common.DTOs;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntegrationPlatform.API.Repositories
{
    public class TestRepository : ITestRepository
    {

        private readonly ApplicationDbContext _context;
        private readonly ILogger<NodeRepository> _logger;

        public TestRepository(ILogger<NodeRepository> logger, ApplicationDbContext context)
        {
            _logger = logger;
            _context = context;
        }

        public async Task<List<TestRequestDto>> GetPendingRequests()
        {
            var requests = await _context.Tests
                .Where(t => t.Status == 0).
                Select(b => b.Request)
                .ToListAsync();
            var result = new List<TestRequestDto>();
            foreach (var request in requests)
            {
                var obj = new TestRequestDto
                {
                    Id = Guid.Parse(request["Id"].ToString()),
                    TestType = request["TestType"].ToString(),
                    AdapterType = request["AdapterType"].ToString(),
                    PluginId = request["PluginId"]?.ToString(),
                    TestData = request["TestData"] != null ? (Dictionary<string, object>)request["TestData"] : null,
                    CreatedAt = request["CreatedAt"] != null ? DateTime.Parse(request["CreatedAt"].ToString()) : (DateTime?)null,
                    ProcessedAt = request["ProcessedAt"] != null ? DateTime.Parse(request["ProcessedAt"].ToString()) : (DateTime?)null,
                    Status = request["Status"]?.ToString(),
                    AssignedNodeId = request["AssignedNodeId"] != null ? Guid.Parse(request["AssignedNodeId"].ToString()) : (Guid?)null
                };
                obj.Configuration = request["Configuration"] != null ? JsonSerializer.Deserialize<Dictionary<string, object>>(request["Configuration"].ToString()) : new Dictionary<string, object>();
                result.Add(obj);
            }
            return result;
        }

        public async Task<bool> IsTestingFinish(string requestId)
        {
            var existingEntity = await _context.Tests.FirstOrDefaultAsync(t => t.RequestId == requestId);
            return existingEntity != null && existingEntity.Status == 1;
        }

        public async Task<bool> UpdateRequest(string requestId, TestRequestDto request)
        {
            var existingEntity = await _context.Tests.FirstOrDefaultAsync(t => t.RequestId == requestId);
            if (existingEntity != null)
            {
                existingEntity.Request = new Dictionary<string, object>
                {
                    { "Id", request.Id },
                    { "TestType", request.TestType },
                    { "AdapterType", request.AdapterType },
                    { "PluginId", request.PluginId },
                    { "Configuration", request.Configuration },
                    { "TestData", request.TestData },
                    { "CreatedAt", request.CreatedAt },
                    { "ProcessedAt", request.ProcessedAt },
                    { "Status", request.Status },
                    { "AssignedNodeId", request.AssignedNodeId }
                };
                _context.Tests.Update(existingEntity);
                return await _context.SaveChangesAsync() > 0;
            }
            return false;
        }
        public async Task<bool> AddTestRequest(string requestId, TestRequestDto request)
        {
            var requestEntity = new Test
            {
                RequestId = requestId,
                Request = new Dictionary<string, object>
                {
                    { "Id", request.Id },
                    { "TestType", request.TestType },
                    { "AdapterType", request.AdapterType },
                    { "PluginId", request.PluginId },
                    { "Configuration", request.Configuration },
                    { "TestData", request.TestData },
                    { "CreatedAt", request.CreatedAt },
                    { "ProcessedAt", request.ProcessedAt },
                    { "Status", request.Status },
                    { "AssignedNodeId", request.AssignedNodeId }
                },
                Status = 0
            };
            _context.Tests.Add(requestEntity);
            return await _context.SaveChangesAsync() > 0;
        }

        public async Task<bool> AddTestResponse(string requestId, TestResultDto result)
        {
            var existingEntity = _context.Tests.FirstOrDefault(t => t.RequestId == requestId);
            if (existingEntity == null)
            {
                var responseEntity = new Test
                {
                    RequestId = requestId,
                    Response = new Dictionary<string, object>
                {
                    { "IsSuccess", result.IsSuccess },
                    { "Message", result.Message },
                    { "Result", result.Result },
                    { "StatusCode", result.StatusCode },
                    { "ResponseTimeMs", result.ResponseTimeMs },
                    { "Errors", result.Errors },
                    { "CompletedAt", result.CompletedAt }
                },
                    Status = 1
                };
                _context.Tests.Add(responseEntity);
            }
            else
            {
                existingEntity.Response = new Dictionary<string, object>
                {
                    { "IsSuccess", result.IsSuccess },
                    { "Message", result.Message },
                    { "Result", result.Result },
                    { "StatusCode", result.StatusCode },
                    { "ResponseTimeMs", result.ResponseTimeMs },
                    { "Errors", result.Errors },
                    { "CompletedAt", result.CompletedAt }
                };
                existingEntity.Status = 1;
                _context.Tests.Update(existingEntity);
            }

            return await _context.SaveChangesAsync() > 0;
        }

        public async Task<Dictionary<string, object>> GetTestRequest(string requestId)
        {
            var Entity = _context.Tests.FirstOrDefault(t => t.RequestId == requestId);
            if (Entity == null)
            {
                return null;
            }
            return Entity.Request;
        }

        public async Task<Dictionary<string, object>> GetTestResponse(string requestId)
        {
            var Entity = _context.Tests.FirstOrDefault(t => t.RequestId == requestId);
            if (Entity == null)
            {
                return null;
            }
            return Entity.Response;
        }

        public async Task<bool> UpdateRequestStatus(string requestId, string status)
        {
            var existingEntity = _context.Tests.FirstOrDefault(t => t.RequestId == requestId);
            if (existingEntity != null)
            {
                existingEntity.Status = 1; // Örneğin, 1 tamamlandı anlamına gelebilir
                _context.Tests.Update(existingEntity);
                return _context.SaveChanges() > 0;
            }
            return false;
        }
    }
}
