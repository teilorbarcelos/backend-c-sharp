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
            var originalEnv = Environment.GetEnvironmentVariable("ENVIRONMENT");
            var originalDisable = Environment.GetEnvironmentVariable("DISABLE_RATE_LIMIT");
            
            try
            {
                // Enable rate limit for this test
                Environment.SetEnvironmentVariable("ENVIRONMENT", "Production");
                Environment.SetEnvironmentVariable("DISABLE_RATE_LIMIT", "false");

                // Hit the limit (100 requests)
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
    }
}
