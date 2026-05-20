using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MageBackend.Database;
using Xunit;

namespace MageBackend.Tests
{
    public class PasswordRecoveryTests : IntegrationTestBase
    {
        public PasswordRecoveryTests(IntegrationTestFixture fixture) : base(fixture) { }

        [Fact]
        public async Task GivenValidEmail_WhenRequestingPasswordReset_ThenGeneratesToken()
        {
            var reqResp = await _client.PostAsJsonAsync("/v1/auth/password/request", new { email = "admin@email.com" });
            Assert.Equal(HttpStatusCode.OK, reqResp.StatusCode);

            // Fetch user from DB to get the generated token
            string token = "";
            using (var scope = _fixture.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var user = await dbContext.User.Include(u => u.Auth).FirstOrDefaultAsync(u => u.Email == "admin@email.com");
                Assert.NotNull(user);
                Assert.NotNull(user.Auth);
                Assert.NotNull(user.Auth.RequestPasswordToken);
                token = user.Auth.RequestPasswordToken;
            }

            // Validate token
            var valResp = await _client.PostAsJsonAsync("/v1/auth/password/validate", new { email = "admin@email.com", token });
            Assert.Equal(HttpStatusCode.OK, valResp.StatusCode);

            // Change password
            var changeResp = await _client.PostAsJsonAsync("/v1/auth/password/change", new { email = "admin@email.com", token, password = "NewAdminPassword123!" });
            Assert.Equal(HttpStatusCode.OK, changeResp.StatusCode);

            // Change it back to original for other tests
            var loginData = await LoginAsync("admin@email.com", "NewAdminPassword123!");
            Assert.NotNull(loginData.Token);
            
            // Revert process
            await _client.PostAsJsonAsync("/v1/auth/password/request", new { email = "admin@email.com" });
            using (var scope = _fixture.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var user = await dbContext.User.Include(u => u.Auth).FirstOrDefaultAsync(u => u.Email == "admin@email.com");
                token = user.Auth.RequestPasswordToken;
            }
            await _client.PostAsJsonAsync("/v1/auth/password/change", new { email = "admin@email.com", token, password = "admin@123" });
        }

        [Fact]
        public async Task GivenInvalidPayloads_WhenRecoveringPassword_ThenValidationFails()
        {
            // Missing email request
            var reqResp = await _client.PostAsJsonAsync("/v1/auth/password/request", new { email = "" });
            Assert.Equal(HttpStatusCode.BadRequest, reqResp.StatusCode);

            // Missing token validate
            var valResp = await _client.PostAsJsonAsync("/v1/auth/password/validate", new { email = "admin@email.com", token = "" });
            Assert.Equal(HttpStatusCode.BadRequest, valResp.StatusCode);

            // Missing password change
            var changeResp = await _client.PostAsJsonAsync("/v1/auth/password/change", new { email = "admin@email.com", token = "123456", password = "" });
            Assert.Equal(HttpStatusCode.BadRequest, changeResp.StatusCode);
        }

        [Fact]
        public async Task GivenInvalidTokenOrEmail_WhenValidatingOrChanging_ThenFails()
        {
            // NotFound user
            var notFoundVal = await _client.PostAsJsonAsync("/v1/auth/password/validate", new { email = "doesnotexist@email.com", token = "123456" });
            Assert.Equal(HttpStatusCode.NotFound, notFoundVal.StatusCode);

            var notFoundChange = await _client.PostAsJsonAsync("/v1/auth/password/change", new { email = "doesnotexist@email.com", token = "123456", password = "NewPassword123!" });
            Assert.Equal(HttpStatusCode.NotFound, notFoundChange.StatusCode);

            // Unauthorized token
            var unauthVal = await _client.PostAsJsonAsync("/v1/auth/password/validate", new { email = "admin@email.com", token = "000000" });
            Assert.Equal(HttpStatusCode.Unauthorized, unauthVal.StatusCode);

            var unauthChange = await _client.PostAsJsonAsync("/v1/auth/password/change", new { email = "admin@email.com", token = "000000", password = "NewPassword123!" });
            Assert.Equal(HttpStatusCode.Unauthorized, unauthChange.StatusCode);
        }
    }
}
