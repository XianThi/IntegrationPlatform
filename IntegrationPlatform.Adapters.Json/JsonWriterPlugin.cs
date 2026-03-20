using IntegrationPlatform.Common.Enums;
using IntegrationPlatform.Common.Interfaces;
using IntegrationPlatform.Common.Interfaces.Plugins;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IntegrationPlatform.Adapters.Json
{
    public class JsonWriterPlugin : JsonPluginBase, IDestinationPlugin
    {
        public override string Id => "IntegrationPlatform.Adapters.Json.Writer";
        public override string Name => "JSON Writer";
        public override string Description => "JSON dosyalarına yazar";
        public override AdapterDirection Direction => AdapterDirection.Destination;
        public override AdapterType Type => AdapterType.JsonWriter;

        public JsonWriterPlugin() : base() { }
        public JsonWriterPlugin(ILogger<JsonWriterPlugin> logger) : base(logger) { }

        protected override Dictionary<string, ParameterDefinition> GetConfigurationSchema()
        {
            return new Dictionary<string, ParameterDefinition>
            {
                ["FilePath"] = new ParameterDefinition
                {
                    Name = "FilePath",
                    DisplayName = "Dosya Yolu",
                    Description = "Yazılacak JSON dosyasının yolu",
                    ParameterType = typeof(string),
                    IsRequired = true
                },
                ["PrettyPrint"] = new ParameterDefinition
                {
                    Name = "PrettyPrint",
                    DisplayName = "Güzel Yazdır",
                    ParameterType = typeof(bool),
                    DefaultValue = true
                }
            };
        }

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
    }
}
