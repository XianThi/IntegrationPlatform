using IntegrationPlatform.API.Services;
using IntegrationPlatform.Common.DTOs;
using IntegrationPlatform.Common.Enums;
using IntegrationPlatform.Common.Interfaces.Plugins;
using IntegrationPlatform.Common.Models;
using Microsoft.AspNetCore.Mvc;

namespace IntegrationPlatform.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TestController : ControllerBase
    {
        private readonly ITestService _testService;
        private readonly ILogger<TestController> _logger;

        public TestController(ITestService testService, ILogger<TestController> logger)
        {
            _testService = testService;
            _logger = logger;
        }

        [HttpGet("pending")]
        public async Task<IActionResult> Status(string nodeId)
        {
            List<TestRequestDto> pendingRequests = await _testService.GetPendingRequestsAsync(Guid.Parse(nodeId));
            return Ok(pendingRequests);
        }
        [HttpPost("result")]
        public async Task<IActionResult> Result([FromBody] TestResultDto result)
        {
            try
            {
                await _testService.SubmitResultAsync(result);
                return Ok(new { Message = "Sonuç kaydedildi" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sonuç kaydetme hatası");
                return StatusCode(500, new { Message = ex.Message });
            }
        }
        [HttpPut("{requestId}/status")]
        public async Task<IActionResult> UpdateStatus(string requestId, [FromBody]object update)
        {
            try
            {
                _logger.LogInformation("Durum güncelleme isteği alındı: {RequestId} - payload : {update}", requestId, update);
                await _testService.UpdateRequestStatusAsync(Guid.Parse(requestId), "Completed");
                return Ok(new { Message = "Durum güncellendi" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Durum güncelleme hatası");
                return StatusCode(500, new { Message = ex.Message });
            }
        }

        [HttpPost("source")]
        public async Task<ActionResult<SourceTestResult>> TestSource([FromBody] TestSourceRequest request)
        {
            try
            {
                var testRequest = new TestRequestDto
                {
                    Id = Guid.NewGuid(),
                    TestType = "Source",
                    AdapterType = request.AdapterType.ToString(),
                    PluginId = request.PluginId,
                    Configuration = request.Configuration,
                    CreatedAt = DateTime.UtcNow,
                    Status = "Pending"
                };

                // Test isteğini kaydet (database veya memory cache)
                await _testService.CreateTestRequestAsync(testRequest);

                // Sonuç için bekle (timeout ile)
                var timeout = TimeSpan.FromSeconds(30);
                var result = await _testService.WaitForResultAsync((Guid)testRequest.Id, timeout);

                if (result != null)
                {
                    return Ok(result);
                }

                return StatusCode(408, new { Message = "Test timeout" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Test source hatası");
                return StatusCode(500, new { Message = ex.Message });
            }
        }
        [HttpPost("destination")]
        public async Task<ActionResult<DestinationTestResult>> TestDestination([FromBody] TestDestinationRequest request)
        {
            try
            {
                var testRequest = new TestRequestDto
                {
                    Id = Guid.NewGuid(),
                    TestType = "Destination",
                    AdapterType = request.AdapterType.ToString(),
                    PluginId = request.PluginId,
                    Configuration = request.Configuration,
                    CreatedAt = DateTime.UtcNow,
                    Status = "Pending"
                };
                // Test isteğini kaydet (database veya memory cache)
                await _testService.CreateTestRequestAsync(testRequest);

                // Sonuç için bekle (timeout ile)
                var timeout = TimeSpan.FromSeconds(30);
                var result = await _testService.WaitForResultAsync((Guid)testRequest.Id, timeout);

                if (result != null)
                {
                    return Ok(result);
                }

                return StatusCode(408, new { Message = "Test timeout" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Test source hatası");
                return StatusCode(500, new { Message = ex.Message });
            }
        }
    }

}
