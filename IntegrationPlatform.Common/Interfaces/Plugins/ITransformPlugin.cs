namespace IntegrationPlatform.Common.Interfaces.Plugins
{
    public interface ITransformPlugin : IPlugin
    {
        // Tekli kayıt dönüşümü
        Task<object> TransformAsync(object input, TransformContext context);

        // Toplu dönüşüm
        Task<List<object>> TransformBatchAsync(List<object> inputs, TransformContext context);

        // Şema dönüşümü
        Task<DataSchema> TransformSchemaAsync(DataSchema inputSchema, TransformContext context);

        // Validasyon
        Task<ValidationResult> ValidateAsync(object input, TransformContext context);

        // Map tanımları
        Task<Dictionary<string, string>> GetMappingSchemaAsync();
    }

    public class TransformContext
    {
        public Dictionary<string, object> Configuration { get; set; }
        public Dictionary<string, string> Mapping { get; set; } // Kaynak -> Hedef alan eşleme
        public Dictionary<string, object> GlobalVariables { get; set; }
        public DataSchema SourceSchema { get; set; }
        public DataSchema TargetSchema { get; set; }
        public List<TransformationRule> Rules { get; set; }
    }

    public class TransformationRule
    {
        public string Name { get; set; }
        public string SourceField { get; set; }
        public string TargetField { get; set; }
        public string TransformationType { get; set; } // Map, Convert, Script, etc.
        public string Script { get; set; } // JavaScript/C# script
        public Dictionary<string, object> Parameters { get; set; }
    }

    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; }
        public List<string> Warnings { get; set; }
        public Dictionary<string, object> ValidatedData { get; set; }
    }
}
