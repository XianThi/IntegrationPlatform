namespace IntegrationPlatform.Common.Interfaces.Plugins
{
    public interface ISourcePlugin : IPlugin
    {
        // Veri çekme metodları
        Task<SourceData> FetchAsync(SourceContext context);

        // Test modu için
        Task<SourceTestResult> TestConnectionAsync(Dictionary<string, object> configuration);

        // Şema keşfi için
        Task<DataSchema> DiscoverSchemaAsync(Dictionary<string, object> configuration);

        // Sayfalama/İlerleme desteği
        Task<SourceData> FetchNextAsync(SourceContext context, string continuationToken);

        // Streaming desteği
        IAsyncEnumerable<object> FetchStreamAsync(SourceContext context, CancellationToken cancellationToken);
    }

    public class SourceContext
    {
        public Dictionary<string, object> Configuration { get; set; }
        public Dictionary<string, object> Parameters { get; set; } // Runtime parametreler
        public string ContinuationToken { get; set; } // Sayfalama için
        public int? Limit { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public Dictionary<string, string> Filters { get; set; }
        public CancellationToken CancellationToken { get; set; }
    }

    public class SourceData
    {
        public List<object> Data { get; set; }
        public long TotalCount { get; set; }
        public string ContinuationToken { get; set; } // Sonraki sayfa için
        public Dictionary<string, object> Metadata { get; set; }
        public DataSchema Schema { get; set; }
        public TimeSpan ElapsedTime { get; set; }
    }

    public class SourceTestResult
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; }
        public object SampleData { get; set; }
        public Dictionary<string, object> Metadata { get; set; }
        public DataSchema DetectedSchema { get; set; }
        public Dictionary<string, object> Statistics { get; set; }
        public TimeSpan ResponseTime { get; set; }
        public int StatusCode { get; set; }
        public Dictionary<string, string> Headers { get; set; }
    }
}
