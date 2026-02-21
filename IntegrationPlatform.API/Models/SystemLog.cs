using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IntegrationPlatform.API.Models
{
    [Table("SystemLogs")]
    public class SystemLog
    {
        [Key]
        public long Id { get; set; }

        public DateTime Timestamp { get; set; }

        public string Level { get; set; } // Info, Warning, Error

        public string Source { get; set; } // Worker, API, Dashboard

        public Guid? NodeId { get; set; }

        public Guid? WorkflowId { get; set; }

        public string Message { get; set; }

        [Column(TypeName = "jsonb")]
        public Dictionary<string, object> Properties { get; set; }

        public string Exception { get; set; }

        // Index için
        public string MachineName { get; set; }
    }
}
