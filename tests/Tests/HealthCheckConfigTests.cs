using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MageBackend.Infrastructure.Configuration;
using Xunit;

namespace MageBackend.Tests
{
    public class HealthCheckConfigTests
    {
        [Fact]
        public void GivenServiceCollection_WhenAddAppHealthChecks_ThenRegistersHealthCheckService()
        {
            var services = new ServiceCollection();
            services.AddLogging();

            services.AddAppHealthChecks();

            var provider = services.BuildServiceProvider();
            var healthCheckService = provider.GetService<HealthCheckService>();
            Assert.NotNull(healthCheckService);
        }

        [Fact]
        public void GivenHealthyReport_WhenSerializeReport_ThenOkStatus()
        {
            var report = new HealthReport(
                new Dictionary<string, HealthReportEntry>(),
                HealthStatus.Healthy,
                TimeSpan.Zero);

            var result = HealthCheckConfig.SerializeReport(report);
            var json = JsonSerializer.Serialize(result, HealthCheckConfig.JsonOptions);
            var doc = JsonDocument.Parse(json);

            Assert.Equal("ok", doc.RootElement.GetProperty("status").GetString());
            Assert.True(doc.RootElement.TryGetProperty("timestamp", out _));
            Assert.True(doc.RootElement.TryGetProperty("checks", out _));
        }

        [Fact]
        public void GivenDegradedReport_WhenSerializeReport_ThenOkStatus()
        {
            var report = new HealthReport(
                new Dictionary<string, HealthReportEntry>(),
                HealthStatus.Degraded,
                TimeSpan.Zero);

            var result = HealthCheckConfig.SerializeReport(report);
            var json = JsonSerializer.Serialize(result, HealthCheckConfig.JsonOptions);
            var doc = JsonDocument.Parse(json);

            Assert.Equal("ok", doc.RootElement.GetProperty("status").GetString());
        }

        [Fact]
        public void GivenUnhealthyReport_WhenSerializeReport_ThenErrorStatus()
        {
            var report = new HealthReport(
                new Dictionary<string, HealthReportEntry>(),
                HealthStatus.Unhealthy,
                TimeSpan.Zero);

            var result = HealthCheckConfig.SerializeReport(report);
            var json = JsonSerializer.Serialize(result, HealthCheckConfig.JsonOptions);
            var doc = JsonDocument.Parse(json);

            Assert.Equal("error", doc.RootElement.GetProperty("status").GetString());
        }

        [Fact]
        public void GivenReportWithEntry_WhenSerializeReport_ThenIncludesCheckDetails()
        {
            var report = new HealthReport(
                new Dictionary<string, HealthReportEntry>
                {
                    ["sql"] = new(HealthStatus.Healthy, "All good", TimeSpan.FromMilliseconds(5), null, null)
                },
                HealthStatus.Healthy,
                TimeSpan.FromMilliseconds(5));

            var result = HealthCheckConfig.SerializeReport(report);
            var json = JsonSerializer.Serialize(result, HealthCheckConfig.JsonOptions);
            var doc = JsonDocument.Parse(json);

            var checks = doc.RootElement.GetProperty("checks").EnumerateArray().ToList();
            var check = checks[0];

            Assert.Equal("sql", check.GetProperty("name").GetString());
            Assert.Equal("healthy", check.GetProperty("status").GetString());
            Assert.Equal("All good", check.GetProperty("description").GetString());
            Assert.True(check.GetProperty("duration").GetDouble() > 0);
        }

        [Fact]
        public void GivenReportWithAllDependencies_WhenSerializeReport_ThenAllFourChecksIncluded()
        {
            var entries = new Dictionary<string, HealthReportEntry>
            {
                ["sql"] = new(HealthStatus.Healthy, "", TimeSpan.Zero, null, null),
                ["redis"] = new(HealthStatus.Healthy, "", TimeSpan.Zero, null, null),
                ["rabbitmq"] = new(HealthStatus.Healthy, "", TimeSpan.Zero, null, null),
                ["pdf"] = new(HealthStatus.Healthy, "", TimeSpan.Zero, null, null)
            };

            var result = HealthCheckConfig.SerializeReport(
                new HealthReport(entries, HealthStatus.Healthy, TimeSpan.Zero));
            var json = JsonSerializer.Serialize(result, HealthCheckConfig.JsonOptions);
            var doc = JsonDocument.Parse(json);

            var checkNames = doc.RootElement.GetProperty("checks")
                .EnumerateArray()
                .Select(c => c.GetProperty("name").GetString())
                .ToHashSet();

            Assert.Equal(4, checkNames.Count);
            Assert.Contains("sql", checkNames);
            Assert.Contains("redis", checkNames);
            Assert.Contains("rabbitmq", checkNames);
            Assert.Contains("pdf", checkNames);
        }

        [Fact]
        public void GivenReportWithTimestamp_WhenSerializeReport_ThenTimestampIsIsoFormat()
        {
            var report = new HealthReport(
                new Dictionary<string, HealthReportEntry>(),
                HealthStatus.Healthy,
                TimeSpan.Zero);

            var result = HealthCheckConfig.SerializeReport(report);
            var json = JsonSerializer.Serialize(result, HealthCheckConfig.JsonOptions);
            var doc = JsonDocument.Parse(json);
            var timestamp = doc.RootElement.GetProperty("timestamp").GetString();

            Assert.NotNull(timestamp);
            Assert.Contains("T", timestamp);
            Assert.Contains("Z", timestamp);
            Assert.True(DateTime.TryParse(timestamp, out _));
        }
    }
}
