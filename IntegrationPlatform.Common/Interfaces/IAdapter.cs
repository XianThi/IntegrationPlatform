using IntegrationPlatform.Common.Enums;

namespace IntegrationPlatform.Common.Interfaces
{
    public interface IAdapter
    {
        string Id { get; }
        string Name { get; }
        string Description { get; }
        AdapterType Type { get; }
        AdapterDirection Direction { get; }
        Version Version { get; }

        // Adapter'ın ihtiyaç duyduğu konfigürasyon şeması
        Dictionary<string, ParameterDefinition> GetConfigurationSchema();

        // Test modu için veri çekme/ gönderme
        Task<AdapterTestResult> TestAsync(Dictionary<string, object> configuration, CancellationToken cancellationToken = default);

        // Normal çalışma modu
        Task<AdapterExecutionResult> ExecuteAsync(AdapterContext context, CancellationToken cancellationToken = default);
    }

    public class ParameterDefinition
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public Type ParameterType { get; set; }
        public bool IsRequired { get; set; }
        public object DefaultValue { get; set; }
        public List<string> PossibleValues { get; set; } // Enum veya seçenekli değerler için
        public string ValidationRegex { get; set; }
        public bool IsSecret { get; set; } // Şifre gibi alanlar için
        public bool IsMultiline { get; set; } // Çok satırlı metin alanları için
    }

    public class AdapterContext
    {
        public Guid WorkflowId { get; set; }
        public Guid StepId { get; set; }
        public Dictionary<string, object> Configuration { get; set; }
        public Dictionary<string, object> InputData { get; set; }
        public Dictionary<string, object> GlobalVariables { get; set; }
        public CancellationToken CancellationToken { get; set; }
    }

    public class AdapterExecutionResult
    {
        public bool IsSuccess { get; set; }
        public string ErrorMessage { get; set; }
        public object Data { get; set; }
        public Dictionary<string, object> Metadata { get; set; }
        public long ProcessingTimeMs { get; set; }
        public long ProcessedRecordCount { get; set; }
    }

    public class AdapterTestResult
    {
        public bool IsSuccess { get; set; }
        public string ErrorMessage { get; set; }
        public object PreviewData { get; set; } // Test sonucu gösterilecek veri
        public Dictionary<string, object> DetectedFields { get; set; } // Otomatik tespit edilen alanlar
        public TimeSpan ResponseTime { get; set; }
        public int StatusCode { get; set; }
    }
}
