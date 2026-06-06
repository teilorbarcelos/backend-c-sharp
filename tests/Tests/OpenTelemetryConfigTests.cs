using System;
using MageBackend.Infrastructure.Configuration;
using Xunit;

namespace MageBackend.Tests
{
    public class OpenTelemetryConfigTests : IDisposable
    {
        public OpenTelemetryConfigTests()
        {
            Environment.SetEnvironmentVariable(OpenTelemetryConfig.OtelEnabledEnvVar, null);
            Environment.SetEnvironmentVariable(OpenTelemetryConfig.OtlpEndpointEnvVar, null);
            Environment.SetEnvironmentVariable(OpenTelemetryConfig.ServiceNameEnvVar, null);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(OpenTelemetryConfig.OtelEnabledEnvVar, null);
            Environment.SetEnvironmentVariable(OpenTelemetryConfig.OtlpEndpointEnvVar, null);
            Environment.SetEnvironmentVariable(OpenTelemetryConfig.ServiceNameEnvVar, null);
            GC.SuppressFinalize(this);
        }

        [Fact]
        public void GivenNoEnvVar_WhenIsEnabled_ThenReturnsTrue()
        {
            Assert.True(OpenTelemetryConfig.IsEnabled());
        }

        [Fact]
        public void GivenOtelEnabledTrue_WhenIsEnabled_ThenReturnsTrue()
        {
            Environment.SetEnvironmentVariable(OpenTelemetryConfig.OtelEnabledEnvVar, "true");
            Assert.True(OpenTelemetryConfig.IsEnabled());
        }

        [Fact]
        public void GivenOtelEnabledFalse_WhenIsEnabled_ThenReturnsFalse()
        {
            Environment.SetEnvironmentVariable(OpenTelemetryConfig.OtelEnabledEnvVar, "false");
            Assert.False(OpenTelemetryConfig.IsEnabled());
        }

        [Fact]
        public void GivenOtelEnabledInvalid_WhenIsEnabled_ThenReturnsTrue()
        {
            Environment.SetEnvironmentVariable(OpenTelemetryConfig.OtelEnabledEnvVar, "not-a-bool");
            Assert.True(OpenTelemetryConfig.IsEnabled());
        }

        [Fact]
        public void GivenNoEndpoint_WhenGetOtlpEndpoint_ThenReturnsDefault()
        {
            Assert.Equal(OpenTelemetryConfig.DefaultOtlpEndpoint, OpenTelemetryConfig.GetOtlpEndpoint());
        }

        [Fact]
        public void GivenCustomEndpoint_WhenGetOtlpEndpoint_ThenReturnsCustom()
        {
            Environment.SetEnvironmentVariable(OpenTelemetryConfig.OtlpEndpointEnvVar, "http://otel-collector:4317");
            Assert.Equal("http://otel-collector:4317", OpenTelemetryConfig.GetOtlpEndpoint());
        }

        [Fact]
        public void GivenCustomEndpointWithHttps_WhenGetOtlpEndpoint_ThenReturnsCustom()
        {
            Environment.SetEnvironmentVariable(OpenTelemetryConfig.OtlpEndpointEnvVar, "https://api.otel.example.com:443");
            Assert.Equal("https://api.otel.example.com:443", OpenTelemetryConfig.GetOtlpEndpoint());
        }

        [Fact]
        public void GivenEmptyEndpoint_WhenGetOtlpEndpoint_ThenReturnsDefault()
        {
            Environment.SetEnvironmentVariable(OpenTelemetryConfig.OtlpEndpointEnvVar, "");
            Assert.Equal(OpenTelemetryConfig.DefaultOtlpEndpoint, OpenTelemetryConfig.GetOtlpEndpoint());
        }

        [Fact]
        public void GivenWhitespaceEndpoint_WhenGetOtlpEndpoint_ThenReturnsDefault()
        {
            Environment.SetEnvironmentVariable(OpenTelemetryConfig.OtlpEndpointEnvVar, "   ");
            Assert.Equal(OpenTelemetryConfig.DefaultOtlpEndpoint, OpenTelemetryConfig.GetOtlpEndpoint());
        }

        [Fact]
        public void GivenNoServiceName_WhenGetServiceName_ThenReturnsDefault()
        {
            Assert.Equal(OpenTelemetryConfig.DefaultServiceName, OpenTelemetryConfig.GetServiceName());
        }

        [Fact]
        public void GivenCustomServiceName_WhenGetServiceName_ThenReturnsCustom()
        {
            Environment.SetEnvironmentVariable(OpenTelemetryConfig.ServiceNameEnvVar, "MyCustomApp");
            Assert.Equal("MyCustomApp", OpenTelemetryConfig.GetServiceName());
        }

        [Fact]
        public void GivenEmptyServiceName_WhenGetServiceName_ThenReturnsDefault()
        {
            Environment.SetEnvironmentVariable(OpenTelemetryConfig.ServiceNameEnvVar, "");
            Assert.Equal(OpenTelemetryConfig.DefaultServiceName, OpenTelemetryConfig.GetServiceName());
        }

        [Fact]
        public void GivenServiceNameEnvVar_WhenInspected_ThenHasExpectedConstants()
        {
            Assert.Equal("OTEL_ENABLED", OpenTelemetryConfig.OtelEnabledEnvVar);
            Assert.Equal("OTEL_EXPORTER_OTLP_ENDPOINT", OpenTelemetryConfig.OtlpEndpointEnvVar);
            Assert.Equal("OTEL_SERVICE_NAME", OpenTelemetryConfig.ServiceNameEnvVar);
            Assert.Equal("http://localhost:4317", OpenTelemetryConfig.DefaultOtlpEndpoint);
            Assert.Equal("MageBackend", OpenTelemetryConfig.DefaultServiceName);
        }
    }
}
