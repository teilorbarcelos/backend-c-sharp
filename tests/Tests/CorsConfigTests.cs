using System;
using System.Linq;
using MageBackend.Core;
using Xunit;

namespace MageBackend.Tests
{
    public class CorsConfigTests : IDisposable
    {
        private const string EnvVar = CorsConfig.AllowedOriginsEnvVar;

        public CorsConfigTests()
        {
            Environment.SetEnvironmentVariable(EnvVar, null);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(EnvVar, null);
            GC.SuppressFinalize(this);
        }

        [Fact]
        public void GivenProductionEnv_WhenIsProduction_ThenTrue()
        {
            Assert.True(CorsConfig.IsProduction("Production"));
        }

        [Fact]
        public void GivenProductionEnvCaseInsensitive_WhenIsProduction_ThenTrue()
        {
            Assert.True(CorsConfig.IsProduction("PRODUCTION"));
            Assert.True(CorsConfig.IsProduction("production"));
        }

        [Theory]
        [InlineData("Development")]
        [InlineData("Staging")]
        [InlineData("Testing")]
        [InlineData("Local")]
        [InlineData("")]
        public void GivenNonProductionEnv_WhenIsProduction_ThenFalse(string env)
        {
            Assert.False(CorsConfig.IsProduction(env));
        }

        [Fact]
        public void GivenEmptyEnvVarInDevelopment_WhenGetAllowedOrigins_ThenReturnsDevDefaults()
        {
            var origins = CorsConfig.GetAllowedOrigins("Development");

            Assert.Equal(CorsConfig.DevDefaultOrigins, origins);
        }

        [Fact]
        public void GivenEmptyEnvVarInTesting_WhenGetAllowedOrigins_ThenReturnsDevDefaults()
        {
            var origins = CorsConfig.GetAllowedOrigins("Testing");

            Assert.Equal(CorsConfig.DevDefaultOrigins, origins);
        }

        [Fact]
        public void GivenEmptyEnvVarInStaging_WhenGetAllowedOrigins_ThenReturnsDevDefaults()
        {
            var origins = CorsConfig.GetAllowedOrigins("Staging");

            Assert.Equal(CorsConfig.DevDefaultOrigins, origins);
        }

        [Fact]
        public void GivenWhitespaceEnvVarInDevelopment_WhenGetAllowedOrigins_ThenReturnsDevDefaults()
        {
            Environment.SetEnvironmentVariable(EnvVar, "   ");

            var origins = CorsConfig.GetAllowedOrigins("Development");

            Assert.Equal(CorsConfig.DevDefaultOrigins, origins);
        }

        [Fact]
        public void GivenEmptyEnvVarInProduction_WhenGetAllowedOrigins_ThenThrows()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => CorsConfig.GetAllowedOrigins("Production"));

            Assert.Contains("CORS_ALLOWED_ORIGINS", ex.Message);
            Assert.Contains("Production", ex.Message);
        }

        [Fact]
        public void GivenWhitespaceEnvVarInProduction_WhenGetAllowedOrigins_ThenThrows()
        {
            Environment.SetEnvironmentVariable(EnvVar, "  ");

            Assert.Throws<InvalidOperationException>(() => CorsConfig.GetAllowedOrigins("Production"));
        }

        [Fact]
        public void GivenSingleOriginEnvVar_WhenGetAllowedOrigins_ThenReturnsOne()
        {
            Environment.SetEnvironmentVariable(EnvVar, "https://app.example.com");

            var origins = CorsConfig.GetAllowedOrigins("Production");

            Assert.Single(origins);
            Assert.Equal("https://app.example.com", origins[0]);
        }

        [Fact]
        public void GivenMultipleOriginsEnvVar_WhenGetAllowedOrigins_ThenReturnsAllPreservingOrder()
        {
            Environment.SetEnvironmentVariable(EnvVar, "https://app.example.com,https://admin.example.com,https://api.example.com");

            var origins = CorsConfig.GetAllowedOrigins("Production");

            Assert.Equal(3, origins.Count);
            Assert.Equal("https://app.example.com", origins[0]);
            Assert.Equal("https://admin.example.com", origins[1]);
            Assert.Equal("https://api.example.com", origins[2]);
        }

        [Fact]
        public void GivenOriginsWithExtraWhitespace_WhenGetAllowedOrigins_ThenTrimsEach()
        {
            Environment.SetEnvironmentVariable(EnvVar, "  https://app.example.com  ,  https://admin.example.com  ");

            var origins = CorsConfig.GetAllowedOrigins("Production");

            Assert.Equal(2, origins.Count);
            Assert.Equal("https://app.example.com", origins[0]);
            Assert.Equal("https://admin.example.com", origins[1]);
        }

        [Fact]
        public void GivenOriginsWithEmptyEntries_WhenGetAllowedOrigins_ThenSkipsThem()
        {
            Environment.SetEnvironmentVariable(EnvVar, "https://app.example.com,,https://admin.example.com,");

            var origins = CorsConfig.GetAllowedOrigins("Production");

            Assert.Equal(2, origins.Count);
            Assert.Equal("https://app.example.com", origins[0]);
            Assert.Equal("https://admin.example.com", origins[1]);
        }

        [Fact]
        public void GivenEnvVarSetInNonProduction_WhenGetAllowedOrigins_ThenEnvVarWinsOverDefaults()
        {
            Environment.SetEnvironmentVariable(EnvVar, "https://custom.example.com");

            var origins = CorsConfig.GetAllowedOrigins("Development");

            Assert.Single(origins);
            Assert.Equal("https://custom.example.com", origins[0]);
        }

        [Fact]
        public void GivenDevDefaultOrigins_WhenInspected_ThenAllAreLoopbackHttp()
        {
            Assert.NotEmpty(CorsConfig.DevDefaultOrigins);
            Assert.All(CorsConfig.DevDefaultOrigins, origin =>
            {
                Assert.StartsWith("http://", origin);
                Assert.True(
                    origin.Contains("localhost:") || origin.Contains("127.0.0.1:"),
                    $"Dev origin '{origin}' should be a loopback URL");
            });
        }
    }
}
