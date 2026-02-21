using IntegrationPlatform.Common.Enums;
using IntegrationPlatform.Common.Helpers;
using IntegrationPlatform.Common.Interfaces;
using IntegrationPlatform.Common.Interfaces.Plugins;
using IntegrationPlatform.Common.Models;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Diagnostics;
using System.Text;

namespace IntegrationPlatform.Adapters.Database
{
    public class DatabasePlugin : ISourcePlugin, IDestinationPlugin
    {
        private readonly ILogger<DatabasePlugin>? _logger;
        private Dictionary<string, IDbConnection> _connections;

        public string Id => "IntegrationPlatform.Adapters.Database";
        public string Name => "Database İşlemleri";
        public string Description => "Veritabanlarından veri çeker, tablolara veri yazar. SQL Server, PostgreSQL, MySQL destekler.";
        public Version Version => new Version(1, 0, 0);
        public string Author => "Integration Platform";
        public AdapterDirection Direction => AdapterDirection.Source;  // Primary type
        public AdapterType Type => AdapterType.Database;

        public DatabasePlugin(ILogger<DatabasePlugin> logger)
        {
            _logger = logger;
            _connections = new Dictionary<string, IDbConnection>();
        }
        public DatabasePlugin()
        {
            _connections = new Dictionary<string, IDbConnection>();
        }
        public async Task<bool> InitializeAsync(PluginContext context)
        {
            _logger?.LogDebug("DatabasePlugin başlatılıyor...");
            return true;
        }

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

        #endregion

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

        #region Database Helpers

        private IDbConnection CreateConnection(string provider, string connectionString)
        {
            return provider?.ToLower() switch
            {
                "postgresql" or "postgres" => new Npgsql.NpgsqlConnection(connectionString),
                "mysql" => new MySql.Data.MySqlClient.MySqlConnection(connectionString),
                _ => new Microsoft.Data.SqlClient.SqlConnection(connectionString)
            };
        }

        private async Task OpenConnectionAsync(IDbConnection connection, CancellationToken cancellationToken)
        {
            if (connection.State != ConnectionState.Open)
            {
                var openTask = Task.Run(() => connection.Open(), cancellationToken);
                await openTask;
            }
        }

        private async Task<int> ExecuteNonQueryAsync(IDbCommand command, CancellationToken cancellationToken)
        {
            var executeTask = Task.Run(() => command.ExecuteNonQuery(), cancellationToken);
            return await executeTask;
        }

        private async Task<IDataReader> ExecuteReaderAsync(IDbCommand command, CancellationToken cancellationToken)
        {
            var readerTask = Task.Run(() => command.ExecuteReader(), cancellationToken);
            return await readerTask;
        }

        private async Task<object> ExecuteScalarAsync(IDbCommand command, CancellationToken cancellationToken)
        {
            var scalarTask = Task.Run(() => command.ExecuteScalar(), cancellationToken);
            return await scalarTask;
        }

