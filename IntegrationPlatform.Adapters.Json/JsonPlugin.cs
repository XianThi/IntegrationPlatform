using IntegrationPlatform.Common.Enums;
using IntegrationPlatform.Common.Helpers;
using IntegrationPlatform.Common.Interfaces;
using IntegrationPlatform.Common.Interfaces.Plugins;
using IntegrationPlatform.Common.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IntegrationPlatform.Adapters.Json
{
    public class JsonPlugin : ISourcePlugin, ITransformPlugin, IDestinationPlugin
    {
        private readonly ILogger<JsonPlugin>? _logger;
        private JsonSerializerOptions _jsonOptions;

        public string Id => "IntegrationPlatform.Adapters.Json";
        public string Name => "JSON İşlemleri";
        public string Description => "JSON dosyalarını okur, yazar ve dönüştürür. Parse, stringify, mapping işlemleri.";
        public Version Version => new Version(1, 0, 0);
        public string Author => "Integration Platform";
        public AdapterDirection Direction => AdapterDirection.Transform;  // Primary type, ama hepsini yapıyor
        public AdapterType Type => AdapterType.JsonFile;
        public JsonPlugin() {
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
        }
        public JsonPlugin(ILogger<JsonPlugin> logger)
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

        public async Task<SourceData> FetchAsync(SourceContext context)
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

        private List<object> ParseJson(string content, Dictionary<string, object> configuration)
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

        #region ITransformPlugin - JSON Dönüşüm

        public async Task<object> TransformAsync(object input, TransformContext context)
        {
            _logger?.LogDebug("JSON dönüşümü yapılıyor...");

            var operation = context.Configuration.GetValueOrDefault("Operation", "parse")?.ToString();

            return operation switch
            {
                "parse" => ParseJsonString(input, context),
                "stringify" => StringifyObject(input, context),
                "map" => MapJsonFields(input, context),
                "filter" => FilterJsonData(input, context),
                "aggregate" => AggregateJsonData(input, context),
                _ => input
            };
        }

        private object ParseJsonString(object input, TransformContext context)
        {
            if (input is string jsonString)
            {
                return JsonNode.Parse(jsonString);
            }
            return input;
        }

        private object StringifyObject(object input, TransformContext context)
        {
            var pretty = context.Configuration.GetValueOrDefault("PrettyPrint", true);
            var options = (bool)pretty ? _jsonOptions : new JsonSerializerOptions();

            return JsonSerializer.Serialize(input, options);
        }

        private object MapJsonFields(object input, TransformContext context)
        {
            if (input is JsonObject jsonObject)
            {
                var newObj = new JsonObject();

                foreach (var mapping in context.Mapping)
                {
                    var sourceField = mapping.Key;
                    var targetField = mapping.Value;

                    if (jsonObject.ContainsKey(sourceField))
                    {
                        newObj[targetField] = jsonObject[sourceField]?.DeepClone();
                    }
                }

                return newObj;
            }

            return input;
        }

        private object FilterJsonData(object input, TransformContext context)
        {
            if (input is JsonArray jsonArray)
            {
                var filterField = context.Configuration["FilterField"]?.ToString();
                var filterValue = context.Configuration["FilterValue"]?.ToString();
                var filterOperator = context.Configuration.GetValueOrDefault("FilterOperator", "eq")?.ToString();

                var filtered = jsonArray.Where(item =>
                {
                    if (item is JsonObject obj && obj.ContainsKey(filterField))
                    {
                        var value = obj[filterField]?.ToString();

                        return filterOperator switch
                        {
                            "eq" => value == filterValue,
                            "neq" => value != filterValue,
                            "contains" => value?.Contains(filterValue) == true,
                            "startswith" => value?.StartsWith(filterValue) == true,
                            "endswith" => value?.EndsWith(filterValue) == true,
                            "gt" => decimal.TryParse(value, out var v) &&
                                    decimal.TryParse(filterValue, out var f) && v > f,
                            "lt" => decimal.TryParse(value, out var v) &&
                                    decimal.TryParse(filterValue, out var f) && v < f,
                            _ => true
                        };
                    }
                    return false;
                }).ToList();

                return new JsonArray(filtered.ToArray());
            }

            return input;
        }

        private object AggregateJsonData(object input, TransformContext context)
        {
            if (input is JsonArray jsonArray)
            {
                var groupBy = context.Configuration["GroupBy"]?.ToString();
                var aggregate = context.Configuration["Aggregate"]?.ToString(); // count, sum, avg, min, max
                var aggregateField = context.Configuration["AggregateField"]?.ToString();

                if (string.IsNullOrEmpty(groupBy))
                    return input;

                var groups = jsonArray
                    .Where(item => item is JsonObject)
                    .GroupBy(item => (item as JsonObject)?[groupBy]?.ToString())
                    .Select(g =>
                    {
                        var result = new JsonObject
                        {
                            [groupBy] = g.Key
                        };

                        if (!string.IsNullOrEmpty(aggregate) && !string.IsNullOrEmpty(aggregateField))
                        {
                            var values = g.Select(item =>
                            {
                                var obj = item as JsonObject;
                                return decimal.TryParse(obj?[aggregateField]?.ToString(), out var val) ? val : 0;
                            });

                            result[aggregate] = aggregate switch
                            {
                                "count" => g.Count(),
                                "sum" => values.Sum(),
                                "avg" => values.Average(),
                                "min" => values.Min(),
                                "max" => values.Max(),
                                _ => g.Count()
                            };
                        }
                        else
                        {
                            result["count"] = g.Count();
                        }

                        return result;
                    })
                    .ToList();

                return new JsonArray(groups.ToArray());
            }

            return input;
        }

        public async Task<List<object>> TransformBatchAsync(List<object> inputs, TransformContext context)
        {
            var results = new List<object>();

            foreach (var input in inputs)
            {
                var result = await TransformAsync(input, context);
                results.Add(result);
            }

            return results;
        }

        public async Task<DataSchema> TransformSchemaAsync(DataSchema inputSchema, TransformContext context)
        {
            var operation = context.Configuration.GetValueOrDefault("Operation", "parse")?.ToString();

            if (operation == "map" && context.Mapping.Any())
            {
                var newSchema = new DataSchema
                {
                    Name = inputSchema.Name,
                    Fields = new List<DataField>()
                };

                foreach (var mapping in context.Mapping)
                {
                    var sourceField = inputSchema.Fields.FirstOrDefault(f => f.Name == mapping.Key);
                    if (sourceField != null)
                    {
                        newSchema.Fields.Add(new DataField
                        {
                            Name = mapping.Value,
                            Type = sourceField.Type,
                            IsNullable = sourceField.IsNullable
                        });
                    }
                }

                return newSchema;
            }

            return inputSchema;
        }

        public async Task<ValidationResult> ValidateAsync(object input, TransformContext context)
        {
            var errors = new List<string>();
            var warnings = new List<string>();

            try
            {
                // JSON format kontrolü
                if (input is string jsonString)
                {
                    JsonDocument.Parse(jsonString);
                }
                else if (input is JsonNode)
                {
                    // Zaten JSON node, geçerli
                }
                else
                {
                    errors.Add("Geçersiz JSON formatı");
                }

                // Required field kontrolü
                if (context.Configuration.ContainsKey("RequiredFields"))
                {
                    var requiredFields = context.Configuration["RequiredFields"] as List<string>;
                    if (requiredFields != null && input is JsonObject jsonObject)
                    {
                        foreach (var field in requiredFields)
                        {
                            if (!jsonObject.ContainsKey(field))
                            {
                                errors.Add($"Required field eksik: {field}");
                            }
                        }
                    }
                }

                // Schema validasyonu
                if (context.TargetSchema != null && input is JsonObject obj)
                {
                    foreach (var field in context.TargetSchema.Fields)
                    {
                        if (obj.ContainsKey(field.Name))
                        {
                            var value = obj[field.Name]?.ToString();

                            // Tip kontrolü
                            if (!IsTypeCompatible(value, field.Type))
                            {
                                warnings.Add($"Field {field.Name} tip uyuşmazlığı. Beklenen: {field.Type}");
                            }
                        }
                        else if (!field.IsNullable)
                        {
                            errors.Add($"Field {field.Name} zorunlu ama yok");
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                errors.Add($"JSON parse hatası: {ex.Message}");
            }
            catch (Exception ex)
            {
                errors.Add($"Validasyon hatası: {ex.Message}");
            }

            return new ValidationResult
            {
                IsValid = !errors.Any(),
                Errors = errors,
                Warnings = warnings,
                ValidatedData = (Dictionary<string, object>)input
            };
        }

        private bool IsTypeCompatible(string value, DataType type)
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

        public async Task<Dictionary<string, string>> GetMappingSchemaAsync()
        {
            return new Dictionary<string, string>();
        }

        #endregion

        #region IDestinationPlugin - Json Dosyası Yazma

        public async Task<WriteResult> WriteAsync(object data, DestinationContext context)
        {
            _logger?.LogDebug("JSON dosyasına yazılıyor...");

            try
            {
                var filePath = context.Configuration["FilePath"]?.ToString();
                var writeMode = context.WriteMode;
                var pretty = context.Configuration.GetValueOrDefault("PrettyPrint", true);

                if (string.IsNullOrEmpty(filePath))
                    throw new ArgumentException("FilePath parametresi zorunludur");

                // Klasör var mı kontrol et
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    if (context.CreateIfNotExists)
                        Directory.CreateDirectory(directory);
                    else
                        throw new DirectoryNotFoundException($"Klasör bulunamadı: {directory}");
                }

                var stopwatch = Stopwatch.StartNew();

                string jsonContent;
                if ((bool)pretty)
                {
                    jsonContent = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                }
                else
                {
                    jsonContent = JsonSerializer.Serialize(data);
                }

                // Write mode'a göre yaz
                if (writeMode == WriteMode.Append && File.Exists(filePath))
                {
                    // Append için - NDJSON formatında her satıra bir JSON
                    using var writer = new StreamWriter(filePath, true);
                    if (data is IEnumerable<object> list)
                    {
                        foreach (var item in list)
                        {
                            await writer.WriteLineAsync(JsonSerializer.Serialize(item));
                        }
                    }
                    else
                    {
                        await writer.WriteLineAsync(jsonContent);
                    }
                }
                else
                {
                    // Overwrite veya normal yazma
                    if (writeMode == WriteMode.Overwrite && File.Exists(filePath))
                    {
                        File.Delete(filePath);
                    }

                    await File.WriteAllTextAsync(filePath, jsonContent);
                }

                stopwatch.Stop();

                var recordCount = data is IEnumerable<object> enumerable ? enumerable.Count() : 1;

                return new WriteResult
                {
                    IsSuccess = true,
                    RecordsWritten = recordCount,
                    RecordsFailed = 0,
                    ElapsedTime = stopwatch.Elapsed,
                    TargetMetadata = new Dictionary<string, object>
                    {
                        ["file_path"] = filePath,
                        ["file_size"] = new FileInfo(filePath).Length,
                        ["write_mode"] = writeMode.ToString()
                    }
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "JSON dosyasına yazılırken hata");

                return new WriteResult
                {
                    IsSuccess = false,
                    RecordsWritten = 0,
                    RecordsFailed = 1,
                    Errors = new List<WriteError>
                    {
                        new WriteError
                        {
                            RecordIndex = 0,
                            ErrorMessage = ex.Message,
                            ErrorCode = "JSON_WRITE_ERROR"
                        }
                    },
                    ElapsedTime = TimeSpan.Zero
                };
            }
        }

        public async Task<WriteResult> WriteBatchAsync(List<object> data, DestinationContext context)
        {
            return await WriteAsync(data, context);
        }

        public async Task<WriteResult> WriteStreamAsync(IAsyncEnumerable<object> dataStream, DestinationContext context)
        {
            var filePath = context.Configuration["FilePath"]?.ToString();
            var batchSize = context.BatchSize;
            var buffer = new List<object>();
            long totalWritten = 0;
            var errors = new List<WriteError>();
            var stopwatch = Stopwatch.StartNew();

            try
            {
                await foreach (var item in dataStream)
                {
                    buffer.Add(item);

                    if (buffer.Count >= batchSize)
                    {
                        var result = await WriteBatchAsync(buffer, context);
                        totalWritten += result.RecordsWritten;
                        errors.AddRange(result.Errors);
                        buffer.Clear();
                    }
                }

                // Kalanları yaz
                if (buffer.Any())
                {
                    var result = await WriteBatchAsync(buffer, context);
                    totalWritten += result.RecordsWritten;
                    errors.AddRange(result.Errors);
                }

                stopwatch.Stop();

                return new WriteResult
                {
                    IsSuccess = !errors.Any(),
                    RecordsWritten = totalWritten,
                    RecordsFailed = errors.Count,
                    Errors = errors,
                    ElapsedTime = stopwatch.Elapsed
                };
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                return new WriteResult
                {
                    IsSuccess = false,
                    RecordsWritten = totalWritten,
                    RecordsFailed = buffer.Count,
                    Errors = new List<WriteError>
                    {
                        new WriteError
                        {
                            ErrorMessage = ex.Message,
                            ErrorCode = "STREAM_WRITE_ERROR"
                        }
                    },
                    ElapsedTime = stopwatch.Elapsed
                };
            }
        }

        public async Task<bool> CreateTargetSchemaAsync(DataSchema schema, DestinationContext context)
        {
            // JSON için schema oluşturmaya gerek yok
            return true;
        }

        public async Task<bool> TestWritePermissionAsync(Dictionary<string, object> configuration)
        {
            try
            {
                var filePath = configuration["FilePath"]?.ToString();

                if (string.IsNullOrEmpty(filePath))
                    return false;

                var directory = Path.GetDirectoryName(filePath);

                // Klasör yazılabilir mi kontrol et
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    // Klasör oluşturmayı dene
                    Directory.CreateDirectory(directory);
                }

                // Test dosyası yazmayı dene
                var testFile = Path.Combine(directory ?? "", "_test_write.tmp");
                await File.WriteAllTextAsync(testFile, "{}");
                File.Delete(testFile);

                return true;
            }
            catch
            {
                return false;
            }
        }
        public async Task<DestinationTestResult> TestDestinationAsync(Dictionary<string, object> configuration)
        {
            try
            {
                var filePath = configuration["FilePath"]?.ToString();
                var results = new DestinationTestResult();

                if (string.IsNullOrEmpty(filePath))
                {
                    results.IsSuccess = false;
                    results.Message = "Dosya yolu belirtilmemiş";
                    return results;
                }

                var directory = Path.GetDirectoryName(filePath);

                // 1. Klasör kontrolü
                if (!string.IsNullOrEmpty(directory))
                {
                    if (!Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                        results.Warnings.Add($"Klasör oluşturuldu: {directory}");
                    }
                }

                // 2. Yazma izni testi
                var testFile = Path.Combine(directory ?? "", $"write_test_{Guid.NewGuid()}.tmp");
                await File.WriteAllTextAsync(testFile, "{\"test\": true}");

                var fileInfo = new FileInfo(testFile);
                results.CanWrite = true;

                File.Delete(testFile);

                // 3. Hedef dosya durumu
                if (File.Exists(filePath))
                {
                    var targetInfo = new FileInfo(filePath);
                    results.DestinationExists = true;
                }

                results.IsSuccess = true;
                results.Message = "Yazma testi başarılı";

                return results;
            }
            catch (UnauthorizedAccessException)
            {
                return new DestinationTestResult
                {
                    IsSuccess = false,
                    Message = "Yetki hatası: Dosyaya yazma izniniz yok",
                    CanWrite = false
                };
            }
            catch (Exception ex)
            {
                return new DestinationTestResult
                {
                    IsSuccess = false,
                    Message = $"Test hatası: {ex.Message}",
                    CanWrite = false
                };
            }
        }

        public async Task<WriteResult> UpsertAsync(object data, DestinationContext context, string keyField)
        {
            // JSON için upsert - dosyanın tamamını oku, güncelle, yaz
            var filePath = context.Configuration["FilePath"]?.ToString();

            if (File.Exists(filePath))
            {
                var existingData = await FetchAsync(new SourceContext
                {
                    Configuration = new Dictionary<string, object>
                    {
                        ["FilePath"] = filePath
                    }
                });

                // Mevcut veriyi güncelle
                var updatedData = UpdateData(existingData.Data, data, keyField);
                return await WriteAsync(updatedData, context);
            }

            return await WriteAsync(data, context);
        }

        private object UpdateData(List<object> existingData, object newData, string keyField)
        {
            if (existingData == null) return newData;

            var jsonArray = new JsonArray();

            // Mevcut kayıtları ekle
            foreach (var item in existingData)
            {
                jsonArray.Add(JsonSerializer.SerializeToNode(item));
            }

            // Yeni kaydı ekle veya güncelle
            if (newData is JsonObject newObj && !string.IsNullOrEmpty(keyField) && newObj.ContainsKey(keyField))
            {
                var key = newObj[keyField]?.ToString();

                // Aynı key varsa güncelle
                for (int i = 0; i < jsonArray.Count; i++)
                {
                    if (jsonArray[i] is JsonObject existingObj &&
                        existingObj.ContainsKey(keyField) &&
                        existingObj[keyField]?.ToString() == key)
                    {
                        jsonArray[i] = newObj;
                        return jsonArray;
                    }
                }
            }

            // Yoksa ekle
            jsonArray.Add(JsonSerializer.SerializeToNode(newData));
            return jsonArray;
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
    }
}
