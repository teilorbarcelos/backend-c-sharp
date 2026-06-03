using System;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace MageBackend.Tests
{
    public class RateLimitTests : IntegrationTestBase
    {
        public RateLimitTests(IntegrationTestFixture fixture) : base(fixture) { }

        [Fact]
        public async Task GivenStandardIp_WhenLimitReached_ThenReturnsTooManyRequests()
        {
            var cacheField = typeof(MageBackend.Core.Middleware.RateLimitMiddleware)
                .GetField("_cache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (cacheField != null)
            {
                var cache = (System.Collections.Concurrent.ConcurrentDictionary<string, (int Count, DateTime WindowStart)>)cacheField.GetValue(null)!;
                cache.Clear();
            }

            var originalEnv = Environment.GetEnvironmentVariable("ENVIRONMENT");
            var originalDisable = Environment.GetEnvironmentVariable("DISABLE_RATE_LIMIT");

            try
            {
                Environment.SetEnvironmentVariable("ENVIRONMENT", "Production");
                Environment.SetEnvironmentVariable("DISABLE_RATE_LIMIT", "false");

                for (int i = 0; i < 101; i++)
                {
                    var resp = await _client.GetAsync("/health");

                    if (i < 100)
                    {
                        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                        Assert.True(resp.Headers.Contains("x-ratelimit-remaining"));
                    }
                    else
                    {
                        Assert.Equal(HttpStatusCode.TooManyRequests, resp.StatusCode);
                    }
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("ENVIRONMENT", originalEnv);
                Environment.SetEnvironmentVariable("DISABLE_RATE_LIMIT", originalDisable);
            }
        }

        [Fact]
        public async Task GivenExistingCacheEntryInPast_WhenRequestMade_ThenResetsWindow()
        {
            var originalEnv = Environment.GetEnvironmentVariable("ENVIRONMENT");
            var originalDisable = Environment.GetEnvironmentVariable("DISABLE_RATE_LIMIT");

            try
            {
                var field = typeof(MageBackend.Core.Middleware.RateLimitMiddleware)
                    .GetField("_cache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                Assert.NotNull(field);

                var cache = (System.Collections.Concurrent.ConcurrentDictionary<string, (int Count, DateTime WindowStart)>)field.GetValue(null)!;
                cache.Clear();

                var pastTime = DateTime.UtcNow.AddMinutes(-2);
                cache["127.0.0.1"] = (50, pastTime);
                cache["::1"] = (50, pastTime);
                cache["localhost"] = (50, pastTime);

                Environment.SetEnvironmentVariable("ENVIRONMENT", "Production");
                Environment.SetEnvironmentVariable("DISABLE_RATE_LIMIT", "false");

                var resp = await _client.GetAsync("/health");
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

                if (resp.Headers.TryGetValues("x-ratelimit-remaining", out var values))
                {
                    var remaining = string.Join("", values);
                    Assert.Equal("99", remaining);
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("ENVIRONMENT", originalEnv);
                Environment.SetEnvironmentVariable("DISABLE_RATE_LIMIT", originalDisable);
            }
        }
    }
}
