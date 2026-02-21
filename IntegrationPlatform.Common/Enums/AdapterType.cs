namespace IntegrationPlatform.Common.Enums
{
    /// <summary>
    /// Adapter'ın veri akışındaki yönü
    /// </summary>
    public enum AdapterDirection
    {
        Source = 1,      // Veri kaynağı (okuma)
        Transform = 2,    // Veri dönüşümü
        Destination = 3   // Veri hedefi (yazma)
    }
    /// <summary>
    /// Desteklenen adapter tipleri
    /// </summary>
    public enum AdapterType
    {
        // Source Adapter'lar (Veri Kaynakları)
        Rest = 1,
        Soap = 2,
        Database = 3,
        Ftp = 4,

        // File Source'lar
        JsonFile = 5,
        CsvFile = 6,
        XmlFile = 7,
        ExcelFile = 8,

        // Transform Adapter'lar
        DataMapper = 9,
        ScriptEngine = 10,
        Aggregator = 11,
        Filter = 12,

        // Destination Adapter'lar (Veri Hedefleri)
        DatabaseWriter = 13,
        FtpWriter = 14,

        // File Destination'lar
        JsonWriter = 15,
        CsvWriter = 16,
        XmlWriter = 17,
        ExcelWriter = 18
    }

    public enum WorkflowStatus
    {
        Idle = 0,
        Running = 1,
        Paused = 2,
        Stopped = 3,
        Completed = 4,
        Failed = 5
    }

    public enum NodeStatus
    {
        Online = 1,
        Offline = 2,
        Busy = 3,
        Maintenance = 4
    }
}
