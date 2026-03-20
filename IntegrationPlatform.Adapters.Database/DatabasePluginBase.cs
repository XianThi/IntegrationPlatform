using IntegrationPlatform.Common.Enums;
using IntegrationPlatform.Common.Interfaces.Plugins;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using Npgsql;
using System.Data;
using System.Data.Common;
using System.Text;

namespace IntegrationPlatform.Adapters.Database
{
    public abstract class DatabasePluginBase
    {
        protected readonly ILogger? _logger;

        public abstract string Id { get; }
        public abstract string Name { get; }
        public abstract AdapterDirection Direction { get; }
        public abstract AdapterType Type { get; }

        protected DatabasePluginBase(ILogger? logger = null)
        {
            _logger = logger;
        }

        // Factory Pattern: Provider tipine göre ilgili Connection nesnesini döner
        protected DbConnection CreateConnection(string provider, string connectionString)
        {
            DbConnection connection = provider.ToLower() switch
            {
                "sqlserver" => new SqlConnection(connectionString),
                "postgresql" => new NpgsqlConnection(connectionString),
                "mysql" => new MySqlConnection(connectionString),
                _ => throw new NotSupportedException($"Provider '{provider}' desteklenmiyor.")
            };

            return connection;
        }

        #region Database Helpers


        protected virtual async Task OpenConnectionAsync(IDbConnection connection, CancellationToken cancellationToken)
        {
            if (connection.State != ConnectionState.Open)
            {
                var openTask = Task.Run(() => connection.Open(), cancellationToken);
                await openTask;
            }
        }

        protected virtual async Task<int> ExecuteNonQueryAsync(IDbCommand command, CancellationToken cancellationToken)
        {
            var executeTask = Task.Run(() => command.ExecuteNonQuery(), cancellationToken);
            return await executeTask;
        }

        protected virtual async Task<IDataReader> ExecuteReaderAsync(IDbCommand command, CancellationToken cancellationToken)
        {
            var readerTask = Task.Run(() => command.ExecuteReader(), cancellationToken);
            return await readerTask;
        }

        protected virtual async Task<object> ExecuteScalarAsync(IDbCommand command, CancellationToken cancellationToken)
        {
            var scalarTask = Task.Run(() => command.ExecuteScalar(), cancellationToken);
            return await scalarTask;
        }

        protected virtual async Task<List<string>> GetTableListAsync(IDbConnection connection, string provider)
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

        protected virtual async Task<List<DataField>> GetTableSchemaAsync(IDbConnection connection, string tableName, string provider)
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

        protected virtual async Task<bool> CheckTableExistsAsync(IDbConnection connection, string tableName, string provider)
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

        protected virtual async Task<bool> TestWritePermissionAsync(IDbConnection connection, string tableName, string provider)
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

        protected virtual async Task<int> WriteBatchAsync(
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

        protected virtual string BuildInsertStatement(string tableName, List<string> columns, string provider, int rowCount)
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

        protected virtual async Task<bool> CreateTableFromDataAsync(
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

        protected virtual async Task<bool> CreateTableFromSchemaAsync(
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

        protected virtual string AddPaginationToQuery(string query, int offset, int limit, string provider)
        {
            return provider?.ToLower() switch
            {
                "postgresql" or "postgres" or "mysql" =>
                    $"{query} LIMIT {limit} OFFSET {offset}",
                _ =>
                    $"{query} OFFSET {offset} ROWS FETCH NEXT {limit} ROWS ONLY"
            };
        }

        protected virtual string ExtractTableNameFromQuery(string query)
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

        protected virtual DataType MapDbTypeToDataType(string dbType)
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

        protected virtual string GetSqlType(object value, string provider)
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

        protected virtual string GetSqlTypeFromDataType(DataType dataType, string provider)
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
    }
}
