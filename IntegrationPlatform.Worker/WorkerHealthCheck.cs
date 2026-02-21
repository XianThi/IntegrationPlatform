namespace IntegrationPlatform.Worker
{
    public class WorkerHealthCheck : Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck
    {
        private readonly ILogger<WorkerHealthCheck> _logger;

        public WorkerHealthCheck(ILogger<WorkerHealthCheck> logger)
        {
            _logger = logger;
        }

        public Task<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult> CheckHealthAsync(
            Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            // Worker'ın sağlık durumunu kontrol et
            // Örnek: Disk alanı, bellek kullanımı vs.

            var memoryUsage = GC.GetGCMemoryInfo().MemoryLoadBytes / (1024.0 * 1024.0);
            var isHealthy = memoryUsage < 1024; // 1GB'dan az mı?

            if (isHealthy)
            {
                return Task.FromResult(Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("Worker sağlıklı"));
            }
            else
            {
                return Task.FromResult(Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Degraded("Yüksek bellek kullanımı"));
            }
        }
    }
}
