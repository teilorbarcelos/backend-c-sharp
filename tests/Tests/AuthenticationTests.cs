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
    public class AuthenticationTests : IntegrationTestBase
    {
        public AuthenticationTests(IntegrationTestFixture fixture) : base(fixture) { }

        // ==========================================
        // --- 01. Auth & Session (9 tests) ---------
        // ==========================================

        [Fact]
        public async Task GivenInvalidCredentials_WhenLoggingIn_ThenReturnsUnauthorized()
        {
            var invalidResp = await _client.PostAsJsonAsync("/v1/auth/login", new { email = "wrong@example.com", password = "wrong" });
            Assert.Equal(HttpStatusCode.Unauthorized, invalidResp.StatusCode);
        }

        [Fact]
        public async Task GivenValidCredentials_WhenLoggingIn_ThenReturnsAuthTokensAndCreatesSession()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            Assert.NotEmpty(loginData.Token);
            Assert.NotEmpty(loginData.RefreshToken);
            Assert.Equal("admin@email.com", loginData.User.Email);
        }

        [Fact]
        public async Task GivenValidLogin_WhenProcessed_ThenRedisSessionIsCreated()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            var db = RedisProvider.Database;
            var server = db.Multiplexer.GetServer(db.Multiplexer.GetEndPoints()[0]);
            var found = false;
            foreach (var key in server.Keys())
            {
                if (key.ToString().Contains(loginData.User.Id))
                {
                    found = true;
                    break;
                }
            }
            Assert.True(found, "Redis session key not found for user.");
        }

        [Fact]
        public async Task GivenValidRefreshToken_WhenRefreshing_ThenReturnsNewTokens()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            var refreshResp = await _client.PostAsJsonAsync("/v1/auth/refresh", new { refreshToken = loginData.RefreshToken });
            Assert.Equal(HttpStatusCode.OK, refreshResp.StatusCode);
            var refreshData = await refreshResp.Content.ReadFromJsonAsync<LoginResponse>();
            Assert.NotNull(refreshData);
            Assert.NotEmpty(refreshData.Token);
        }

        [Fact]
        public async Task GivenInvalidTokens_WhenAccessing_ThenReturnsUnauthorized()
        {
            SetAuthHeader("invalid-token");
            var meInvalidResp = await _client.GetAsync("/v1/auth/me");
            Assert.Equal(HttpStatusCode.Unauthorized, meInvalidResp.StatusCode);
            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenAuthenticatedUser_WhenRequestingMe_ThenReturnsValidStructure()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);
            var meResp = await _client.GetAsync("/v1/auth/me");
            Assert.Equal(HttpStatusCode.OK, meResp.StatusCode);
            var meData = await meResp.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(meData.TryGetProperty("user", out var userEl));
            Assert.Equal("admin@email.com", userEl.GetProperty("email").GetString());
            Assert.True(userEl.TryGetProperty("role", out var roleEl));
            Assert.Equal(JsonValueKind.Object, roleEl.ValueKind);
            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenMutationAction_WhenExecuted_ThenInvalidatesSession()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);
            var meResp = await _client.GetAsync("/v1/auth/me");
            Assert.Equal(HttpStatusCode.OK, meResp.StatusCode);
            var meData = await meResp.Content.ReadFromJsonAsync<JsonElement>();
            var userObj = meData.GetProperty("user");
            var userId = userObj.GetProperty("id").GetString()!;
            var name = userObj.GetProperty("name").GetString()!;
            var roleId = userObj.GetProperty("role").GetProperty("id").GetString()!;

            var updateResp = await _client.PutAsJsonAsync($"/v1/user/{userId}", new
            {
                name = name + " Updated",
                email = "admin@email.com",
                id_role = roleId
            });

            if (updateResp.StatusCode == HttpStatusCode.OK)
            {
                await Task.Delay(600);
                var meResp2 = await _client.GetAsync("/v1/auth/me");
                Assert.Equal(HttpStatusCode.Unauthorized, meResp2.StatusCode);
            }
            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenInactiveUser_WhenLoggingIn_ThenReturnsForbidden()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var email = $"inactive_user_{uniqueSuffix}@email.com";
            var password = $"Pass_{uniqueSuffix}!";

            var createUserResp = await _client.PostAsJsonAsync("/v1/user", new
            {
                name = "Inactive User Test",
                email = email,
                password = password,
                id_role = "administrator",
                active = true
            });
            Assert.Contains(createUserResp.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });
            var userData = await createUserResp.Content.ReadFromJsonAsync<JsonElement>();
            var userId = userData.GetProperty("id").GetString()!;

            // Deactivate direct in DB or via Patch
            var deactResp = await _client.PatchAsJsonAsync($"/v1/user/{userId}/status", new { active = false });
            Assert.Equal(HttpStatusCode.OK, deactResp.StatusCode);

            ClearAuthHeader();

            var loginResp = await _client.PostAsJsonAsync("/v1/auth/login", new { email = email, password = password });
            Assert.Contains(loginResp.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden });
        }

        [Fact]
        public async Task GivenInactiveRole_WhenLoggingIn_ThenReturnsForbidden()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var roleName = $"Temp Role {uniqueSuffix}";
            var createRoleResp = await _client.PostAsJsonAsync("/v1/role", new
            {
                name = roleName,
                description = "Will be deactivated",
                permissions = new List<object>()
            });
            Assert.Contains(createRoleResp.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });
            var roleData = await createRoleResp.Content.ReadFromJsonAsync<JsonElement>();
            var roleId = roleData.GetProperty("id").GetString()!;

            var email = $"inactive_role_{uniqueSuffix}@email.com";
            var password = $"Pass_{uniqueSuffix}!";
            var createUserResp = await _client.PostAsJsonAsync("/v1/user", new
            {
                name = "Inactive Role Test User",
                email = email,
                password = password,
                id_role = roleId,
                active = true
            });
            Assert.Contains(createUserResp.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });

            // Deactivate role
            var deactResp = await _client.PatchAsJsonAsync($"/v1/role/{roleId}/status", new { active = false });
            Assert.Equal(HttpStatusCode.OK, deactResp.StatusCode);

            ClearAuthHeader();

            var loginResp = await _client.PostAsJsonAsync("/v1/auth/login", new { email = email, password = password });
            Assert.Contains(loginResp.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden });
        }

        [Fact]
        public async Task GivenEmptyCredentials_WhenLoggingIn_ThenReturnsBadRequest()
        {
            var resp = await _client.PostAsJsonAsync("/v1/auth/login", new { email = "", password = "" });
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }

        [Fact]
        public async Task GivenValidEmailButWrongPassword_WhenLoggingIn_ThenReturnsUnauthorized()
        {
            var resp = await _client.PostAsJsonAsync("/v1/auth/login", new { email = "admin@email.com", password = "wrong-password" });
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }

        [Fact]
        public async Task GivenInvalidRefreshTokenSignature_WhenRefreshing_ThenReturnsUnauthorized()
        {
            var resp = await _client.PostAsJsonAsync("/v1/auth/refresh", new { refreshToken = "invalid.jwt.token" });
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }

        [Fact]
        public async Task GivenValidRefreshTokenButDeletedSession_WhenRefreshing_ThenReturnsUnauthorized()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");

            var redisDb = RedisProvider.Database;
            var refreshBytes = System.Text.Encoding.UTF8.GetBytes(loginData.RefreshToken);
            var refreshHashBytes = System.Security.Cryptography.SHA256.HashData(refreshBytes);
            var refreshTokenHash = Convert.ToHexString(refreshHashBytes).ToLower();
            var refreshKey = $"session:user:{loginData.User.Id}:refresh:{refreshTokenHash}";

            await redisDb.KeyDeleteAsync(refreshKey);

            var refreshResp = await _client.PostAsJsonAsync("/v1/auth/refresh", new { refreshToken = loginData.RefreshToken });
            Assert.Equal(HttpStatusCode.Unauthorized, refreshResp.StatusCode);
        }

        [Fact]
        public async Task GivenDeactivatedUser_WhenRefreshing_ThenReturnsUnauthorized()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");

            SetAuthHeader(loginData.Token);
            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var email = $"refresh_deact_{uniqueSuffix}@email.com";
            var password = "Password123!";

            var createUserResp = await _client.PostAsJsonAsync("/v1/user", new
            {
                name = "Refresh Deact User",
                email = email,
                password = password,
                id_role = "administrator"
            });
            var userData = await createUserResp.Content.ReadFromJsonAsync<JsonElement>();
            var userId = userData.GetProperty("id").GetString()!;

            ClearAuthHeader();
            var userLogin = await LoginAsync(email, password);

            SetAuthHeader(loginData.Token);
            await _client.PatchAsJsonAsync($"/v1/user/{userId}/status", new { active = false });
            ClearAuthHeader();

            var refreshResp = await _client.PostAsJsonAsync("/v1/auth/refresh", new { refreshToken = userLogin.RefreshToken });
            Assert.Equal(HttpStatusCode.Unauthorized, refreshResp.StatusCode);
        }

        [Fact]
        public async Task GivenDeactivatedUser_WhenRequestingMe_ThenReturnsUnauthorized()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var email = $"me_deact_{uniqueSuffix}@email.com";
            var password = "Password123!";

            var createUserResp = await _client.PostAsJsonAsync("/v1/user", new
            {
                name = "Me Deact User",
                email = email,
                password = password,
                id_role = "administrator"
            });
            var userData = await createUserResp.Content.ReadFromJsonAsync<JsonElement>();
            var userId = userData.GetProperty("id").GetString()!;

            ClearAuthHeader();
            var userLogin = await LoginAsync(email, password);

            SetAuthHeader(loginData.Token);
            await _client.PatchAsJsonAsync($"/v1/user/{userId}/status", new { active = false });

            SetAuthHeader(userLogin.Token);
            var meResp = await _client.GetAsync("/v1/auth/me");
            Assert.Equal(HttpStatusCode.Unauthorized, meResp.StatusCode);
            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenAuthenticatedUser_WhenLoggingOut_ThenInvalidatesSessions()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var logoutResp = await _client.PostAsync("/v1/auth/logout", null);
            Assert.Equal(HttpStatusCode.OK, logoutResp.StatusCode);

            var meResp = await _client.GetAsync("/v1/auth/me");
            Assert.Equal(HttpStatusCode.Unauthorized, meResp.StatusCode);
            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenEmptyRefreshToken_WhenRefreshing_ThenReturnsBadRequest()
        {
            var resp = await _client.PostAsJsonAsync("/v1/auth/refresh", new { refreshToken = "" });
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }

        [Fact]
        public async Task GivenSoftDeletedUser_WhenRefreshing_ThenReturnsUnauthorized()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var email = $"refresh_del_{uniqueSuffix}@email.com";
            var password = "Password123!";

            var createResp = await _client.PostAsJsonAsync("/v1/user", new
            {
                name = "Refresh Delete User",
                email = email,
                password = password,
                id_role = "administrator"
            });
            var userData = await createResp.Content.ReadFromJsonAsync<JsonElement>();
            var userId = userData.GetProperty("id").GetString()!;

            ClearAuthHeader();
            var userLogin = await LoginAsync(email, password);

            // Soft-delete the user via admin
            var adminLogin = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(adminLogin.Token);
            await _client.DeleteAsync($"/v1/user/{userId}");
            ClearAuthHeader();

            // Try to refresh with the deleted user's token
            var refreshResp = await _client.PostAsJsonAsync("/v1/auth/refresh", new { refreshToken = userLogin.RefreshToken });
            Assert.Equal(HttpStatusCode.Unauthorized, refreshResp.StatusCode);
        }

        [Fact]
        public async Task GivenSoftDeletedUser_WhenRequestingMe_ThenReturnsUnauthorized()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var email = $"me_del_{uniqueSuffix}@email.com";
            var password = "Password123!";

            var createResp = await _client.PostAsJsonAsync("/v1/user", new
            {
                name = "Me Delete User",
                email = email,
                password = password,
                id_role = "administrator"
            });
            var userData = await createResp.Content.ReadFromJsonAsync<JsonElement>();
            var userId = userData.GetProperty("id").GetString()!;

            ClearAuthHeader();
            var userLogin = await LoginAsync(email, password);

            // Soft-delete the user via admin
            var adminLogin = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(adminLogin.Token);
            await _client.DeleteAsync($"/v1/user/{userId}");

            // Try /me with the deleted user's token
            SetAuthHeader(userLogin.Token);
            var meResp = await _client.GetAsync("/v1/auth/me");
            Assert.Equal(HttpStatusCode.Unauthorized, meResp.StatusCode);
            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenDeactivatedUser_WhenRefreshingWithActiveSession_ThenReturnsUnauthorized()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var email = $"refresh_deact_{uniqueSuffix}@email.com";
            var password = "Password123!";

            var createResp = await _client.PostAsJsonAsync("/v1/user", new
            {
                name = "Refresh Deact User",
                email = email,
                password = password,
                id_role = "administrator"
            });
            var userData = await createResp.Content.ReadFromJsonAsync<JsonElement>();
            var userId = userData.GetProperty("id").GetString()!;

            ClearAuthHeader();
            var userLogin = await LoginAsync(email, password);

            // Directly update active to false in DB, avoiding the SessionManager invalidation
            using (var scope = _fixture.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var user = await dbContext.User.FindAsync(userId);
                if (user != null)
                {
                    user.Active = false;
                    await dbContext.SaveChangesAsync();
                }
            }

            // Try to refresh with the token (Redis session is still active)
            var refreshResp = await _client.PostAsJsonAsync("/v1/auth/refresh", new { refreshToken = userLogin.RefreshToken });
            Assert.Equal(HttpStatusCode.Unauthorized, refreshResp.StatusCode);
        }

        [Fact]
        public async Task GivenDeactivatedUser_WhenRequestingMeWithActiveSession_ThenReturnsUnauthorized()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var email = $"me_deact_{uniqueSuffix}@email.com";
            var password = "Password123!";

            var createResp = await _client.PostAsJsonAsync("/v1/user", new
            {
                name = "Me Deact User",
                email = email,
                password = password,
                id_role = "administrator"
            });
            var userData = await createResp.Content.ReadFromJsonAsync<JsonElement>();
            var userId = userData.GetProperty("id").GetString()!;

            ClearAuthHeader();
            var userLogin = await LoginAsync(email, password);

            // Directly update active to false in DB, avoiding the SessionManager invalidation
            using (var scope = _fixture.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var user = await dbContext.User.FindAsync(userId);
                if (user != null)
                {
                    user.Active = false;
                    await dbContext.SaveChangesAsync();
                }
            }

            // Try to access /me with the token (Redis session is still active)
            SetAuthHeader(userLogin.Token);
            var meResp = await _client.GetAsync("/v1/auth/me");
            Assert.Equal(HttpStatusCode.Unauthorized, meResp.StatusCode);
            ClearAuthHeader();
        }
    }
}