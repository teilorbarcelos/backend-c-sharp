using System;
using System.Diagnostics.CodeAnalysis;

namespace MageBackend.Infrastructure.Configuration
{
    public static class OpenTelemetryConfig
    {
        public const string OtelEnabledEnvVar = "OTEL_ENABLED";
        public const string OtlpEndpointEnvVar = "OTEL_EXPORTER_OTLP_ENDPOINT";
        public const string ServiceNameEnvVar = "OTEL_SERVICE_NAME";

        public const string DefaultOtlpEndpoint = "http://localhost:4317";
        public const string DefaultServiceName = "MageBackend";

        public static bool IsEnabled()
        {
            var raw = Environment.GetEnvironmentVariable(OtelEnabledEnvVar);
            if (string.IsNullOrEmpty(raw)) return true;

            return !bool.TryParse(raw, out var val) || val;
        }

        public static string GetOtlpEndpoint()
        {
            var raw = Environment.GetEnvironmentVariable(OtlpEndpointEnvVar);
            return !string.IsNullOrWhiteSpace(raw) ? raw : DefaultOtlpEndpoint;
        }

        public static string GetServiceName()
        {
            var raw = Environment.GetEnvironmentVariable(ServiceNameEnvVar);
            return !string.IsNullOrWhiteSpace(raw) ? raw : DefaultServiceName;
        }
    }
}
