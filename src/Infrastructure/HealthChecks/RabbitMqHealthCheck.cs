using Microsoft.Extensions.Diagnostics.HealthChecks;
using MageBackend.Infrastructure.Messaging;

namespace MageBackend.Infrastructure.HealthChecks
{
    public class RabbitMqHealthCheck : IHealthCheck
    {
        private readonly RabbitMQProvider _provider;

        public RabbitMqHealthCheck(RabbitMQProvider provider)
        {
            _provider = provider;
        }

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                _provider.Publish("healthcheck", new { timestamp = DateTime.UtcNow });
                return Task.FromResult(HealthCheckResult.Healthy());
            }
            catch (Exception ex)
            {
                return Task.FromResult(HealthCheckResult.Degraded("RabbitMQ is unreachable", ex));
            }
        }
    }
}
