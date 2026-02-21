namespace IntegrationPlatform.Adapters.Json
{
    public class WriteTestResult
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; }
        public bool CanWrite { get; set; }
        public bool TargetFileExists { get; set; }
        public long TargetFileSize { get; set; }
        public DateTime? TargetFileModified { get; set; }
        public long TestFileSize { get; set; }
        public List<string> Warnings { get; set; } = new();
    }
}
