using System.ComponentModel.DataAnnotations.Schema;

namespace IntegrationPlatform.API.Models
{
    public class Test
    {
        public Guid Id { get; set; }
        public string? RequestId { get; set; }
        [Column(TypeName = "jsonb")]
        public Dictionary<string, object>? Request { get; set; }
        [Column(TypeName = "jsonb")]
        public Dictionary<string, object>? Response { get; set; }
        public int Status { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
