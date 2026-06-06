using Microsoft.Extensions.Diagnostics.HealthChecks;
using MageBackend.Infrastructure.Auth;

namespace MageBackend.Infrastructure.HealthChecks
{
    public class RedisHealthCheck : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                var db = RedisProvider.Connection.GetDatabase();
                await db.PingAsync();
                return HealthCheckResult.Healthy();
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Redis is unreachable", ex);
            }
        }
    }
}
