using IntegrationPlatform.Common.Enums;
using IntegrationPlatform.Common.Helpers;
using IntegrationPlatform.Common.Interfaces;
using IntegrationPlatform.Common.Interfaces.Plugins;
using IntegrationPlatform.Common.Models;
using Microsoft.Extensions.Logging;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IntegrationPlatform.Adapters.Json
{
    public class JsonReaderPlugin : JsonPluginBase, ISourcePlugin
    {
        private readonly ILogger<JsonReaderPlugin>? _logger;
        private JsonSerializerOptions _jsonOptions;

        public override string Id => "IntegrationPlatform.Adapters.Json";
        public override string Name => "JSON Reader";
        public override string Description => "JSON dosyalarını okur, yazar ve dönüştürür. Parse, stringify, mapping işlemleri.";
        public Version Version => new Version(1, 0, 0);
        public string Author => "Integration Platform";
        public override AdapterDirection Direction => AdapterDirection.Source;  // Primary type, ama hepsini yapıyor
        public override AdapterType Type => AdapterType.JsonFile;


        public JsonReaderPlugin()
        {
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
        }
        public JsonReaderPlugin(ILogger<JsonReaderPlugin> logger)
        {
            _logger = logger;
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
        }

        public async Task<bool> InitializeAsync(PluginContext context)
        {
            _logger?.LogDebug("JsonPlugin başlatılıyor...");
            return true;
        }

        #region ISourcePlugin - Json Dosyası Okuma


        public async Task<SourceTestResult> TestConnectionAsync(Dictionary<string, object> configuration)
        {
            try
            {
                var filePath = configuration["FilePath"]?.ToString();

                if (string.IsNullOrEmpty(filePath))
                {
                    return new SourceTestResult
                    {
                        IsSuccess = false,
                        Message = "Dosya yolu belirtilmemiş"
                    };
                }

                if (!File.Exists(filePath))
                {
                    return new SourceTestResult
                    {
                        IsSuccess = false,
                        Message = $"Dosya bulunamadı: {filePath}"
                    };
                }

                // İlk 5 satırı oku
                var lines = await File.ReadAllLinesAsync(filePath);
                var sampleContent = string.Join("\n", lines.Take(10));

                // JSON geçerli mi kontrol et
                try
                {
                    JsonDocument.Parse(sampleContent);
                }
                catch
                {
                    // Belki satır satır JSON olabilir (NDJSON)
                    var isValid = lines.Take(5).All(line =>
                    {
                        try { JsonDocument.Parse(line); return true; }
                        catch { return false; }
                    });

                    if (!isValid)
                    {
                        return new SourceTestResult
                        {
                            IsSuccess = false,
                            Message = "Geçersiz JSON formatı"
                        };
                    }
                }

                var sampleData = ParseJson(sampleContent, configuration).Take(3).ToList();

                return new SourceTestResult
                {
                    IsSuccess = true,
                    Message = $"Dosya okunabilir. {new FileInfo(filePath).Length} bytes, {lines.Length} satır",
                    SampleData = sampleData,
                    DetectedSchema = sampleData.Any() ? SchemaDetector.DetectSchema(sampleData.First()) : null
                };
            }
            catch (Exception ex)
            {
                return new SourceTestResult
                {
                    IsSuccess = false,
                    Message = ex.Message
                };
            }
        }

        public async Task<DataSchema> DiscoverSchemaAsync(Dictionary<string, object> configuration)
        {
            var testResult = await TestConnectionAsync(configuration);

            if (testResult.IsSuccess && testResult.SampleData != null)
            {
                var sample = testResult.SampleData as List<object>;
                if (sample?.Any() == true)
                {
                    return SchemaDetector.DetectSchema(sample.First());
                }
            }

            return new DataSchema { Fields = new List<DataField>() };
        }

        public async Task<SourceData> FetchNextAsync(SourceContext context, string continuationToken)
        {
            // JSON dosyasında sayfalama için - büyük dosyaları parça parça oku
            var filePath = context.Configuration["FilePath"]?.ToString();
            var batchSize = (int)context.Configuration.GetValueOrDefault("BatchSize", 1000);
            var currentLine = int.Parse(continuationToken ?? "0");

            var lines = await File.ReadAllLinesAsync(filePath);
            var batch = lines.Skip(currentLine).Take(batchSize).ToList();

            var data = batch.Select(line => JsonNode.Parse(line)).Cast<object>().ToList();

            var nextToken = currentLine + batch.Count < lines.Length
                ? (currentLine + batch.Count).ToString()
                : null;

            return new SourceData
            {
                Data = data,
                TotalCount = lines.Length,
                ContinuationToken = nextToken,
                Metadata = new Dictionary<string, object>
                {
                    ["batch_start"] = currentLine,
                    ["batch_end"] = currentLine + batch.Count
                }
            };
        }

        public async IAsyncEnumerable<object> FetchStreamAsync(SourceContext context, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var filePath = context.Configuration["FilePath"]?.ToString();

            using var reader = new StreamReader(filePath);
            string line;

            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                if (!string.IsNullOrWhiteSpace(line))
                {
                    yield return JsonNode.Parse(line);
                }
            }
        }

        #endregion


        public async Task<AdapterMetadata> GetMetadataAsync()
        {
            return new AdapterMetadata
            {
                ConfigurationSchema = new Dictionary<string, ParameterDefinition>
                {
                    // Source parametreleri
                    ["FilePath"] = new ParameterDefinition
                    {
                        Name = "FilePath",
                        DisplayName = "Dosya Yolu",
                        Description = "JSON dosyasının tam yolu",
                        ParameterType = typeof(string),
                        IsRequired = true
                    },
                    ["Encoding"] = new ParameterDefinition
                    {
                        Name = "Encoding",
                        DisplayName = "Dosya Encoding",
                        Description = "Dosya karakter kodlaması",
                        ParameterType = typeof(string),
                        IsRequired = false,
                        PossibleValues = new List<string> { "utf-8", "utf-16", "ascii" },
                        DefaultValue = "utf-8"
                    },
                    ["DataPath"] = new ParameterDefinition
                    {
                        Name = "DataPath",
                        DisplayName = "Data Path",
                        Description = "JSON içinde verinin bulunduğu path (örn: data.users)",
                        ParameterType = typeof(string),
                        IsRequired = false
                    },

                    // Transform parametreleri
                    ["Operation"] = new ParameterDefinition
                    {
                        Name = "Operation",
                        DisplayName = "İşlem",
                        Description = "Yapılacak JSON işlemi",
                        ParameterType = typeof(string),
                        IsRequired = false,
                        PossibleValues = new List<string>
                        {
                            "parse", "stringify", "map", "filter", "aggregate",
                            "flatten", "unflatten", "merge", "split"
                        },
                        DefaultValue = "parse"
                    },
                    ["PrettyPrint"] = new ParameterDefinition
                    {
                        Name = "PrettyPrint",
                        DisplayName = "Güzel Yazdır",
                        Description = "JSON'u girintili formatla",
                        ParameterType = typeof(bool),
                        IsRequired = false,
                        DefaultValue = true
                    },
                    ["FilterField"] = new ParameterDefinition
                    {
                        Name = "FilterField",
                        DisplayName = "Filtre Alanı",
                        Description = "Filtrelenecek alan adı",
                        ParameterType = typeof(string),
                        IsRequired = false
                    },
                    ["FilterValue"] = new ParameterDefinition
                    {
                        Name = "FilterValue",
                        DisplayName = "Filtre Değeri",
                        Description = "Filtre değeri",
                        ParameterType = typeof(string),
                        IsRequired = false
                    },
                    ["FilterOperator"] = new ParameterDefinition
                    {
                        Name = "FilterOperator",
                        DisplayName = "Filtre Operatörü",
                        Description = "Filtreleme operatörü",
                        ParameterType = typeof(string),
                        IsRequired = false,
                        PossibleValues = new List<string>
                        { "eq", "neq", "contains", "startswith", "endswith", "gt", "lt" },
                        DefaultValue = "eq"
                    },
                    ["GroupBy"] = new ParameterDefinition
                    {
                        Name = "GroupBy",
                        DisplayName = "Gruplama Alanı",
                        Description = "Gruplama yapılacak alan",
                        ParameterType = typeof(string),
                        IsRequired = false
                    },
                    ["Aggregate"] = new ParameterDefinition
                    {
                        Name = "Aggregate",
                        DisplayName = "Agrega İşlemi",
                        Description = "Gruplama sonrası agrega işlemi",
                        ParameterType = typeof(string),
                        IsRequired = false,
                        PossibleValues = new List<string> { "count", "sum", "avg", "min", "max" }
                    },
                    ["AggregateField"] = new ParameterDefinition
                    {
                        Name = "AggregateField",
                        DisplayName = "Agrega Alanı",
                        Description = "Agrega işlemi yapılacak alan",
                        ParameterType = typeof(string),
                        IsRequired = false
                    },

                    // Destination parametreleri
                    ["BatchSize"] = new ParameterDefinition
                    {
                        Name = "BatchSize",
                        DisplayName = "Batch Boyutu",
                        Description = "Toplu yazma boyutu",
                        ParameterType = typeof(int),
                        IsRequired = false,
                        DefaultValue = 1000
                    }
                },
                SupportedFormats = new List<string> { "JSON", "NDJSON" },
                SupportsBatch = true,
                SupportsStreaming = true,
                MaxBatchSize = 10000,
                OutputTypes = new Dictionary<string, Type>
                {
                    ["default"] = typeof(JsonNode),
                    ["array"] = typeof(JsonArray),
                    ["object"] = typeof(JsonObject)
                },
                InputTypes = new Dictionary<string, Type>
                {
                    ["json_string"] = typeof(string),
                    ["json_node"] = typeof(JsonNode),
                    ["object"] = typeof(object)
                },
                Capabilities = new List<Capability>
                {
                    new Capability { Name = "Parse", Description = "JSON string'i parse eder" },
                    new Capability { Name = "Stringify", Description = "Object'i JSON string'e çevirir" },
                    new Capability { Name = "Map", Description = "Alanları yeniden adlandırır" },
                    new Capability { Name = "Filter", Description = "JSON array'i filtreler" },
                    new Capability { Name = "Aggregate", Description = "Gruplama ve agrega işlemleri" },
                    new Capability { Name = "Read", Description = "Dosyadan JSON okur" },
                    new Capability { Name = "Write", Description = "Dosyaya JSON yazar" },
                    new Capability { Name = "Upsert", Description = "Varolan JSON'ı günceller" }
                }
            };
        }

        public async Task<bool> ShutdownAsync()
        {
            _logger?.LogDebug("JsonPlugin kapatılıyor...");
            return true;
        }

        Task<SourceData> ISourcePlugin.FetchAsync(SourceContext context)
        {
            return FetchAsync(context);
        }
    }
}
