using IntegrationPlatform.Common.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IntegrationPlatform.Common.Models
{
    public class TestSourceRequest
    {
        public string? PluginId { get; set; }
        public AdapterType AdapterType { get; set; }
        public Dictionary<string, object> Configuration { get; set; }
        public string? RequestId { get; set; } = Guid.NewGuid().ToString();
        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    }

    public class TestDestinationRequest
    {
        public string? PluginId { get; set; }
        public AdapterType AdapterType { get; set; }
        public Dictionary<string, object> Configuration { get; set; }
        public Dictionary<string, object> TestData { get; set; } // Test için gönderilecek data
        public string? RequestId { get; set; } = Guid.NewGuid().ToString();
        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    }

    public class TestResponse
    {
        public string RequestId { get; set; }
        public bool IsSuccess { get; set; }
        public string Message { get; set; }
        public object Result { get; set; }
        public TimeSpan ProcessingTime { get; set; }
        public DateTime CompletedAt { get; set; }
        public List<string> Errors { get; set; } = new();
    }
}
