using IntegrationPlatform.Common.Enums;
using IntegrationPlatform.Common.Helpers;
using IntegrationPlatform.Common.Interfaces;
using IntegrationPlatform.Common.Interfaces.Plugins;
using IntegrationPlatform.Common.Models;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Diagnostics;

namespace IntegrationPlatform.Adapters.Database
{
    internal class DatabaseWriterPlugin : DatabasePluginBase, IDestinationPlugin
    {
        public override AdapterDirection Direction => AdapterDirection.Destination;

        public override string Id => "IntegrationPlatform.Adapters.Database.Writer";

        public override string Name => "Database Writer";

        public override AdapterType Type => AdapterType.DatabaseWriter;

        public string Description => "Veritabanına veri yazmak için gerekli bağlantı nesnesini oluşturur..";

        public Version Version => new Version(1, 0, 0);
        public string Author => "Integration Platform";
        #region ISourcePlugin - Veri Çekme

        #region IDestinationPlugin - Veri Yazma

        public async Task<DestinationTestResult> TestDestinationAsync(Dictionary<string, object> configuration)
        {
            var result = new DestinationTestResult
            {
                Details = new Dictionary<string, object>(),
                Warnings = new List<string>(),
                Errors = new List<string>()
            };

            try
            {
                var connectionString = configuration["ConnectionString"]?.ToString();
                var provider = configuration.GetValueOrDefault("Provider", "sqlserver")?.ToString();
                var tableName = configuration["TableName"]?.ToString();

                if (string.IsNullOrEmpty(connectionString))
                {
                    result.Errors.Add("ConnectionString parametresi zorunludur");
                    result.IsSuccess = false;
                    result.Message = "Konfigürasyon hatası";
                    return result;
                }

                using (var connection = CreateConnection(provider, connectionString))
                {
                    await OpenConnectionAsync(connection, CancellationToken.None);

                    result.Details["database"] = connection.Database;
                    result.Details["connection_timeout"] = connection.ConnectionTimeout;
                    result.CanWrite = true;

                    // Tablo kontrolü
                    if (!string.IsNullOrEmpty(tableName))
                    {
                        var tableExists = await CheckTableExistsAsync(connection, tableName, provider);
                        result.DestinationExists = tableExists;
                        result.Details["table_name"] = tableName;
                        result.Details["table_exists"] = tableExists;

                        if (!tableExists && (bool)configuration.GetValueOrDefault("CreateIfNotExists", false))
                        {
                            result.Warnings.Add($"Tablo '{tableName}' mevcut değil, oluşturulacak");
                        }
                        else if (!tableExists)
                        {
                            result.Errors.Add($"Tablo '{tableName}' mevcut değil");
                        }
                    }

                    // Yazma izni test et (küçük bir test kaydı dene)
                    if (!string.IsNullOrEmpty(tableName) && result.DestinationExists)
                    {
                        var testResult = await TestWritePermissionAsync(connection, tableName, provider);
                        if (!testResult)
                        {
                            result.CanWrite = false;
                            result.Warnings.Add("Tablo yazma izni test edilemedi");
                        }
                    }
                }

                result.IsSuccess = result.Errors.Count == 0;
                result.Message = result.IsSuccess ? "Destination test başarılı" : "Destination test başarısız";
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.Message = "Test hatası";
                result.Errors.Add(ex.Message);
            }

            return result;
        }

