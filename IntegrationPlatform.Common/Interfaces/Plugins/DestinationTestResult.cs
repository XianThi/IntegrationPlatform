namespace IntegrationPlatform.Common.Interfaces.Plugins
{
    public class DestinationTestResult
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; }
        public bool CanWrite { get; set; }
        public bool DestinationExists { get; set; }
        public Dictionary<string, object> Details { get; set; }
        public List<string> Warnings { get; set; } = new();
        public List<string> Errors { get; set; } = new();
    }
}
