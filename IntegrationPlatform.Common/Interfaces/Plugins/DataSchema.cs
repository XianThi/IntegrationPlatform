namespace IntegrationPlatform.Common.Interfaces.Plugins
{
    public class DataSchema
    {
        public string Name { get; set; }
        public List<DataField> Fields { get; set; }
        public Dictionary<string, object> Metadata { get; set; }
    }

    public class DataField
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public DataType Type { get; set; }
        public bool IsNullable { get; set; }
        public bool IsKey { get; set; }
        public int? MaxLength { get; set; }
        public object DefaultValue { get; set; }
        public string Format { get; set; } // Tarih formatı, sayı formatı vb.
        public List<string> PossibleValues { get; set; } // Enum için
        public Dictionary<string, object> Metadata { get; set; }
    }

    public enum DataType
    {
        String = 1,
        Integer = 2,
        Long = 3,
        Decimal = 4,
        Boolean = 5,
        DateTime = 6,
        Date = 7,
        Time = 8,
        Guid = 9,
        Binary = 10,
        Array = 11,
        Object = 12
    }
}
