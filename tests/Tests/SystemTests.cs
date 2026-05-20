using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MageBackend.Database;
using MageBackend.Features.Auth;
using MageBackend.Features.User;
using MageBackend.Features.Role;
using MageBackend.Features.Product;
using MageBackend.Infrastructure.Auth;
using Xunit;

namespace MageBackend.Tests
{
    public class SystemTests : IntegrationTestBase
    {
        public SystemTests(IntegrationTestFixture fixture) : base(fixture) { }

        // ==========================================
        // --- 07. Observability (2 tests) ----------
        // ==========================================

        [Fact]
        public async Task GivenHealthEndpoint_WhenAccessed_ThenReturnsHealthy()
        {
            var healthResp = await _client.GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, healthResp.StatusCode);
            var healthData = await healthResp.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("UP", healthData.GetProperty("status").GetString());
        }

        [Fact]
        public async Task GivenMetricsEndpoint_WhenAccessed_ThenReturnsPrometheusData()
        {
            var metricsResp = await _client.GetAsync("/metrics");
            Assert.Equal(HttpStatusCode.OK, metricsResp.StatusCode);
            var metricsText = await metricsResp.Content.ReadAsStringAsync();
            Assert.Contains("http_request_duration_seconds", metricsText);
        }

        // ==========================================
        // --- 08. Rate Limit (1 test) --------------
        // ==========================================

        [Fact]
        public async Task GivenManyRequests_WhenLimitExceeded_ThenReturnsRateLimitHeaders()
        {
            var resp = await _client.GetAsync("/health");
            Assert.True(resp.Headers.Contains("x-ratelimit-limit"), "Missing x-ratelimit-limit header");
            Assert.True(resp.Headers.Contains("x-ratelimit-remaining"), "Missing x-ratelimit-remaining header");
        }

        // ==========================================
        // --- 12. Error Logs (1 test) --------------
        // ==========================================

        [Fact]
        public async Task GivenUnhandledException_WhenOccurs_ThenLogsToDatabase()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            // Send payload with missing name to trigger a ValidationException in RoleController
            var resp = await _client.PostAsJsonAsync("/v1/role", new { description = "Triggering validation error" });
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

            await Task.Delay(600);
            using (var scope = _fixture.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var errorLog = await dbContext.ErrorLog
                    .FirstOrDefaultAsync(e => e.IdUser == loginData.User.Id && e.Source.Contains("POST /v1/role"));
                Assert.NotNull(errorLog);
                Assert.NotNull(errorLog.ErrorMessage);
                Assert.Contains("validation", errorLog.ErrorMessage.ToLower());
            }

            ClearAuthHeader();
        }

        // ==========================================
        // --- 13. PDF Debug (1 test) ---------------
        // ==========================================

        [Fact]
        public async Task GivenPdfDebugEndpoint_WhenAccessed_ThenReturnsValidPdf()
        {
            var resp = await _client.GetAsync("/v1/debug/pdf");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            Assert.Equal("application/pdf", resp.Content.Headers.ContentType?.MediaType);
        }

        
    }
}