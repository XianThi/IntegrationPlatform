using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IntegrationPlatform.Common.DTOs
{
    public class TestRequestDto
    {
        public Guid Id { get; set; }
        public string? TestType { get; set; } // "Source", "Destination"
        public string AdapterType { get; set; }
        public string? PluginId { get; set; }
        public Dictionary<string, object> Configuration { get; set; }
        public Dictionary<string, object>? TestData { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? ProcessedAt { get; set; }
        public string? Status { get; set; } // "Pending", "Processing", "Completed", "Failed"
        public Guid? AssignedNodeId { get; set; }
    }
}