        public async Task<WriteResult> WriteAsync(object data, DestinationContext context)
        {
            _logger?.LogDebug("Veritabanına veri yazılıyor...");

            var connectionString = context.Configuration["ConnectionString"]?.ToString();
            var tableName = context.Configuration["TableName"]?.ToString();
            var provider = context.Configuration.GetValueOrDefault("Provider", "sqlserver")?.ToString();
            var batchSize = context.BatchSize > 0 ? context.BatchSize : 1000;

            if (string.IsNullOrEmpty(connectionString))
                throw new ArgumentException("ConnectionString parametresi zorunludur");

            if (string.IsNullOrEmpty(tableName))
                throw new ArgumentException("TableName parametresi zorunludur");

            var stopwatch = Stopwatch.StartNew();
            var recordsWritten = 0;
            var errors = new List<WriteError>();

            // Data tipine göre işle
            var records = data as IEnumerable<object>;
            if (records == null)
            {
                records = new[] { data };
            }

            var recordsList = records.ToList();

            using (var connection = CreateConnection(provider, connectionString))
            {
                await OpenConnectionAsync(connection, context.CancellationToken);

                // Tablo yoksa oluştur
                if (context.CreateIfNotExists)
                {
                    var tableExists = await CheckTableExistsAsync(connection, tableName, provider);
                    if (!tableExists && recordsList.Any())
                    {
                        await CreateTableFromDataAsync(connection, tableName, recordsList.First(), provider);
                    }
                }

                // Batch'ler halinde yaz
                for (int i = 0; i < recordsList.Count; i += batchSize)
                {
                    var batch = recordsList.Skip(i).Take(batchSize).ToList();

                    try
                    {
                        var insertCount = await WriteBatchAsync(connection, tableName, batch, context, provider);
                        recordsWritten += insertCount;
                    }
                    catch (Exception ex)
                    {
                        errors.Add(new WriteError
                        {
                            RecordIndex = i,
                            ErrorMessage = ex.Message,
                            ErrorCode = "BATCH_WRITE_ERROR"
                        });

                        if (context.WriteMode == WriteMode.Overwrite)
                        {
                            // Overwrite modunda hata olursa dur
                            break;
                        }
                    }
                }
            }

            stopwatch.Stop();

            return new WriteResult
            {
                IsSuccess = errors.Count == 0,
                RecordsWritten = recordsWritten,
                RecordsFailed = recordsList.Count - recordsWritten,
                Errors = errors,
                ElapsedTime = stopwatch.Elapsed,
                TargetMetadata = new Dictionary<string, object>
                {
                    ["provider"] = provider,
                    ["table"] = tableName,
                    ["batch_size"] = batchSize
                }
            };
        }

        public async Task<WriteResult> WriteBatchAsync(List<object> data, DestinationContext context)
        {
            return await WriteAsync(data, context);
        }

        public async Task<WriteResult> WriteStreamAsync(IAsyncEnumerable<object> dataStream, DestinationContext context)
        {
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
            var connectionString = context.Configuration["ConnectionString"]?.ToString();
            var tableName = context.Configuration["TableName"]?.ToString() ?? schema.Name;
            var provider = context.Configuration.GetValueOrDefault("Provider", "sqlserver")?.ToString();

            using (var connection = CreateConnection(provider, connectionString))
            {
                await OpenConnectionAsync(connection, context.CancellationToken);
                return await CreateTableFromSchemaAsync(connection, tableName, schema, provider);
            }
        }

        public async Task<WriteResult> UpsertAsync(object data, DestinationContext context, string keyField)
        {
            // Basit upsert - önce sil, sonra ekle
            var connectionString = context.Configuration["ConnectionString"]?.ToString();
            var tableName = context.Configuration["TableName"]?.ToString();
            var provider = context.Configuration.GetValueOrDefault("Provider", "sqlserver")?.ToString();

            using (var connection = CreateConnection(provider, connectionString))
            {
                await OpenConnectionAsync(connection, context.CancellationToken);

                // Key'e göre sil
                if (data is Dictionary<string, object> row && row.ContainsKey(keyField))
                {
                    var deleteSql = provider switch
                    {
                        "postgresql" => $"DELETE FROM {tableName} WHERE {keyField} = @key",
                        "mysql" => $"DELETE FROM {tableName} WHERE {keyField} = @key",
                        _ => $"DELETE FROM [{tableName}] WHERE [{keyField}] = @key"
                    };

                    using (var deleteCmd = connection.CreateCommand())
                    {
                        deleteCmd.CommandText = deleteSql;
                        var param = deleteCmd.CreateParameter();
                        param.ParameterName = "@key";
                        param.Value = row[keyField] ?? DBNull.Value;
                        deleteCmd.Parameters.Add(param);
                        deleteCmd.ExecuteNonQuery();
                    }
                }
            }

            // Sonra ekle
            return await WriteAsync(data, context);
        }

