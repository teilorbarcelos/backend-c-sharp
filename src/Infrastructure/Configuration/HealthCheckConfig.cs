using System.Diagnostics.CodeAnalysis;
using System.Net.Mime;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MageBackend.Infrastructure.HealthChecks;

namespace MageBackend.Infrastructure.Configuration
{
    public static class HealthCheckConfig
    {
        public static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private static readonly string[] RabbitMqTags = ["rabbitmq"];
        private static readonly string[] PdfTags = ["pdf"];

        [ExcludeFromCodeCoverage]
        public static IServiceCollection AddAppHealthChecks(this IServiceCollection services)
        {
            services.AddHealthChecks()
                .AddCheck<SqlHealthCheck>("sql")
                .AddCheck<RedisHealthCheck>("redis")
                .AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: RabbitMqTags)
                .AddCheck<PdfHealthCheck>("pdf", tags: PdfTags);

            return services;
        }

        [ExcludeFromCodeCoverage]
        public static void MapAppHealthChecks(this WebApplication app)
        {
            app.MapHealthChecks("/health", new HealthCheckOptions
            {
                Predicate = _ => true,
                ResponseWriter = WriteHealthResponse,
                ResultStatusCodes =
                {
                    [HealthStatus.Healthy] = StatusCodes.Status200OK,
                    [HealthStatus.Degraded] = StatusCodes.Status200OK,
                    [HealthStatus.Unhealthy] = StatusCodes.Status200OK
                }
            });
        }

        [ExcludeFromCodeCoverage]
        public static async Task<IResult> RunHealthChecksAsync(HttpContext http)
        {
            var healthCheckService = http.RequestServices.GetRequiredService<HealthCheckService>();
            HealthReport report;

            try
            {
                report = await healthCheckService.CheckHealthAsync();
            }
            catch (Exception)
            {
                var errorResponse = new
                {
                    status = "error",
                    timestamp = DateTime.UtcNow.ToString("o"),
                    checks = Array.Empty<object>()
                };
                return Results.Json(errorResponse, JsonOptions, statusCode: 200);
            }

            return Results.Json(SerializeReport(report), JsonOptions, statusCode: 200);
        }

        internal static object SerializeReport(HealthReport report)
        {
            var statusText = report.Status == HealthStatus.Unhealthy ? "error" : "ok";

            var checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString().ToLowerInvariant(),
                description = e.Value.Description,
                duration = e.Value.Duration.TotalMilliseconds
            });

            return new
            {
                status = statusText,
                timestamp = DateTime.UtcNow.ToString("o"),
                checks
            };
        }

        [ExcludeFromCodeCoverage]
        internal static Task WriteHealthResponse(HttpContext context, HealthReport report)
        {
            context.Response.ContentType = MediaTypeNames.Application.Json;
            return context.Response.WriteAsync(JsonSerializer.Serialize(SerializeReport(report), JsonOptions));
        }
    }
}
