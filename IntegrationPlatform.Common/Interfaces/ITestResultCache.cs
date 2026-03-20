using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IntegrationPlatform.Common.Interfaces
{
    public interface ITestResultCache
    {
        Task<T> WaitForResultAsync<T>(string requestId, TimeSpan timeout) where T : class;
        void StoreResult<T>(string requestId, T result) where T : class;
    }
}
