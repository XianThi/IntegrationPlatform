using IntegrationPlatform.Common.Enums;
using IntegrationPlatform.Common.Helpers;
using IntegrationPlatform.Common.Interfaces;
using IntegrationPlatform.Common.Interfaces.Plugins;
using IntegrationPlatform.Common.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace IntegrationPlatform.Adapters.Json
{
    public abstract class JsonPluginBase
    {
        protected readonly ILogger? _logger;
        protected JsonSerializerOptions _jsonOptions;

        public abstract string Id { get; }
        public abstract string Name { get; }
        public abstract string Description { get; }
        public abstract AdapterDirection Direction { get; }
        public abstract AdapterType Type { get; }

        public Version Version => new Version(1, 0, 0);
        public string Author => "Integration Platform";

        protected JsonPluginBase()
        {
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
        }

        protected JsonPluginBase(ILogger logger) : this()
        {
            _logger = logger;
        }

        // Ortak yardımcı metodlar
        protected List<object> ParseJson(string content, Dictionary<string, object> configuration)
        {
            var jsonNode = JsonNode.Parse(content);

            // Root array mi?
            if (jsonNode is JsonArray jsonArray)
            {
                return jsonArray.Select(item => item?.AsObject()).Cast<object>().ToList();
            }

            // Root object ve içinde data alanı var mı?
            var dataPath = (string)configuration.GetValueOrDefault("DataPath", "");
            if (!string.IsNullOrEmpty(dataPath) && jsonNode is JsonObject jsonObject)
            {
                var current = jsonObject;
                foreach (var part in dataPath.Split('.'))
                {
                    if (current.ContainsKey(part))
                        current = current[part]?.AsObject();
                    else
                        break;
                }

                if (current != null && current.Root is JsonArray array)
                {
                    return array.Select(item => item?.AsObject()).Cast<object>().ToList();
                }
            }

            // Tek obje dönmüşse liste içinde döndür
            return new List<object> { jsonNode };
        }
        protected bool IsTypeCompatible(string value, DataType type)
        {
            if (string.IsNullOrEmpty(value)) return true;

            return type switch
            {
                DataType.Integer => int.TryParse(value, out _),
                DataType.Decimal => decimal.TryParse(value, out _),
                DataType.Boolean => bool.TryParse(value, out _),
                DataType.DateTime => DateTime.TryParse(value, out _),
                _ => true
            };
        }
        protected async Task<SourceData> FetchAsync(SourceContext context)
        {
            _logger?.LogDebug("JSON dosyası okunuyor...");

            try
            {
                var filePath = context.Configuration["FilePath"]?.ToString();
                var encoding = context.Configuration.GetValueOrDefault("Encoding", "utf-8")?.ToString();

                if (string.IsNullOrEmpty(filePath))
                    throw new ArgumentException("FilePath parametresi zorunludur");

                if (!File.Exists(filePath))
                    throw new FileNotFoundException($"Dosya bulunamadı: {filePath}");

                var stopwatch = Stopwatch.StartNew();

                // Dosyayı oku
                var content = await File.ReadAllTextAsync(filePath, Encoding.GetEncoding(encoding));

                // JSON parse et
                var data = ParseJson(content, context.Configuration);

                stopwatch.Stop();

                return new SourceData
                {
                    Data = data,
                    TotalCount = data?.Count ?? 0,
                    Metadata = new Dictionary<string, object>
                    {
                        ["file_path"] = filePath,
                        ["file_size"] = new FileInfo(filePath).Length,
                        ["encoding"] = encoding,
                        ["elapsed_ms"] = stopwatch.ElapsedMilliseconds
                    },
                    Schema = data?.Any() == true ? SchemaDetector.DetectSchema(data.First()) : null,
                    ElapsedTime = stopwatch.Elapsed
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "JSON dosyası okunurken hata");
                throw;
            }
        }

        public virtual async Task<bool> InitializeAsync(PluginContext context)
        {
            _logger?.LogDebug("{PluginName} başlatılıyor...", Name);
            return true;
        }

        public virtual async Task<bool> ShutdownAsync()
        {
            _logger?.LogDebug("{PluginName} kapatılıyor...", Name);
            return true;
        }

        public virtual async Task<AdapterMetadata> GetMetadataAsync()
        {
            return new AdapterMetadata
            {
                ConfigurationSchema = GetConfigurationSchema(),
                SupportedFormats = new List<string> { "JSON", "NDJSON" },
                SupportsBatch = true,
                SupportsStreaming = true,
                MaxBatchSize = 10000
            };
        }

        protected virtual Dictionary<string, ParameterDefinition> GetConfigurationSchema()
        {
            return new Dictionary<string, ParameterDefinition>();
        }
    }
}
