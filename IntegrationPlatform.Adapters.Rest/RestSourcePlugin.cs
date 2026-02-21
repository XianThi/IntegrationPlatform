using IntegrationPlatform.Common.Enums;
using IntegrationPlatform.Common.Helpers;
using IntegrationPlatform.Common.Interfaces;
using IntegrationPlatform.Common.Interfaces.Plugins;
using IntegrationPlatform.Common.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace IntegrationPlatform.Adapters.Rest
{
    public class RestSourcePlugin : ISourcePlugin
    {
        private readonly ILogger<RestSourcePlugin>? _logger;
        private HttpClient _httpClient;
        private JsonSerializerOptions _jsonOptions;

        public string Id => "IntegrationPlatform.Adapters.Rest";
        public string Name => "REST API Source";
        public string Description => "REST API'lerden veri çeker. GET, POST, PUT, DELETE metodlarını destekler.";
        public Version Version => new Version(1, 0, 0);
        public string Author => "ETL System";
        public AdapterDirection Direction => AdapterDirection.Source;
        public AdapterType Type => AdapterType.Rest;

        
        // 1️⃣ PARAMETRESİZ CONSTRUCTOR (ZORUNLU!)
        public RestSourcePlugin()
        {
            // Logger null olabilir, sonra Initialize'da set edeceğiz
            _httpClient = new HttpClient();
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
        }
        public RestSourcePlugin(ILogger<RestSourcePlugin> logger):this()
        {
            _logger = logger;
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
        }
        public async Task<bool> InitializeAsync(PluginContext context)
        {
            _logger?.LogDebug("RestSourcePlugin başlatılıyor...");

            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));

            return true;
        }

        public async Task<SourceData> FetchAsync(SourceContext context)
        {
            _logger?.LogDebug("REST API'den veri çekiliyor...");

            try
            {
                // Konfigürasyonu al
                var url = context.Configuration["Url"]?.ToString();
                var method = context.Configuration.GetValueOrDefault("Method", "GET")?.ToString() ?? "GET";
                var headers = (Dictionary<string, string>)context.Configuration.GetValueOrDefault("Headers", new Dictionary<string, string>());
                var body = context.Configuration.GetValueOrDefault("Body", "")?.ToString();
                var timeout = context.Configuration.GetValueOrDefault("Timeout", 30);

                if (string.IsNullOrEmpty(url))
                    throw new ArgumentException("URL parametresi zorunludur");

                // HTTP isteğini hazırla
                using var request = new HttpRequestMessage(new HttpMethod(method), url);

                // Headers ekle
                foreach (var header in headers)
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }

                // Authentication
                if (context.Configuration.ContainsKey("ApiKey"))
                {
                    var apiKey = context.Configuration["ApiKey"]?.ToString();
                    var apiKeyHeader = (string)context.Configuration.GetValueOrDefault("ApiKeyHeader", "X-API-Key");
                    request.Headers.TryAddWithoutValidation(apiKeyHeader, apiKey);
                }

                if (context.Configuration.ContainsKey("BearerToken"))
                {
                    var token = context.Configuration["BearerToken"]?.ToString();
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }

                // Body ekle (POST, PUT için)
                if ((method == "POST" || method == "PUT") && !string.IsNullOrEmpty(body))
                {
                    request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                }

                // İsteği gönder
                var stopwatch = Stopwatch.StartNew();
                var response = await _httpClient.SendAsync(request);
                stopwatch.Stop();

                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var data = ParseResponse(content, context.Configuration);

                // Metadata oluştur
                var metadata = new Dictionary<string, object>
                {
                    ["status_code"] = (int)response.StatusCode,
                    ["content_type"] = response.Content.Headers.ContentType?.MediaType,
                    ["content_length"] = response.Content.Headers.ContentLength,
                    ["elapsed_ms"] = stopwatch.ElapsedMilliseconds
                };

                // Headers'ları metadata'ya ekle
                foreach (var header in response.Headers)
                {
                    metadata[$"response_header_{header.Key}"] = string.Join(", ", header.Value);
                }

                return new SourceData
                {
                    Data = data,
                    TotalCount = data?.Count ?? 0,
                    Metadata = metadata,
                    Schema = data?.Any() == true ? SchemaDetector.DetectSchema(data.First()) : null,
                    ElapsedTime = stopwatch.Elapsed
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "REST API çağrısı başarısız: {Url}", context.Configuration["Url"]);
                throw;
            }
        }

        private List<object> ParseResponse(string content, Dictionary<string, object> configuration)
        {
            var responseType = configuration.GetValueOrDefault("ResponseType", "json")?.ToString();

            return responseType?.ToLower() switch
            {
                "json" => ParseJsonResponse(content, configuration),
                "xml" => ParseXmlResponse(content, configuration),
                "csv" => ParseCsvResponse(content, configuration),
                _ => ParseJsonResponse(content, configuration)
            };
        }

        private List<object> ParseJsonResponse(string content, Dictionary<string, object> configuration)
        {
            try
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                // Root element array mi?
                if (root.ValueKind == JsonValueKind.Array)
                {
                    return root.EnumerateArray()
                        .Select(item => item.Clone())
                        .Cast<object>()
                        .ToList();
                }

                // Root element object ve içinde data alanı var mı?
                var dataPath = (string)configuration.GetValueOrDefault("DataPath", "");
                if (!string.IsNullOrEmpty(dataPath))
                {
                    var current = root;
                    foreach (var part in dataPath.Split('.'))
                    {
                        if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(part, out var next))
                            current = next;
                        else
                            break;
                    }

                    if (current.ValueKind == JsonValueKind.Array)
                    {
                        return current.EnumerateArray()
                            .Select(item => item.Clone())
                            .Cast<object>()
                            .ToList();
                    }
                }

                // Tek bir obje dönmüşse liste içinde döndür
                return new List<object> { root.Clone() };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "JSON parse hatası");
                return new List<object>();
            }
        }

        private List<object> ParseXmlResponse(string content, Dictionary<string, object> configuration)
        {
            // XML parsing implementasyonu
            _logger?.LogWarning("XML parsing henüz implemente edilmedi");
            return new List<object>();
        }

        private List<object> ParseCsvResponse(string content, Dictionary<string, object> configuration)
        {
            // CSV parsing implementasyonu
            _logger?.LogWarning("CSV parsing henüz implemente edilmedi");
            return new List<object>();
        }

        public async Task<SourceTestResult> TestConnectionAsync(Dictionary<string, object> configuration)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var url = configuration["Url"]?.ToString();
                var method = configuration.GetValueOrDefault("Method", "GET")?.ToString() ?? "GET";

                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(10);

                using var request = new HttpRequestMessage(new HttpMethod(method), url);

                // Headers ekle
                if (configuration.ContainsKey("ApiKey"))
                {
                    var apiKey = configuration["ApiKey"]?.ToString();
                    request.Headers.TryAddWithoutValidation("X-API-Key", apiKey);
                }

                var response = await client.SendAsync(request);
                stopwatch.Stop();

                var content = await response.Content.ReadAsStringAsync();
                var sampleData = ParseResponse(content, configuration).Take(5).ToList();

                // Şema çıkar
                var schema = sampleData.Any()
                    ? SchemaDetector.DetectSchema(sampleData.First())
                    : null;

                return new SourceTestResult
                {
                    IsSuccess = response.IsSuccessStatusCode,
                    Message = response.IsSuccessStatusCode
                        ? $"Bağlantı başarılı. {sampleData.Count} örnek kayıt alındı."
                        : $"Hata: {response.StatusCode} - {response.ReasonPhrase}",
                    SampleData = sampleData,
                    DetectedSchema = schema,
                    ResponseTime = stopwatch.Elapsed,
                    StatusCode = (int)response.StatusCode,
                    Headers = response.Headers.ToDictionary(
                        h => h.Key,
                        h => string.Join(", ", h.Value))
                };
            }
            catch (TaskCanceledException)
            {
                stopwatch.Stop();
                return new SourceTestResult
                {
                    IsSuccess = false,
                    Message = "İstek zaman aşımına uğradı (timeout)",
                    ResponseTime = stopwatch.Elapsed,
                    StatusCode = 408
                };
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                return new SourceTestResult
                {
                    IsSuccess = false,
                    Message = ex.Message,
                    ResponseTime = stopwatch.Elapsed,
                    StatusCode = 500
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

            return new DataSchema
            {
                Name = "Unknown",
                Fields = new List<DataField>()
            };
        }

        public async Task<SourceData> FetchNextAsync(SourceContext context, string continuationToken)
        {
            // Sayfalama için
            if (!string.IsNullOrEmpty(continuationToken))
            {
                // URL'e sayfa parametresi ekle
                var url = context.Configuration["Url"]?.ToString();
                var pageParam = context.Configuration.GetValueOrDefault("PageParameter", "page");

                var separator = url.Contains('?') ? '&' : '?';
                var pagedUrl = $"{url}{separator}{pageParam}={continuationToken}";

                context.Configuration["Url"] = pagedUrl;
                return await FetchAsync(context);
            }

            return await FetchAsync(context);
        }

        public async IAsyncEnumerable<object> FetchStreamAsync(SourceContext context, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            // Streaming için - büyük verilerde parça parça okuma
            var response = await FetchAsync(context);

            foreach (var item in response.Data)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                yield return item;
            }
        }

        public async Task<AdapterMetadata> GetMetadataAsync()
        {
            return new AdapterMetadata
            {
                ConfigurationSchema = new Dictionary<string, ParameterDefinition>
                {
                    ["Url"] = new ParameterDefinition
                    {
                        Name = "Url",
                        DisplayName = "API URL",
                        Description = "REST API endpoint URL (örnek: https://api.example.com/users)",
                        ParameterType = typeof(string),
                        IsRequired = true,
                        ValidationRegex = @"^https?://.+"
                    },
                    ["Method"] = new ParameterDefinition
                    {
                        Name = "Method",
                        DisplayName = "HTTP Method",
                        Description = "HTTP metodu",
                        ParameterType = typeof(string),
                        IsRequired = true,
                        PossibleValues = new List<string> { "GET", "POST", "PUT", "DELETE", "PATCH" },
                        DefaultValue = "GET"
                    },
                    ["Headers"] = new ParameterDefinition
                    {
                        Name = "Headers",
                        DisplayName = "HTTP Headers",
                        Description = "İstek başlıkları (key-value çiftleri)",
                        ParameterType = typeof(Dictionary<string, string>),
                        IsRequired = false
                    },
                    ["ApiKey"] = new ParameterDefinition
                    {
                        Name = "ApiKey",
                        DisplayName = "API Key",
                        Description = "API Key authentication",
                        ParameterType = typeof(string),
                        IsRequired = false,
                        IsSecret = true
                    },
                    ["BearerToken"] = new ParameterDefinition
                    {
                        Name = "BearerToken",
                        DisplayName = "Bearer Token",
                        Description = "JWT Bearer token",
                        ParameterType = typeof(string),
                        IsRequired = false,
                        IsSecret = true
                    },
                    ["Body"] = new ParameterDefinition
                    {
                        Name = "Body",
                        DisplayName = "Request Body",
                        Description = "POST/PUT istekleri için JSON body",
                        ParameterType = typeof(string),
                        IsRequired = false
                    },
                    ["ResponseType"] = new ParameterDefinition
                    {
                        Name = "ResponseType",
                        DisplayName = "Response Type",
                        Description = "Yanıt formatı",
                        ParameterType = typeof(string),
                        IsRequired = false,
                        PossibleValues = new List<string> { "json", "xml", "csv" },
                        DefaultValue = "json"
                    },
                    ["DataPath"] = new ParameterDefinition
                    {
                        Name = "DataPath",
                        DisplayName = "Data Path",
                        Description = "JSON içinde verinin bulunduğu path (örn: data.items)",
                        ParameterType = typeof(string),
                        IsRequired = false
                    },
                    ["Timeout"] = new ParameterDefinition
                    {
                        Name = "Timeout",
                        DisplayName = "Timeout (saniye)",
                        Description = "İstek zaman aşımı süresi",
                        ParameterType = typeof(int),
                        IsRequired = false,
                        DefaultValue = 30
                    },
                    ["PageParameter"] = new ParameterDefinition
                    {
                        Name = "PageParameter",
                        DisplayName = "Sayfa Parametresi",
                        Description = "Sayfalama için kullanılacak query parameter adı",
                        ParameterType = typeof(string),
                        IsRequired = false,
                        DefaultValue = "page"
                    }
                },
                SupportedFormats = new List<string> { "JSON", "XML", "CSV" },
                SupportsBatch = false,
                SupportsStreaming = true,
                MaxBatchSize = 0,
                OutputTypes = new Dictionary<string, Type>
                {
                    ["default"] = typeof(List<object>)
                },
                Capabilities = new List<Capability>
                {
                    new Capability
                    {
                        Name = "Pagination",
                        Description = "Sayfalama desteği",
                        Parameters = new Dictionary<string, object>
                        {
                            ["page_parameter"] = "page",
                            ["limit_parameter"] = "limit"
                        }
                    },
                    new Capability
                    {
                        Name = "Authentication",
                        Description = "API Key ve Bearer token desteği"
                    },
                    new Capability
                    {
                        Name = "Filtering",
                        Description = "Query parameter ile filtreleme"
                    }
                }
            };
        }

        public async Task<bool> ShutdownAsync()
        {
            _logger?.LogDebug("RestSourcePlugin kapatılıyor...");
            _httpClient?.Dispose();
            return true;
        }
    }
}
