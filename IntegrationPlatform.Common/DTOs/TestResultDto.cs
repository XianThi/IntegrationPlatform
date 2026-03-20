using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IntegrationPlatform.Common.DTOs
{
    public class TestResultDto
    {
        public Guid RequestId { get; set; }
        public bool IsSuccess { get; set; }
        public string Message { get; set; }
        public object Result { get; set; }
        public int? StatusCode { get; set; }
        public double ResponseTimeMs { get; set; }
        public List<string> Errors { get; set; }
        public DateTime CompletedAt { get; set; }
    }
}