        private async Task<List<string>> GetTableListAsync(IDbConnection connection, string provider)
        {
            var tables = new List<string>();

            try
            {
                string schemaQuery = provider?.ToLower() switch
                {
                    "postgresql" or "postgres" =>
                        "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'",
                    "mysql" =>
                        "SHOW TABLES",
                    _ =>
                        "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'"
                };

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = schemaQuery;
                    command.CommandTimeout = 10;

                    using (var reader = await ExecuteReaderAsync(command, CancellationToken.None))
                    {
                        while (reader.Read())
                        {
                            tables.Add(reader[0].ToString());
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Tablo listesi alınamadı");
            }

            return tables;
        }

        private async Task<List<DataField>> GetTableSchemaAsync(IDbConnection connection, string tableName, string provider)
        {
            var fields = new List<DataField>();

            try
            {
                string schemaQuery = provider?.ToLower() switch
                {
                    "postgresql" or "postgres" => @"
                        SELECT 
                            column_name,
                            data_type,
                            is_nullable = 'YES' as is_nullable
                        FROM information_schema.columns 
                        WHERE table_name = @tableName",
                    "mysql" => @"
                        SELECT 
                            COLUMN_NAME,
                            DATA_TYPE,
                            IS_NULLABLE = 'YES' as is_nullable
                        FROM INFORMATION_SCHEMA.COLUMNS 
                        WHERE TABLE_NAME = @tableName",
                    _ => @"
                        SELECT 
                            COLUMN_NAME,
                            DATA_TYPE,
                            IS_NULLABLE
                        FROM INFORMATION_SCHEMA.COLUMNS 
                        WHERE TABLE_NAME = @tableName"
                };

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = schemaQuery;

                    var param = command.CreateParameter();
                    param.ParameterName = "@tableName";
                    param.Value = tableName;
                    command.Parameters.Add(param);

                    using (var reader = await ExecuteReaderAsync(command, CancellationToken.None))
                    {
                        while (reader.Read())
                        {
                            fields.Add(new DataField
                            {
                                Name = reader["COLUMN_NAME"].ToString(),
                                Type = MapDbTypeToDataType(reader["DATA_TYPE"].ToString()),
                                IsNullable = reader["IS_NULLABLE"].ToString() == "YES"
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Tablo şeması alınamadı");
            }

            return fields;
        }

        private async Task<bool> CheckTableExistsAsync(IDbConnection connection, string tableName, string provider)
        {
            try
            {
                string checkQuery = provider?.ToLower() switch
                {
                    "postgresql" or "postgres" =>
                        "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = @tableName",
                    "mysql" =>
                        "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = @tableName",
                    _ =>
                        "SELECT COUNT(*) FROM sysobjects WHERE name = @tableName AND xtype = 'U'"
                };

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = checkQuery;

                    var param = command.CreateParameter();
                    param.ParameterName = "@tableName";
                    param.Value = tableName;
                    command.Parameters.Add(param);

                    var count = await ExecuteScalarAsync(command, CancellationToken.None);
                    return Convert.ToInt32(count) > 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> TestWritePermissionAsync(IDbConnection connection, string tableName, string provider)
        {
            try
            {
                // Basit bir insert yapmayı dene (sonra geri al)
                string testQuery = provider?.ToLower() switch
                {
                    "postgresql" or "postgres" =>
                        $"INSERT INTO {tableName} DEFAULT VALUES RETURNING 1",
                    "mysql" =>
                        $"INSERT INTO {tableName} () VALUES ()",
                    _ =>
                        $"INSERT INTO [{tableName}] DEFAULT OUTPUT INSERTED.ID VALUES ()"
                };

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = testQuery;
                    command.CommandTimeout = 5;

                    try
                    {
                        await ExecuteScalarAsync(command, CancellationToken.None);
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        private async Task<int> WriteBatchAsync(
            IDbConnection connection,
            string tableName,
            List<object> batch,
            DestinationContext context,
            string provider)
        {
            if (!batch.Any()) return 0;

            // Batch'teki tüm kayıtların aynı alanlara sahip olduğunu varsay
            var firstRow = batch.First() as Dictionary<string, object>;
            if (firstRow == null) return 0;

            var columns = firstRow.Keys.ToList();

            // Insert statement oluştur
            var insertSql = BuildInsertStatement(tableName, columns, provider, batch.Count);

            using (var transaction = connection.BeginTransaction())
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = insertSql;
                command.CommandTimeout = 60;

                // Parametreleri ekle
                for (int i = 0; i < batch.Count; i++)
                {
                    var row = batch[i] as Dictionary<string, object>;
                    if (row == null) continue;

                    foreach (var col in columns)
                    {
                        var param = command.CreateParameter();
                        param.ParameterName = $"@p{i}_{col}";
                        param.Value = row.GetValueOrDefault(col) ?? DBNull.Value;
                        command.Parameters.Add(param);
                    }
                }

                try
                {
                    var result = await ExecuteNonQueryAsync(command, context.CancellationToken);
                    transaction.Commit();
                    return result;
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        private string BuildInsertStatement(string tableName, List<string> columns, string provider, int rowCount)
        {
            var columnList = string.Join(", ", columns.Select(c => provider == "sqlserver" ? $"[{c}]" : c));

            var valueList = new List<string>();
            for (int i = 0; i < rowCount; i++)
            {
                var rowValues = string.Join(", ", columns.Select(c => $"@p{i}_{c}"));
                valueList.Add($"({rowValues})");
            }

            return provider?.ToLower() switch
            {
                "postgresql" or "postgres" =>
                    $"INSERT INTO {tableName} ({columnList}) VALUES {string.Join(", ", valueList)}",
                "mysql" =>
                    $"INSERT INTO {tableName} ({columnList}) VALUES {string.Join(", ", valueList)}",
                _ =>
                    $"INSERT INTO [{tableName}] ({columnList}) VALUES {string.Join(", ", valueList)}"
            };
        }

        private async Task<bool> CreateTableFromDataAsync(
            IDbConnection connection,
            string tableName,
            object sampleData,
            string provider)
        {
            if (sampleData is not Dictionary<string, object> row) return false;

            var createSql = new StringBuilder();
            createSql.Append(provider == "sqlserver" ? $"CREATE TABLE [{tableName}] (" : $"CREATE TABLE {tableName} (");

            var columns = new List<string>();
            foreach (var kvp in row)
            {
                var sqlType = GetSqlType(kvp.Value, provider);
                columns.Add(provider == "sqlserver" ? $"[{kvp.Key}] {sqlType}" : $"{kvp.Key} {sqlType}");
            }

            createSql.Append(string.Join(", ", columns));
            createSql.Append(")");

            using (var command = connection.CreateCommand())
            {
                command.CommandText = createSql.ToString();
                try
                {
                    await ExecuteNonQueryAsync(command, CancellationToken.None);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Tablo oluşturulamadı");
                    return false;
                }
            }
        }

        private async Task<bool> CreateTableFromSchemaAsync(
            IDbConnection connection,
            string tableName,
            DataSchema schema,
            string provider)
        {
            var createSql = new StringBuilder();
            createSql.Append(provider == "sqlserver" ? $"CREATE TABLE [{tableName}] (" : $"CREATE TABLE {tableName} (");

            var columns = new List<string>();
            foreach (var field in schema.Fields)
            {
                var sqlType = GetSqlTypeFromDataType(field.Type, provider);
                var nullable = field.IsNullable ? "" : " NOT NULL";
                columns.Add(provider == "sqlserver" ? $"[{field.Name}] {sqlType}{nullable}" : $"{field.Name} {sqlType}{nullable}");
            }

            createSql.Append(string.Join(", ", columns));
            createSql.Append(")");

            using (var command = connection.CreateCommand())
            {
                command.CommandText = createSql.ToString();
                try
                {
                    await ExecuteNonQueryAsync(command, CancellationToken.None);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        private string AddPaginationToQuery(string query, int offset, int limit, string provider)
        {
            return provider?.ToLower() switch
            {
                "postgresql" or "postgres" or "mysql" =>
                    $"{query} LIMIT {limit} OFFSET {offset}",
                _ =>
                    $"{query} OFFSET {offset} ROWS FETCH NEXT {limit} ROWS ONLY"
            };
        }

        private string ExtractTableNameFromQuery(string query)
        {
            // Basit bir FROM clause parsing
            var fromIndex = query.IndexOf("FROM", StringComparison.OrdinalIgnoreCase);
            if (fromIndex >= 0)
            {
                var afterFrom = query[(fromIndex + 4)..].Trim();
                var spaceIndex = afterFrom.IndexOf(' ');
                if (spaceIndex > 0)
                {
                    return afterFrom[..spaceIndex].Trim('[', ']', '"', '`');
                }
                return afterFrom.Trim('[', ']', '"', '`');
            }
            return null;
        }

        private DataType MapDbTypeToDataType(string dbType)
        {
            return dbType.ToLower() switch
            {
                var s when s.Contains("int") => DataType.Integer,
                var s when s.Contains("bigint") => DataType.Long,
                var s when s.Contains("decimal") || s.Contains("numeric") || s.Contains("money") => DataType.Decimal,
                var s when s.Contains("date") || s.Contains("time") => DataType.DateTime,
                var s when s.Contains("bit") || s.Contains("bool") => DataType.Boolean,
                var s when s.Contains("uniqueidentifier") || s.Contains("guid") => DataType.Guid,
                var s when s.Contains("binary") || s.Contains("image") || s.Contains("bytea") => DataType.Binary,
                var s when s.Contains("json") => DataType.Object,
                _ => DataType.String
            };
        }

        private string GetSqlType(object value, string provider)
        {
            if (value == null) return "NVARCHAR(MAX)";

            return value switch
            {
                int => "INT",
                long => "BIGINT",
                decimal => "DECIMAL(18,2)",
                float => "FLOAT",
                double => "FLOAT",
                bool => provider == "postgresql" ? "BOOLEAN" : "BIT",
                DateTime => "DATETIME",
                Guid => provider == "postgresql" ? "UUID" : "UNIQUEIDENTIFIER",
                byte[] => provider == "postgresql" ? "BYTEA" : "VARBINARY(MAX)",
                _ => "NVARCHAR(MAX)"
            };
        }

        private string GetSqlTypeFromDataType(DataType dataType, string provider)
        {
            return dataType switch
            {
                DataType.Integer => "INT",
                DataType.Long => provider == "postgresql" ? "BIGINT" : "BIGINT",
                DataType.Decimal => "DECIMAL(18,2)",
                DataType.Boolean => provider == "postgresql" ? "BOOLEAN" : "BIT",
                DataType.DateTime => "DATETIME",
                DataType.Guid => provider == "postgresql" ? "UUID" : "UNIQUEIDENTIFIER",
                DataType.Binary => provider == "postgresql" ? "BYTEA" : "VARBINARY(MAX)",
                DataType.Object => "NVARCHAR(MAX)",
                _ => "NVARCHAR(MAX)"
            };
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
                    new Capability { Name = "Query", Description = "SQL sorgusu çalıştırır" },
                    new Capability { Name = "BulkInsert", Description = "Toplu veri ekleme" },
                    new Capability { Name = "SchemaDiscovery", Description = "Tablo şemasını keşfeder" },
                    new Capability { Name = "TableCreation", Description = "Tablo oluşturur" },
                    new Capability { Name = "Upsert", Description = "Varolan kayıtları günceller" },
                    new Capability { Name = "Streaming", Description = "Büyük veri kümelerini akıcı işler" }
                }
            };
        }

        public async Task<bool> ShutdownAsync()
        {
            _logger?.LogDebug("DatabasePlugin kapatılıyor...");

            foreach (var conn in _connections.Values)
            {
                try { conn.Close(); } catch { }
                try { conn.Dispose(); } catch { }
            }
            _connections.Clear();

            return true;
        }
    }
}
