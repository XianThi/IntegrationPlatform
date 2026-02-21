using IntegrationPlatform.Common.Enums;
using IntegrationPlatform.Common.Interfaces.Plugins;

namespace IntegrationPlatform.API.Services
{
    public interface ITestService
    {
        Task<SourceTestResult> TestSourceAsync(AdapterType adapterType, Dictionary<string, object> configuration);
        Task<DestinationTestResult> TestDestinationAsync(AdapterType adapterType, Dictionary<string, object> configuration);
    }
}
