using IntegrationPlatform.Common.Enums;
using IntegrationPlatform.Common.Interfaces.Plugins;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IntegrationPlatform.Adapters.Json
{
    public class JsonTransformPlugin : JsonPluginBase, ITransformPlugin
    {
        public override string Id => "IntegrationPlatform.Adapters.Json.Transform";
        public override string Name => "JSON Transformer";
        public override string Description => "JSON verilerini dönüştürür";
        public override AdapterDirection Direction => AdapterDirection.Transform;
        public override AdapterType Type => AdapterType.DataMapper;

        public JsonTransformPlugin() : base() { }
        public JsonTransformPlugin(ILogger<JsonTransformPlugin> logger) : base(logger) { }

        // ITransformPlugin implementasyonları...
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

        public async Task<Dictionary<string, string>> GetMappingSchemaAsync()
        {
            return new Dictionary<string, string>();
        }

        #endregion
    }
}
