using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using MageBackend.Web.Middleware;
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
            foreach (var key in server.Keys(pattern: "ratelimit:*"))
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

        /*
         * ============================================================
         *  Per-endpoint rate limit (RateLimitConfig)
         * ============================================================
         */

        [Fact]
        public async Task GivenLoginEndpoint_WhenLimitReached_ThenReturns429WithLoginSpecificHeaders()
        {
            using var _ = EnableRateLimit();
            await ClearRateLimitKeysAsync();

            try
            {
                var loginPayload = new { email = "ratelimit-test@example.com", password = "x" };
                for (int i = 0; i < 5; i++)
                {
                    var resp = await _client.PostAsJsonAsync("/v1/auth/login", loginPayload);
                    Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
                    Assert.True(resp.Headers.TryGetValues("x-ratelimit-limit", out var limits));
                    Assert.Equal("5", limits!.First());
                }

                var blocked = await _client.PostAsJsonAsync("/v1/auth/login", loginPayload);
                Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
            }
            finally
            {
                await ClearRateLimitKeysAsync();
            }
        }

        [Fact]
        public async Task GivenLoginLimitReached_WhenRequestingDifferentEndpoint_ThenOtherEndpointStillWorks()
        {
            /*
             * Garante buckets INDEPENDENTES no Redis. Esgotar login NÃO
             * afeta /v1/user/export/pdf (que tem bucket próprio).
             *
             * Estratégia: login como admin PRIMEIRO (1 dos 5), depois esgota
             * os 4 slots restantes com credenciais inválidas, depois tenta
             * o 6º (deve ser 429), depois tenta o PDF (deve funcionar).
             */
            using var _ = EnableRateLimit();
            await ClearRateLimitKeysAsync();

            try
            {
                var adminLogin = await LoginAsync("admin@email.com", "admin@123");
                SetAuthHeader(adminLogin.Token);

                for (int i = 0; i < 4; i++)
                {
                    var resp = await _client.PostAsJsonAsync("/v1/auth/login",
                        new { email = "ratelimit-isolation@example.com", password = "x" });
                    Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
                }

                var loginBlocked = await _client.PostAsJsonAsync("/v1/auth/login",
                    new { email = "ratelimit-isolation@example.com", password = "x" });
                Assert.Equal(HttpStatusCode.TooManyRequests, loginBlocked.StatusCode);

                var pdfResp = await _client.GetAsync("/v1/user/export/pdf");
                Assert.Equal(HttpStatusCode.OK, pdfResp.StatusCode);
                Assert.True(pdfResp.Headers.TryGetValues("x-ratelimit-limit", out var pdfLimits));
                Assert.Equal("10", pdfLimits!.First());
            }
            finally
            {
                ClearAuthHeader();
                await ClearRateLimitKeysAsync();
            }
        }

        [Fact]
        public async Task GivenMultipleEndpointsWithDifferentBuckets_WhenSameIpHitsBoth_ThenCountersAreSeparate()
        {
            using var _ = EnableRateLimit();
            await ClearRateLimitKeysAsync();

            try
            {
                /*
                 * /v1/auth/password/request tem limite 3/min. Após 3 OK,
                 * a 4ª já deve ser 429.
                 */
                for (int i = 0; i < 3; i++)
                {
                    var resp = await _client.PostAsJsonAsync("/v1/auth/password/request",
                        new { email = "spam-test@example.com" });
                    Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                }

                var blocked = await _client.PostAsJsonAsync("/v1/auth/password/request",
                    new { email = "spam-test@example.com" });
                Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);

                /*
                 * /v1/auth/login tem bucket próprio (5/min), independente
                 * do password_request (3/min). Mesmo IP, contador separado —
                 * as 3 calls acima NÃO devem consumir quota do login.
                 */
                for (int i = 0; i < 5; i++)
                {
                    var loginResp = await _client.PostAsJsonAsync("/v1/auth/login",
                        new { email = "ratelimit@example.com", password = "x" });
                    Assert.Equal(HttpStatusCode.Unauthorized, loginResp.StatusCode);
                }
            }
            finally
            {
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