        #endregion

        public async Task<AdapterMetadata> GetMetadataAsync()
        {
            return new AdapterMetadata
            {
                ConfigurationSchema = new Dictionary<string, ParameterDefinition>
                {
                    // Ortak parametreler
                    ["ConnectionString"] = new ParameterDefinition
                    {
                        Name = "ConnectionString",
                        DisplayName = "Bağlantı Cümlesi",
                        Description = "Veritabanı bağlantı cümlesi",
                        ParameterType = typeof(string),
                        IsRequired = true,
                        IsSecret = true
                    },
                    ["Provider"] = new ParameterDefinition
                    {
                        Name = "Provider",
                        DisplayName = "Veritabanı Türü",
                        Description = "Veritabanı sağlayıcısı",
                        ParameterType = typeof(string),
                        IsRequired = true,
                        PossibleValues = new List<string> { "sqlserver", "postgresql", "mysql" },
                        DefaultValue = "sqlserver"
                    },

                    // Destination parametreleri
                    ["TableName"] = new ParameterDefinition
                    {
                        Name = "TableName",
                        DisplayName = "Tablo Adı",
                        Description = "Yazılacak tablo adı",
                        ParameterType = typeof(string),
                        IsRequired = false
                    },
                    ["WriteMode"] = new ParameterDefinition
                    {
                        Name = "WriteMode",
                        DisplayName = "Yazma Modu",
                        Description = "Veri yazma stratejisi",
                        ParameterType = typeof(string),
                        IsRequired = false,
                        PossibleValues = new List<string> { "Append", "Overwrite", "Merge" },
                        DefaultValue = "Append"
                    },
                    ["CreateIfNotExists"] = new ParameterDefinition
                    {
                        Name = "CreateIfNotExists",
                        DisplayName = "Tablo Yoksa Oluştur",
                        Description = "Tablo yoksa otomatik oluşturulsun mu?",
                        ParameterType = typeof(bool),
                        IsRequired = false,
                        DefaultValue = true
                    },
                    ["BatchSize"] = new ParameterDefinition
                    {
                        Name = "BatchSize",
                        DisplayName = "Toplu Yazma Boyutu",
                        Description = "Tek seferde yazılacak kayıt sayısı",
                        ParameterType = typeof(int),
                        IsRequired = false,
                        DefaultValue = 1000
                    }
                },
                SupportedFormats = new List<string> { "Table", "Query", "StoredProcedure" },
                SupportsBatch = true,
                SupportsStreaming = true,
                MaxBatchSize = 10000,
                Capabilities = new List<Capability>
                {
                    new Capability { Name = "BulkInsert", Description = "Toplu veri ekleme" },
                    new Capability { Name = "SchemaDiscovery", Description = "Tablo şemasını keşfeder" },
                    new Capability { Name = "TableCreation", Description = "Tablo oluşturur" },
                    new Capability { Name = "Upsert", Description = "Varolan kayıtları günceller" },
                    new Capability { Name = "Streaming", Description = "Büyük veri kümelerini akıcı işler" }
                }
            };
        }


        public async Task<bool> InitializeAsync(PluginContext context)
        {
            _logger?.LogDebug("DatabasePlugin başlatılıyor...");
            return true;
        }

        public async Task<bool> ShutdownAsync()
        {
            _logger?.LogDebug("DatabasePlugin kapatılıyor...");
            return true;
        }


        #endregion
    }
}
