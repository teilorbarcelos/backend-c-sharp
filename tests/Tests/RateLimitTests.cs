using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using MageBackend.Core.Middleware;
using MageBackend.Infrastructure.Auth;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace MageBackend.Tests
{
    public class RateLimitTests : IntegrationTestBase
    {
        public RateLimitTests(IntegrationTestFixture fixture) : base(fixture) { }

        private static async Task ClearRateLimitKeysAsync()
        {
            var endpoints = RedisProvider.Connection.GetEndPoints();
            var server = RedisProvider.Connection.GetServer(endpoints[0]);
            var db = RedisProvider.Database;
            foreach (var key in server.Keys(pattern: "ratelimit:ip:*"))
            {
                await db.KeyDeleteAsync(key);
            }
        }

        private static EnvVarScope EnableRateLimit()
        {
            return new EnvVarScope(new System.Collections.Generic.Dictionary<string, string?>
            {
                ["DISABLE_RATE_LIMIT"] = "false",
                ["ENVIRONMENT"] = "Production"
            });
        }

        [Fact]
        public async Task GivenStandardIp_WhenLimitReached_ThenReturnsTooManyRequests()
        {
            using var _ = EnableRateLimit();
            await ClearRateLimitKeysAsync();

            try
            {
                for (int i = 0; i < 101; i++)
                {
                    var resp = await _client.GetAsync("/health");

                    if (i < 100)
                    {
                        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                        Assert.True(resp.Headers.Contains("x-ratelimit-remaining"));
                        Assert.True(resp.Headers.Contains("x-ratelimit-reset"));
                    }
                    else
                    {
                        Assert.Equal(HttpStatusCode.TooManyRequests, resp.StatusCode);
                    }
                }
            }
            finally
            {
                await ClearRateLimitKeysAsync();
            }
        }

        [Fact]
        public async Task GivenWindowExpired_WhenRequestMade_ThenResetsCounter()
        {
            using var _ = EnableRateLimit();
            await ClearRateLimitKeysAsync();

            try
            {
                var db = RedisProvider.Database;
                await db.StringSetAsync("ratelimit:ip:127.0.0.1", "50");
                await db.KeyDeleteAsync("ratelimit:ip:127.0.0.1");

                var resp = await _client.GetAsync("/health");
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

                Assert.True(resp.Headers.TryGetValues("x-ratelimit-remaining", out var values));
                Assert.Equal("99", values!.First());
            }
            finally
            {
                await ClearRateLimitKeysAsync();
            }
        }

        [Fact]
        public async Task GivenAnotherReplicaIncremented_WhenLimitReachedFromHttp_ThenSharesCounter()
        {
            using var _ = EnableRateLimit();
            await ClearRateLimitKeysAsync();

            try
            {
                /*
                 * Simula 50 requisições recebidas por "outra réplica" (chama o mesmo script
                 * Lua via TryIncrementAsync). Em seguida, este processo deve respeitar o
                 * contador compartilhado no Redis e bloquear no 51º request HTTP local
                 * (50 da outra réplica + 51 deste = 101 > 100).
                 */
                for (int i = 0; i < 50; i++)
                {
                    await RateLimitMiddleware.TryIncrementAsync("ratelimit:ip:127.0.0.1", 60);
                }

                for (int i = 0; i < 50; i++)
                {
                    var resp = await _client.GetAsync("/health");
                    Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                }

                var blocked = await _client.GetAsync("/health");
                Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
            }
            finally
            {
                await ClearRateLimitKeysAsync();
            }
        }

        [Fact]
        public async Task GivenSuccessfulRequest_WhenHeadersChecked_ThenResetHeaderIsPositive()
        {
            using var _ = EnableRateLimit();
            await ClearRateLimitKeysAsync();

            try
            {
                var resp = await _client.GetAsync("/health");
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

                Assert.True(resp.Headers.TryGetValues("x-ratelimit-reset", out var values));
                var reset = int.Parse(values!.First());
                Assert.InRange(reset, 1, 60);
            }
            finally
            {
                await ClearRateLimitKeysAsync();
            }
        }

        [Fact]
        public async Task GivenCustomLimitEnvVar_WhenSet_ThenAppliesNewLimit()
        {
            using var scope = new EnvVarScope(new System.Collections.Generic.Dictionary<string, string?>
            {
                ["DISABLE_RATE_LIMIT"] = "false",
                ["ENVIRONMENT"] = "Production",
                ["RATE_LIMIT_MAX"] = "5",
                ["RATE_LIMIT_WINDOW_SECONDS"] = "10"
            });
            await ClearRateLimitKeysAsync();

            try
            {
                for (int i = 0; i < 5; i++)
                {
                    var resp = await _client.GetAsync("/health");
                    Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                }

                var blocked = await _client.GetAsync("/health");
                Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
                Assert.True(blocked.Headers.TryGetValues("x-ratelimit-limit", out var limitValues));
                Assert.Equal("5", limitValues!.First());
            }
            finally
            {
                await ClearRateLimitKeysAsync();
            }
        }

        [Fact]
        public async Task GivenInvalidEnvVar_WhenRead_ThenUsesDefaults()
        {
            using var scope = new EnvVarScope(new System.Collections.Generic.Dictionary<string, string?>
            {
                ["DISABLE_RATE_LIMIT"] = "false",
                ["ENVIRONMENT"] = "Production",
                ["RATE_LIMIT_MAX"] = "not-a-number",
                ["RATE_LIMIT_WINDOW_SECONDS"] = "-5"
            });
            await ClearRateLimitKeysAsync();

            try
            {
                var resp = await _client.GetAsync("/health");
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                Assert.True(resp.Headers.TryGetValues("x-ratelimit-limit", out var limitValues));
                Assert.Equal("100", limitValues!.First());
            }
            finally
            {
                await ClearRateLimitKeysAsync();
            }
        }

        [Fact]
        public async Task GivenDisabledRateLimit_WhenRequestMade_ThenAllowsAndExposesHeaders()
        {
            var originalDisable = Environment.GetEnvironmentVariable("DISABLE_RATE_LIMIT");
            Environment.SetEnvironmentVariable("DISABLE_RATE_LIMIT", "true");
            try
            {
                var resp = await _client.GetAsync("/health");
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                Assert.True(resp.Headers.TryGetValues("x-ratelimit-limit", out var limitValues));
                Assert.True(resp.Headers.TryGetValues("x-ratelimit-remaining", out var remainingValues));
                Assert.Equal(limitValues!.First(), remainingValues!.First());
            }
            finally
            {
                Environment.SetEnvironmentVariable("DISABLE_RATE_LIMIT", originalDisable);
            }
        }

        [Fact]
        public async Task GivenLegacyEnvironmentTest_WhenRequestMade_ThenAllowsBypass()
        {
            var origDisable = Environment.GetEnvironmentVariable("DISABLE_RATE_LIMIT");
            var origEnv = Environment.GetEnvironmentVariable("ENVIRONMENT");
            Environment.SetEnvironmentVariable("DISABLE_RATE_LIMIT", "false");
            Environment.SetEnvironmentVariable("ENVIRONMENT", "test");
            try
            {
                for (int i = 0; i < 200; i++)
                {
                    var resp = await _client.GetAsync("/health");
                    Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("DISABLE_RATE_LIMIT", origDisable);
                Environment.SetEnvironmentVariable("ENVIRONMENT", origEnv);
            }
        }

        [Fact]
        public async Task GivenRedisFailure_WhenTryIncrement_ThenReturnsSentinelAndFailsOpen()
        {
            var mockDb = new Mock<IDatabase>();

            var original = RateLimitMiddleware.DatabaseAccessor;
            RateLimitMiddleware.DatabaseAccessor = () => mockDb.Object;
            try
            {
                var (count, ttl) = await RateLimitMiddleware.TryIncrementAsync("ratelimit:ip:test", 60);
                Assert.Equal(-1, count);
                Assert.Equal(0, ttl);
            }
            finally
            {
                RateLimitMiddleware.DatabaseAccessor = original;
            }
        }

        [Fact]
        public async Task GivenRedisFailure_WhenHttpRequestArrives_ThenFailsOpenAndExposesHeaders()
        {
            var mockDb = new Mock<IDatabase>();

            var original = RateLimitMiddleware.DatabaseAccessor;
            RateLimitMiddleware.DatabaseAccessor = () => mockDb.Object;

            using var _ = EnableRateLimit();
            try
            {
                var resp = await _client.GetAsync("/health");
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                Assert.True(resp.Headers.TryGetValues("x-ratelimit-limit", out var limitValues));
                Assert.True(resp.Headers.TryGetValues("x-ratelimit-remaining", out var remainingValues));
                Assert.Equal(limitValues!.First(), remainingValues!.First());
            }
            finally
            {
                RateLimitMiddleware.DatabaseAccessor = original;
                await ClearRateLimitKeysAsync();
            }
        }
    }

    internal sealed class EnvVarScope : IDisposable
    {
        private readonly System.Collections.Generic.Dictionary<string, string?> _originals = new();

        public EnvVarScope(System.Collections.Generic.IDictionary<string, string?> values)
        {
            foreach (var kv in values)
            {
                _originals[kv.Key] = Environment.GetEnvironmentVariable(kv.Key);
                Environment.SetEnvironmentVariable(kv.Key, kv.Value);
            }
        }

        public void Dispose()
        {
            foreach (var kv in _originals)
            {
                Environment.SetEnvironmentVariable(kv.Key, kv.Value);
            }
        }
    }
}
