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
        JsonFile = 3,
        Database = 4,
        Ftp = 5,
        CsvFile = 6,
        ExcelFile = 7,

        // Destination Adapter'lar (Veri Hedefleri)
        JsonWriter = 8,
        DatabaseWriter = 9,
        FtpWriter = 10,
        CsvWriter = 11,
        ExcelWriter = 12,

        // Transform Adapter'lar
        DataMapper = 13,
        ScriptEngine = 14,
        Aggregator = 15,
        Filter = 16,
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
