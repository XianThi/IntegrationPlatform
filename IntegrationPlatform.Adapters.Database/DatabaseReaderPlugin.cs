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
    public class DatabaseReaderPlugin : DatabasePluginBase, ISourcePlugin
    {
        public override AdapterDirection Direction => AdapterDirection.Source;

        public override string Id => "IntegrationPlatform.Adapters.Database";

        public override string Name => "Database Reader";

        public override AdapterType Type => AdapterType.Database;

        public string Description => "Veritabanından veri çekmek için kaynak bağlantısı kurar.";

        public Version Version => new Version(1, 0, 0);
        public string Author => "Integration Platform";

        #region ISourcePlugin - Veri Çekme

        public async Task<SourceData> FetchAsync(SourceContext context)
        {
            _logger?.LogDebug("Veritabanından veri çekiliyor...");

            var connectionString = context.Configuration["ConnectionString"]?.ToString();
            var query = context.Configuration["Query"]?.ToString();
            var provider = context.Configuration.GetValueOrDefault("Provider", "sqlserver")?.ToString();
            var commandTimeout = (int)context.Configuration.GetValueOrDefault("CommandTimeout", 30);

            if (string.IsNullOrEmpty(connectionString))
                throw new ArgumentException("ConnectionString parametresi zorunludur");

            if (string.IsNullOrEmpty(query))
                throw new ArgumentException("Query parametresi zorunludur");

            var stopwatch = Stopwatch.StartNew();
            var data = new List<object>();

            using (var connection = CreateConnection(provider, connectionString))
            {
                await OpenConnectionAsync(connection, context.CancellationToken);

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = query;
                    command.CommandTimeout = commandTimeout;
                    command.CommandType = CommandType.Text;

                    // Parametreleri ekle
                    if (context.Parameters != null)
                    {
                        foreach (var param in context.Parameters)
                        {
                            var dbParam = command.CreateParameter();
                            dbParam.ParameterName = param.Key;
                            dbParam.Value = param.Value ?? DBNull.Value;
                            command.Parameters.Add(dbParam);
                        }
                    }

                    using (var reader = await ExecuteReaderAsync(command, context.CancellationToken))
                    {
                        while (reader.Read())
                        {
                            var row = new Dictionary<string, object>();
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                var value = reader.GetValue(i);
                                row[reader.GetName(i)] = value == DBNull.Value ? null : value;
                            }
                            data.Add(row);
                        }
                    }
                }
            }

            stopwatch.Stop();

            // Schema çıkar (ilk kayıttan)
            DataSchema schema = null;
            if (data.Any())
            {
                schema = SchemaDetector.DetectSchema(data.First());

                // Tablo ismini bulmaya çalış
                var tableName = ExtractTableNameFromQuery(query);
                if (!string.IsNullOrEmpty(tableName))
                {
                    schema.Name = tableName;
                }
            }

            return new SourceData
            {
                Data = data,
                TotalCount = data.Count,
                Metadata = new Dictionary<string, object>
                {
                    ["provider"] = provider,
                    ["row_count"] = data.Count,
                    ["elapsed_ms"] = stopwatch.ElapsedMilliseconds,
                    ["query"] = query.Length > 100 ? query[..100] + "..." : query
                },
                Schema = schema,
                ElapsedTime = stopwatch.Elapsed
            };
        }

        public async Task<SourceTestResult> TestConnectionAsync(Dictionary<string, object> configuration)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new SourceTestResult();

            try
            {
                var connectionString = configuration["ConnectionString"]?.ToString();
                var provider = configuration.GetValueOrDefault("Provider", "sqlserver")?.ToString();
                var testQuery = configuration.GetValueOrDefault("TestQuery", "SELECT 1")?.ToString();

                if (string.IsNullOrEmpty(connectionString))
                {
                    result.IsSuccess = false;
                    result.Message = "ConnectionString parametresi zorunludur";
                    return result;
                }

                using (var connection = CreateConnection(provider, connectionString))
                {
                    await OpenConnectionAsync(connection, CancellationToken.None);

                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = testQuery;
                        command.CommandTimeout = 10;

                        var testResult = await ExecuteScalarAsync(command, CancellationToken.None);

                        stopwatch.Stop();

                        // Tablo listesini al (opsiyonel)
                        var tables = await GetTableListAsync(connection, provider);

                        result.IsSuccess = true;
                        result.Message = $"Bağlantı başarılı. Test sorgu sonucu: {testResult}";
                        result.ResponseTime = stopwatch.Elapsed;
                        result.DetectedSchema = new DataSchema
                        {
                            Name = "Database",
                            Fields = new List<DataField>()
                        };
                        result.Metadata = new Dictionary<string, object>
                        {
                            ["provider"] = provider,
                            ["database"] = connection.Database,
                            ["tables"] = tables,
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                result.IsSuccess = false;
                result.Message = ex.Message;
                result.ResponseTime = stopwatch.Elapsed;
            }

            return result;
        }

        public async Task<DataSchema> DiscoverSchemaAsync(Dictionary<string, object> configuration)
        {
            var schema = new DataSchema();

            try
            {
                var connectionString = configuration["ConnectionString"]?.ToString();
                var provider = configuration.GetValueOrDefault("Provider", "sqlserver")?.ToString();
                var tableName = configuration["TableName"]?.ToString();

                if (string.IsNullOrEmpty(tableName))
                {
                    // Tablo adı yoksa, sample query ile şema çıkar
                    var testResult = await TestConnectionAsync(configuration);
                    if (testResult.IsSuccess && testResult.Metadata?.ContainsKey("tables") == true)
                    {
                        schema.Metadata = new Dictionary<string, object>
                        {
                            ["available_tables"] = testResult.Metadata["tables"]
                        };
                    }
                    return schema;
                }

                using (var connection = CreateConnection(provider, connectionString))
                {
                    await OpenConnectionAsync(connection, CancellationToken.None);

                    var schemaTable = await GetTableSchemaAsync(connection, tableName, provider);

                    schema.Name = tableName;
                    schema.Fields = schemaTable;
                    schema.Metadata = new Dictionary<string, object>
                    {
                        ["provider"] = provider,
                        ["database"] = connection.Database
                    };
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Schema keşfi başarısız");
            }

            return schema;
        }

        public async Task<SourceData> FetchNextAsync(SourceContext context, string continuationToken)
        {
            // Sayfalama için - offset/limit ile çalış
            var pageSize = (int)context.Configuration.GetValueOrDefault("PageSize", 1000);
            var page = int.Parse(continuationToken ?? "0");

            var originalQuery = context.Configuration["Query"]?.ToString();

            // Query'ye sayfalama ekle
            var pagedQuery = AddPaginationToQuery(originalQuery, page * pageSize, pageSize,
                context.Configuration.GetValueOrDefault("Provider", "sqlserver")?.ToString());

            context.Configuration["Query"] = pagedQuery;

            var result = await FetchAsync(context);

            // Sonraki sayfa token'ı
            if (result.Data.Count == pageSize)
            {
                result.ContinuationToken = (page + 1).ToString();
            }

            return result;
        }

        public async IAsyncEnumerable<object> FetchStreamAsync(SourceContext context, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var connectionString = context.Configuration["ConnectionString"]?.ToString();
            var query = context.Configuration["Query"]?.ToString();
            var provider = context.Configuration.GetValueOrDefault("Provider", "sqlserver")?.ToString();
            var batchSize = (int)context.Configuration.GetValueOrDefault("BatchSize", 100);

            using (var connection = CreateConnection(provider, connectionString))
            {
                await OpenConnectionAsync(connection, cancellationToken);

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = query;
                    command.CommandTimeout = 0; // No timeout for streaming

                    using (var reader = command.ExecuteReader())
                    {
                        var batch = new List<object>();

                        while (reader.Read())
                        {
                            var row = new Dictionary<string, object>();
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                var value = reader.GetValue(i);
                                row[reader.GetName(i)] = value == DBNull.Value ? null : value;
                            }

                            batch.Add(row);

                            if (batch.Count >= batchSize)
                            {
                                foreach (var item in batch)
                                    yield return item;
                                batch.Clear();
                            }
                        }

                        // Kalanları gönder
                        foreach (var item in batch)
                            yield return item;
                    }
                }
            }
        }

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

                    // Source parametreleri
                    ["Query"] = new ParameterDefinition
                    {
                        Name = "Query",
                        DisplayName = "SQL Sorgusu",
                        Description = "Çalıştırılacak SQL sorgusu",
                        ParameterType = typeof(string),
                        IsRequired = false,
                        IsMultiline = true
                    },
                    ["CommandTimeout"] = new ParameterDefinition
                    {
                        Name = "CommandTimeout",
                        DisplayName = "Komut Zaman Aşımı",
                        Description = "Sorgu çalışma zaman aşımı (saniye)",
                        ParameterType = typeof(int),
                        IsRequired = false,
                        DefaultValue = 30
                    },
                    ["PageSize"] = new ParameterDefinition
                    {
                        Name = "PageSize",
                        DisplayName = "Sayfa Boyutu",
                        Description = "Sayfalama için kayıt sayısı",
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
                    new Capability { Name = "Query", Description = "SQL sorgusu çalıştırır" },
                    new Capability { Name = "SchemaDiscovery", Description = "Tablo şemasını keşfeder" }
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
