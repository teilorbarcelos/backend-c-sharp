using System;
using System.Linq;
using MageBackend.Infrastructure.Configuration;
using Xunit;

namespace MageBackend.Tests
{
    /*
     * Cobre o contrato do RateLimitConfig: single source of truth para os
     * limites por endpoint. Mudar um limite aqui = todos os lugares que
     * usam o config são atualizados atomicamente.
     */
    public class RateLimitConfigTests
    {
        [Theory]
        [InlineData("/v1/auth/login", 5, 60, "login")]
        [InlineData("/v1/auth/refresh", 30, 60, "refresh")]
        [InlineData("/v1/auth/password/request", 3, 60, "password_request")]
        [InlineData("/v1/auth/password/validate", 10, 60, "password_validate")]
        [InlineData("/v1/auth/password/change", 5, 60, "password_change")]
        [InlineData("/v1/user/export/pdf", 10, 60, "pdf_export")]
        public void GivenKnownEndpoint_WhenGetFor_ThenReturnsSpecificLimit(string path, int expectedMax, int expectedWindow, string expectedKey)
        {
            var limit = RateLimitConfig.GetFor(path);

            Assert.Equal(expectedMax, limit.Max);
            Assert.Equal(expectedWindow, limit.WindowSeconds);
            Assert.Equal(expectedKey, limit.Key);
        }

        [Fact]
        public void GivenUnknownEndpoint_WhenGetFor_ThenReturnsDefault()
        {
            var limit = RateLimitConfig.GetFor("/v1/unknown/endpoint");

            Assert.Equal(RateLimitConfig.DefaultMax, limit.Max);
            Assert.Equal(RateLimitConfig.DefaultWindowSeconds, limit.WindowSeconds);
            Assert.Equal(RateLimitConfig.DefaultKey, limit.Key);
        }

        [Fact]
        public void GivenNullPath_WhenGetFor_ThenReturnsDefault()
        {
            var limit = RateLimitConfig.GetFor(null);

            Assert.Equal(RateLimitConfig.DefaultMax, limit.Max);
        }

        [Fact]
        public void GivenEmptyPath_WhenGetFor_ThenReturnsDefault()
        {
            var limit = RateLimitConfig.GetFor(string.Empty);

            Assert.Equal(RateLimitConfig.DefaultMax, limit.Max);
        }

        [Theory]
        [InlineData("/V1/AUTH/LOGIN")]
        [InlineData("/V1/Auth/Login")]
        [InlineData("/v1/AUTH/login")]
        public void GivenCaseVariationOfKnownEndpoint_WhenGetFor_ThenReturnsSpecificLimit(string path)
        {
            var limit = RateLimitConfig.GetFor(path);

            Assert.Equal("login", limit.Key);
        }

        [Fact]
        public void GivenConfigEndpoints_WhenInspected_ThenAllValuesArePositive()
        {
            Assert.NotEmpty(RateLimitConfig.Endpoints);
            Assert.All(RateLimitConfig.Endpoints.Values, limit =>
            {
                Assert.True(limit.Max > 0, $"Max deve ser > 0, got {limit.Max}");
                Assert.True(limit.WindowSeconds > 0, $"WindowSeconds deve ser > 0, got {limit.WindowSeconds}");
                Assert.False(string.IsNullOrEmpty(limit.Key), "Key não pode ser vazia");
            });
        }

        [Fact]
        public void GivenConfigEndpoints_WhenInspected_ThenAuthEndpointsHaveStrictLimits()
        {
            /*
             * Justificativa dos valores (alinhado com NIST 800-63B / OWASP):
             *  - login ≤ 10 (credential stuffing)
             *  - password request ≤ 3 (email bombing / inbox DoS)
             */
            var login = RateLimitConfig.GetFor("/v1/auth/login");
            var passwordRequest = RateLimitConfig.GetFor("/v1/auth/password/request");

            Assert.True(login.Max <= 10, $"Login max deve ser ≤ 10 (credential stuffing), got {login.Max}");
            Assert.True(passwordRequest.Max <= 5, $"Password request max deve ser ≤ 5, got {passwordRequest.Max}");
        }

        [Fact]
        public void GivenExemptPathsDefault_WhenInspected_ThenEmpty()
        {
            /*
             * Empty por design — /health e /metrics funcionam com o default
             * (100/min) que é suficiente para monitoring. Adicione paths
             * aqui se um caso específico exigir isenção.
             */
            Assert.Empty(RateLimitConfig.ExemptPaths);
        }

        [Fact]
        public void GivenEmptyPath_WhenIsExempt_ThenFalse()
        {
            Assert.False(RateLimitConfig.IsExempt(null));
            Assert.False(RateLimitConfig.IsExempt(string.Empty));
        }

        [Fact]
        public void GivenUnregisteredPath_WhenIsExempt_ThenFalse()
        {
            Assert.False(RateLimitConfig.IsExempt("/v1/anything"));
        }

        [Fact]
        public void GivenAnyKnownPath_WhenGetFor_ThenKeyIsUniquePerEndpoint()
        {
            /*
             * Garante que cada endpoint tem bucket próprio no Redis
             * (esgotar login não afeta /v1/user/export/pdf).
             */
            var keys = RateLimitConfig.Endpoints.Values.Select(v => v.Key).ToList();
            Assert.Equal(keys.Count, keys.Distinct().Count());
        }
    }
}
