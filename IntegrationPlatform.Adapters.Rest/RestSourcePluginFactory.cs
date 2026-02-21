using IntegrationPlatform.Common.Interfaces.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IntegrationPlatform.Adapters.Rest
{
    public static class RestSourcePluginFactory
    {
        public static ISourcePlugin Create(IServiceProvider serviceProvider)
        {
            var logger = serviceProvider.GetRequiredService<ILogger<RestSourcePlugin>>();
            return new RestSourcePlugin(logger);
        }
    }
}
