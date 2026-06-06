using Microsoft.Extensions.Diagnostics.HealthChecks;
using MageBackend.Database;

namespace MageBackend.Infrastructure.HealthChecks
{
    public class SqlHealthCheck : IHealthCheck
    {
        private readonly ApplicationDbContext _context;

        public SqlHealthCheck(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                var canConnect = await _context.Database.CanConnectAsync(cancellationToken);
                return canConnect
                    ? HealthCheckResult.Healthy()
                    : HealthCheckResult.Unhealthy("SQL Server connection failed");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("SQL Server is unreachable", ex);
            }
        }
    }
}
