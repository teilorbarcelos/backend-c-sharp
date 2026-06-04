using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace MageBackend.Tests
{
    /*
     * Cobertura anti-enumeração dos endpoints de auth.
     *
     * O vetor de enumeração é: o response (status, body, error key) revela
     * se um email está cadastrado. Atacante varre uma wordlist de emails
     * e observa as respostas para construir uma lista de alvos válidos,
     * depois parte pra credential stuffing / spear phishing.
     *
     * A correção é colapsar TODAS as falhas de auth em uma única resposta
     * externa, mantendo o motivo real apenas em logs internos (para o SOC
     * detectar tentativas de enumeração).
     */
    public class AuthEnumerationTests : IntegrationTestBase
    {
        public AuthEnumerationTests(IntegrationTestFixture fixture) : base(fixture) { }

        [Fact]
        public async Task GivenLoginWithNonExistentEmail_WhenComparedWithLoginWithWrongPassword_ThenResponsesAreIndistinguishable()
        {
            var nonExistent = await _client.PostAsJsonAsync("/v1/auth/login", new
            {
                email = "definitely-does-not-exist-1234567890@nowhere.invalid",
                password = "anything"
            });

            var wrongPassword = await _client.PostAsJsonAsync("/v1/auth/login", new
            {
                email = "admin@email.com",
                password = "wrong-password-deliberately"
            });

            Assert.Equal(HttpStatusCode.Unauthorized, nonExistent.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);

            var nonExistentBody = await nonExistent.Content.ReadAsStringAsync();
            var wrongPasswordBody = await wrongPassword.Content.ReadAsStringAsync();
            Assert.Equal(nonExistentBody, wrongPasswordBody);
        }

        [Fact]
        public async Task GivenLoginWithInactiveUser_WhenComparedWithLoginWithWrongPassword_ThenResponsesAreIndistinguishable()
        {
            var uniqueSuffix = System.Guid.NewGuid().ToString().Substring(0, 8);
            var email = $"enum_test_{uniqueSuffix}@email.com";
            var password = "Password123!";

            var adminLogin = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(adminLogin.Token);
            var roleResp = await _client.PostAsJsonAsync("/v1/role", new
            {
                name = $"EnumTestRole {uniqueSuffix}",
                description = "Enum test",
                permissions = new[] { new { id_feature = "user", create = true, view = true, delete = false, activate = false } }
            });
            Assert.Contains(roleResp.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });
            var roleData = await roleResp.Content.ReadFromJsonAsync<JsonElement>();
            var roleId = roleData.GetProperty("id").GetString()!;

            var userResp = await _client.PostAsJsonAsync("/v1/user", new
            {
                name = "Enum Test User",
                email,
                password,
                id_role = roleId,
                active = true
            });
            Assert.Contains(userResp.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });

            var loginActive = await _client.PostAsJsonAsync("/v1/auth/login", new { email, password });
            Assert.Equal(HttpStatusCode.OK, loginActive.StatusCode);

            var userData = await userResp.Content.ReadFromJsonAsync<JsonElement>();
            var userId = userData.GetProperty("id").GetString()!;
            var deactivateResp = await _client.PatchAsJsonAsync($"/v1/user/{userId}/status", new { active = false });
            Assert.Equal(HttpStatusCode.OK, deactivateResp.StatusCode);
            ClearAuthHeader();

            var loginInactive = await _client.PostAsJsonAsync("/v1/auth/login", new { email, password });
            var wrongPassword = await _client.PostAsJsonAsync("/v1/auth/login", new
            {
                email = "admin@email.com",
                password = "wrong-password-deliberately"
            });

            Assert.Equal(HttpStatusCode.Unauthorized, loginInactive.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
            var inactiveBody = await loginInactive.Content.ReadAsStringAsync();
            var wrongBody = await wrongPassword.Content.ReadAsStringAsync();
            Assert.Equal(inactiveBody, wrongBody);
        }

        [Fact]
        public async Task GivenValidateResetTokenWithNonExistentEmail_WhenComparedWithWrongToken_ThenResponsesAreIndistinguishable()
        {
            var nonExistent = await _client.PostAsJsonAsync("/v1/auth/password/validate", new
            {
                email = "definitely-does-not-exist-1234567890@nowhere.invalid",
                token = "123456"
            });

            var wrongToken = await _client.PostAsJsonAsync("/v1/auth/password/validate", new
            {
                email = "admin@email.com",
                token = "wrong-token-123456"
            });

            Assert.Equal(HttpStatusCode.Unauthorized, nonExistent.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, wrongToken.StatusCode);

            var nonExistentBody = await nonExistent.Content.ReadAsStringAsync();
            var wrongTokenBody = await wrongToken.Content.ReadAsStringAsync();
            Assert.Equal(nonExistentBody, wrongTokenBody);
        }

        [Fact]
        public async Task GivenChangePasswordWithNonExistentEmail_WhenComparedWithWrongToken_ThenResponsesAreIndistinguishable()
        {
            var nonExistent = await _client.PostAsJsonAsync("/v1/auth/password/change", new
            {
                email = "definitely-does-not-exist-1234567890@nowhere.invalid",
                token = "123456",
                password = "NewPassword123!"
            });

            var wrongToken = await _client.PostAsJsonAsync("/v1/auth/password/change", new
            {
                email = "admin@email.com",
                token = "wrong-token-123456",
                password = "NewPassword123!"
            });

            Assert.Equal(HttpStatusCode.Unauthorized, nonExistent.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, wrongToken.StatusCode);

            var nonExistentBody = await nonExistent.Content.ReadAsStringAsync();
            var wrongTokenBody = await wrongToken.Content.ReadAsStringAsync();
            Assert.Equal(nonExistentBody, wrongTokenBody);
        }

        [Fact]
        public async Task GivenRequestPasswordResetForAnyEmail_WhenCompared_ThenAlwaysReturns200()
        {
            var nonExistent = await _client.PostAsJsonAsync("/v1/auth/password/request", new
            {
                email = "definitely-does-not-exist-1234567890@nowhere.invalid"
            });

            var existent = await _client.PostAsJsonAsync("/v1/auth/password/request", new
            {
                email = "admin@email.com"
            });

            Assert.Equal(HttpStatusCode.OK, nonExistent.StatusCode);
            Assert.Equal(HttpStatusCode.OK, existent.StatusCode);
        }
    }
}
