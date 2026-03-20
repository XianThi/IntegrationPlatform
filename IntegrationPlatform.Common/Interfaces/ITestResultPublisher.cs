using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IntegrationPlatform.Common.Interfaces
{
    public interface ITestResultPublisher
    {
        Task PublishResultAsync<T>(string requestId, T result) where T : class;
    }
}
