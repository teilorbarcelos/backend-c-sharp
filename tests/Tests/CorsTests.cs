using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace MageBackend.Tests
{
    /*
     * Testes de integração da política CORS.
     * O IntegrationTestFixture configura CORS_ALLOWED_ORIGINS com origens
     * conhecidas (ver IntegrationTestFixture.InitializeAsync), então esta
     * classe assume:
     *   - http://localhost:3000      (allowlist)
     *   - http://localhost:4200      (allowlist)
     *   - http://cors-test.example.com (allowlist)
     *   - http://attacker.example.com  (NÃO allowlist)
     */
    public class CorsTests : IntegrationTestBase
    {
        private const string AllowedOrigin1 = "http://localhost:3000";
        private const string AllowedOrigin2 = "http://localhost:4200";
        private const string AllowedOriginCustom = "http://cors-test.example.com";
        private const string DisallowedOrigin = "http://attacker.example.com";

        public CorsTests(IntegrationTestFixture fixture) : base(fixture)
        {
        }

        private static HttpRequestMessage BuildPreflight(string origin, string method = "POST", string path = "/v1/auth/login")
        {
            var request = new HttpRequestMessage(HttpMethod.Options, path);
            request.Headers.Add("Origin", origin);
            request.Headers.Add("Access-Control-Request-Method", method);
            request.Headers.Add("Access-Control-Request-Headers", "content-type,authorization");
            return request;
        }

        [Fact]
        public async Task GivenPreflightFromAllowedOrigin_WhenSending_ThenResponseHasMatchingAccessControlAllowOrigin()
        {
            var response = await _client.SendAsync(BuildPreflight(AllowedOrigin1));

            Assert.True(response.Headers.Contains("Access-Control-Allow-Origin"),
                "Preflight from allowed origin should include Access-Control-Allow-Origin header");
            var acao = response.Headers.GetValues("Access-Control-Allow-Origin").Single();
            Assert.Equal(AllowedOrigin1, acao);
        }

        [Fact]
        public async Task GivenPreflightFromSecondAllowedOrigin_WhenSending_ThenResponseEchoesOrigin()
        {
            var response = await _client.SendAsync(BuildPreflight(AllowedOrigin2));

            Assert.True(response.Headers.Contains("Access-Control-Allow-Origin"));
            Assert.Equal(AllowedOrigin2, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        }

        [Fact]
        public async Task GivenPreflightFromCustomDomainAllowedOrigin_WhenSending_ThenResponseEchoesOrigin()
        {
            var response = await _client.SendAsync(BuildPreflight(AllowedOriginCustom));

            Assert.True(response.Headers.Contains("Access-Control-Allow-Origin"));
            Assert.Equal(AllowedOriginCustom, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        }

        [Fact]
        public async Task GivenPreflightFromDisallowedOrigin_WhenSending_ThenAccessControlAllowOriginDoesNotEchoAttacker()
        {
            var response = await _client.SendAsync(BuildPreflight(DisallowedOrigin));

            if (response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values))
            {
                Assert.NotEqual(DisallowedOrigin, values.Single());
            }
        }

        [Fact]
        public async Task GivenPreflightFromAllowedOrigin_WhenSending_ThenAccessControlAllowMethodsIsAdvertised()
        {
            var response = await _client.SendAsync(BuildPreflight(AllowedOrigin1, method: "POST"));

            Assert.True(response.Headers.Contains("Access-Control-Allow-Methods"));
        }

        [Fact]
        public async Task GivenGetRequestFromAllowedOrigin_WhenSending_ThenResponseHasAccessControlAllowOrigin()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/health");
            request.Headers.Add("Origin", AllowedOrigin1);

            var response = await _client.SendAsync(request);

            Assert.NotNull(response);
            Assert.True(response.Headers.Contains("Access-Control-Allow-Origin"));
            Assert.Equal(AllowedOrigin1, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        }

        [Fact]
        public async Task GivenGetRequestFromDisallowedOrigin_WhenSending_ThenResponseDoesNotEchoAttacker()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/health");
            request.Headers.Add("Origin", DisallowedOrigin);

            var response = await _client.SendAsync(request);

            if (response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values))
            {
                Assert.NotEqual(DisallowedOrigin, values.Single());
            }
        }
    }
}
