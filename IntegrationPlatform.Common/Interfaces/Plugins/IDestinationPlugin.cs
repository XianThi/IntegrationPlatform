namespace IntegrationPlatform.Common.Interfaces.Plugins
{
    public interface IDestinationPlugin : IPlugin
    {
        // Tekli kayıt yazma
        Task<WriteResult> WriteAsync(object data, DestinationContext context);

        // Toplu yazma
        Task<WriteResult> WriteBatchAsync(List<object> data, DestinationContext context);

        // Streaming yazma
        Task<WriteResult> WriteStreamAsync(IAsyncEnumerable<object> dataStream, DestinationContext context);

        // Hedef şema oluşturma
        Task<bool> CreateTargetSchemaAsync(DataSchema schema, DestinationContext context);

        // Test bağlantısı
        Task<DestinationTestResult> TestDestinationAsync(Dictionary<string, object> configuration);

        // Upsert/Update desteği
        Task<WriteResult> UpsertAsync(object data, DestinationContext context, string keyField);
    }

    public class DestinationContext
    {
        public Dictionary<string, object> Configuration { get; set; }
        public WriteMode WriteMode { get; set; } // Append, Overwrite, Merge
        public int BatchSize { get; set; } = 1000;
        public bool CreateIfNotExists { get; set; } = true;
        public Dictionary<string, string> FieldMappings { get; set; }
        public List<string> KeyFields { get; set; } // Upsert için
        public TimeSpan? Timeout { get; set; }
        public bool UseTransaction { get; set; } = false;
        public CancellationToken CancellationToken { get; set; }
    }

    public enum WriteMode
    {
        Append = 1,      // Ekle
        Overwrite = 2,   // Sil ve ekle
        Merge = 3,       // Varolanı güncelle, yoksa ekle
        Truncate = 4     // Tabloyu boşalt ve ekle
    }

    public class WriteResult
    {
        public bool IsSuccess { get; set; }
        public long RecordsWritten { get; set; }
        public long RecordsFailed { get; set; }
        public List<WriteError> Errors { get; set; }
        public TimeSpan ElapsedTime { get; set; }
        public Dictionary<string, object> TargetMetadata { get; set; }
    }

    public class WriteError
    {
        public int RecordIndex { get; set; }
        public object Record { get; set; }
        public string ErrorMessage { get; set; }
        public string ErrorCode { get; set; }
    }

    public class DestinationTestContext
    {
        public Dictionary<string, object> Configuration { get; set; }
        public TestLevel TestLevel { get; set; } // Basic, Full, Performance
        public CancellationToken CancellationToken { get; set; }
    }

    public enum TestLevel
    {
        Basic = 1,      // Sadece bağlantı
        Full = 2,       // Yazma testi de yap
        Performance = 3 // Performans testi
    }
}
