using IntegrationPlatform.Common.Interfaces.Plugins;
using System.Text.Json;

namespace IntegrationPlatform.Common.Helpers
{
    public static class SchemaDetector
    {
        public static DataSchema DetectSchema(object data)
        {
            if (data == null)
                return new DataSchema { Fields = new List<DataField>() };

            var schema = new DataSchema
            {
                Fields = new List<DataField>()
            };

            // JSON element ise
            if (data is JsonElement jsonElement)
            {
                DetectFromJsonElement(jsonElement, schema);
            }
            // Dictionary ise
            else if (data is Dictionary<string, object> dict)
            {
                DetectFromDictionary(dict, schema);
            }
            // Liste ise
            else if (data is IEnumerable<object> list && list.Any())
            {
                var firstItem = list.First();
                if (firstItem != null)
                    schema = DetectSchema(firstItem);

                schema.Metadata = new Dictionary<string, object>
                {
                    ["is_array"] = true,
                    ["item_count"] = list.Count()
                };
            }
            // Primitive type ise
            else
            {
                schema.Fields.Add(new DataField
                {
                    Name = "value",
                    Type = MapType(data.GetType()),
                    DefaultValue = data
                });
            }

            return schema;
        }

        private static void DetectFromJsonElement(JsonElement element, DataSchema schema)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        var field = new DataField
                        {
                            Name = property.Name,
                            Type = MapJsonValueKind(property.Value.ValueKind),
                            IsNullable = property.Value.ValueKind == JsonValueKind.Null
                        };

                        // Örnek değer ekle (çok büyük değilse)
                        if (property.Value.ValueKind != JsonValueKind.Object &&
                            property.Value.ValueKind != JsonValueKind.Array)
                        {
                            field.DefaultValue = ConvertJsonElement(property.Value);
                        }

                        schema.Fields.Add(field);
                    }
                    break;

                case JsonValueKind.Array:
                    schema.Metadata = new Dictionary<string, object>
                    {
                        ["is_array"] = true
                    };

                    if (element.GetArrayLength() > 0)
                    {
                        var firstItem = element.EnumerateArray().First();
                        var itemSchema = DetectSchema(firstItem);
                        schema.Fields = itemSchema.Fields;
                    }
                    break;
            }
        }

        private static void DetectFromDictionary(Dictionary<string, object> dict, DataSchema schema)
        {
            foreach (var kvp in dict)
            {
                var field = new DataField
                {
                    Name = kvp.Key,
                    Type = MapType(kvp.Value?.GetType() ?? typeof(object)),
                    DefaultValue = kvp.Value,
                    IsNullable = kvp.Value == null
                };
                schema.Fields.Add(field);
            }
        }

        private static DataType MapJsonValueKind(JsonValueKind kind)
        {
            return kind switch
            {
                JsonValueKind.String => DataType.String,
                JsonValueKind.Number => DataType.Decimal,
                JsonValueKind.True or JsonValueKind.False => DataType.Boolean,
                JsonValueKind.Object => DataType.Object,
                JsonValueKind.Array => DataType.Array,
                JsonValueKind.Null => DataType.String,
                _ => DataType.String
            };
        }

        private static DataType MapType(Type type)
        {
            if (type == typeof(string)) return DataType.String;
            if (type == typeof(int) || type == typeof(short) || type == typeof(byte)) return DataType.Integer;
            if (type == typeof(long)) return DataType.Long;
            if (type == typeof(decimal) || type == typeof(double) || type == typeof(float)) return DataType.Decimal;
            if (type == typeof(bool)) return DataType.Boolean;
            if (type == typeof(DateTime)) return DataType.DateTime;
            if (type == typeof(Guid)) return DataType.Guid;
            if (type == typeof(byte[])) return DataType.Binary;
            if (type.IsArray) return DataType.Array;
            if (type.IsClass) return DataType.Object;

            return DataType.String;
        }

        private static object ConvertJsonElement(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.TryGetInt64(out long l) ? l : element.GetDecimal(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => element.ToString()
            };
        }

        // İki şemayı karşılaştır ve mapping önerileri üret
        public static Dictionary<string, string> SuggestMapping(DataSchema sourceSchema, DataSchema targetSchema)
        {
            var suggestions = new Dictionary<string, string>();

            foreach (var sourceField in sourceSchema.Fields)
            {
                // Aynı isimde alan bul
                var targetField = targetSchema.Fields
                    .FirstOrDefault(f => f.Name.Equals(sourceField.Name, StringComparison.OrdinalIgnoreCase));

                if (targetField != null && AreTypesCompatible(sourceField.Type, targetField.Type))
                {
                    suggestions[sourceField.Name] = targetField.Name;
                }
                else
                {
                    // Benzer isimli alanları bul
                    targetField = targetSchema.Fields
                        .FirstOrDefault(f =>
                            f.Name.Contains(sourceField.Name, StringComparison.OrdinalIgnoreCase) ||
                            sourceField.Name.Contains(f.Name, StringComparison.OrdinalIgnoreCase));

                    if (targetField != null && AreTypesCompatible(sourceField.Type, targetField.Type))
                    {
                        suggestions[sourceField.Name] = targetField.Name;
                    }
                }
            }

            return suggestions;
        }

        private static bool AreTypesCompatible(DataType source, DataType target)
        {
            // Basit tip uyumluluk kontrolü
            if (source == target) return true;

            // Numeric tipler birbiriyle uyumlu
            if ((source == DataType.Integer || source == DataType.Long || source == DataType.Decimal) &&
                (target == DataType.Integer || target == DataType.Long || target == DataType.Decimal))
                return true;

            // String her şey olabilir
            if (source == DataType.String) return true;

            return false;
        }
    }
}
