using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MageBackend.Infrastructure.HealthChecks
{
    public class PdfHealthCheck : IHealthCheck
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _pdfServiceUrl;

        public PdfHealthCheck(IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _httpClientFactory = httpClientFactory;
            _pdfServiceUrl = configuration["PDF_SERVICE_URL"] ?? "http://localhost:8889";
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                using var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(3);

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(3));
                var response = await client.GetAsync($"{_pdfServiceUrl}/health", cts.Token);
                return response.IsSuccessStatusCode
                    ? HealthCheckResult.Healthy()
                    : HealthCheckResult.Degraded($"PDF service returned {response.StatusCode}");
            }
            catch (TaskCanceledException)
            {
                return HealthCheckResult.Degraded("PDF service timed out");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Degraded("PDF service is unreachable", ex);
            }
        }
    }
}
