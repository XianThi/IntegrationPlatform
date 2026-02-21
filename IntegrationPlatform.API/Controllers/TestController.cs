using IntegrationPlatform.API.Services;
using IntegrationPlatform.Common.Enums;
using IntegrationPlatform.Common.Interfaces.Plugins;
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

        [HttpPost("source")]
        public async Task<ActionResult<SourceTestResult>> TestSource([FromBody] TestSourceRequest request)
        {
            try
            {
                var result = await _testService.TestSourceAsync(request.AdapterType, request.Configuration);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Test hatası");
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("destination")]
        public async Task<ActionResult<DestinationTestResult>> TestDestination([FromBody] TestDestinationRequest request)
        {
            try
            {
                var result = await _testService.TestDestinationAsync(request.AdapterType, request.Configuration);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Test hatası");
                return BadRequest(new { error = ex.Message });
            }
        }
    }

    public class TestSourceRequest
    {
        public AdapterType AdapterType { get; set; }
        public Dictionary<string, object> Configuration { get; set; }
    }

    public class TestDestinationRequest
    {
        public AdapterType AdapterType { get; set; }
        public Dictionary<string, object> Configuration { get; set; }
    }
}
