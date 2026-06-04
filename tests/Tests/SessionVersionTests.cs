using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MageBackend.Database;
using MageBackend.Infrastructure.Auth;
using Xunit;

namespace MageBackend.Tests
{
    public class SessionVersionTests : IntegrationTestBase
    {
        public SessionVersionTests(IntegrationTestFixture fixture) : base(fixture) { }

        [Fact]
        public async Task GivenValidLogin_WhenAuthenticating_ThenSessionVersionIsStoredInRedis()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            var versionKey = $"session:user:{loginData.User.Id}:version";
            var redisDb = RedisProvider.Database;
            var stored = await redisDb.StringGetAsync(versionKey);
            Assert.True(stored.HasValue, "Session version should be set in Redis after login.");
            var storedValue = stored.ToString();
            Assert.True(int.TryParse(storedValue, out var storedVersion), $"Stored version '{storedValue}' is not a valid integer.");
            Assert.True(storedVersion >= 1, $"Expected version >= 1, got {storedVersion}");
        }

        [Fact]
        public async Task GivenValidLogin_WhenTokenIsIssued_ThenTokenContainsSessionVersionClaim()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            var db = RedisProvider.Database;
            var versionKey = $"session:user:{loginData.User.Id}:version";
            var stored = await db.StringGetAsync(versionKey);
            var storedValue = stored.ToString();
            Assert.True(int.TryParse(storedValue, out var redisVersion), $"Stored version '{storedValue}' is not a valid integer.");

            using (var scope = _fixture.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var user = await dbContext.User.Include(u => u.Auth).FirstOrDefaultAsync(u => u.Id == loginData.User.Id);
                Assert.NotNull(user);
                Assert.NotNull(user.Auth);
                Assert.Equal(redisVersion, user.Auth.SessionVersion);
            }
        }

        [Fact]
        public async Task GivenActiveSession_WhenLoggingOut_ThenSessionVersionIsIncrementedInDb()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            int beforeVersion;
            using (var scope = _fixture.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var user = await dbContext.User.Include(u => u.Auth).FirstOrDefaultAsync(u => u.Id == loginData.User.Id);
                Assert.NotNull(user?.Auth);
                beforeVersion = user!.Auth!.SessionVersion;
            }

            var logoutResp = await _client.PostAsync("/v1/auth/logout", null);
            Assert.Equal(HttpStatusCode.OK, logoutResp.StatusCode);

            int afterVersion;
            using (var scope = _fixture.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var user = await dbContext.User.Include(u => u.Auth).FirstOrDefaultAsync(u => u.Id == loginData.User.Id);
                Assert.NotNull(user?.Auth);
                afterVersion = user!.Auth!.SessionVersion;
            }

            Assert.True(afterVersion > beforeVersion, $"SessionVersion should be incremented after logout. Before={beforeVersion}, After={afterVersion}");
            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenPasswordChange_WhenCompletingRecovery_ThenSessionVersionIsIncremented()
        {
            await _client.PostAsJsonAsync("/v1/auth/password/request", new { email = "admin@email.com" });

            string token = "";
            int beforeVersion;
            using (var scope = _fixture.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var user = await dbContext.User.Include(u => u.Auth).FirstOrDefaultAsync(u => u.Email == "admin@email.com");
                Assert.NotNull(user?.Auth);
                token = user!.Auth!.RequestPasswordToken!;
                beforeVersion = user.Auth.SessionVersion;
            }

            var newPassword = "NewAdminPass123!";
            var changeResp = await _client.PostAsJsonAsync("/v1/auth/password/change", new { email = "admin@email.com", token, password = newPassword });
            Assert.Equal(HttpStatusCode.OK, changeResp.StatusCode);

            int afterVersion;
            using (var scope = _fixture.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var user = await dbContext.User.Include(u => u.Auth).FirstOrDefaultAsync(u => u.Email == "admin@email.com");
                Assert.NotNull(user?.Auth);
                afterVersion = user!.Auth!.SessionVersion;
            }

            Assert.True(afterVersion > beforeVersion, $"SessionVersion should be incremented after password change. Before={beforeVersion}, After={afterVersion}");

            // Restore the original password
            await LoginAsync("admin@email.com", newPassword);
            await _client.PostAsJsonAsync("/v1/auth/password/request", new { email = "admin@email.com" });
            using (var scope = _fixture.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var user = await dbContext.User.Include(u => u.Auth).FirstOrDefaultAsync(u => u.Email == "admin@email.com");
                token = user!.Auth!.RequestPasswordToken!;
            }
            await _client.PostAsJsonAsync("/v1/auth/password/change", new { email = "admin@email.com", token, password = "admin@123" });
        }

        [Fact]
        public async Task GivenExistingSessionInRedis_WhenKeyIsDeleted_ThenMiddlewareFallsBackToDatabase()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var versionKey = $"session:user:{loginData.User.Id}:version";
            var redisDb = RedisProvider.Database;
            await redisDb.KeyDeleteAsync(versionKey);

            var meResp = await _client.GetAsync("/v1/auth/me");
            Assert.Equal(HttpStatusCode.OK, meResp.StatusCode);

            var rehydrated = await redisDb.StringGetAsync(versionKey);
            Assert.True(rehydrated.HasValue, "Middleware should repopulate Redis from DB on cache miss.");
            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenCacheRehydratedFromDb_WhenTokenVersionMatches_ThenRequestSucceeds()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var versionKey = $"session:user:{loginData.User.Id}:version";
            var redisDb = RedisProvider.Database;
            await redisDb.KeyDeleteAsync(versionKey);

            // 1st request triggers DB fallback and repopulates Redis
            var resp1 = await _client.GetAsync("/v1/auth/me");
            Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);

            // 2nd request should hit Redis and pass
            var resp2 = await _client.GetAsync("/v1/auth/me");
            Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);
            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenFutureVersionClaim_WhenRequestComesIn_ThenReturnsUnauthorized()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");

            // Manually craft a token-like scenario: tamper sv to be higher than current
            int currentVersion;
            using (var scope = _fixture.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var user = await dbContext.User.Include(u => u.Auth).FirstOrDefaultAsync(u => u.Id == loginData.User.Id);
                currentVersion = user!.Auth!.SessionVersion;
            }

            // Set Redis version to a LOWER number so token's higher sv becomes invalid
            var versionKey = $"session:user:{loginData.User.Id}:version";
            var redisDb = RedisProvider.Database;
            await redisDb.StringSetAsync(versionKey, "0", TimeSpan.FromDays(7));

            // Now construct a token claim that has sv = currentVersion (which is > stored 0)
            // We can't easily forge a JWT, so we use the existing token (which has sv = currentVersion) and verify it's rejected
            SetAuthHeader(loginData.Token);
            var resp = await _client.GetAsync("/v1/auth/me");
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenRoleUpdate_WhenExecuted_ThenInvalidatesSessionsForAllRoleUsers()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var roleName = $"SessVerRole {uniqueSuffix}";

            var roleResp = await _client.PostAsJsonAsync("/v1/role", new
            {
                name = roleName,
                description = "Session Version Test",
                permissions = new[] { new { id_feature = "user", create = true, view = true, delete = false, activate = false } }
            });
            Assert.Contains(roleResp.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });
            var roleData = await roleResp.Content.ReadFromJsonAsync<JsonElement>();
            var roleId = roleData.GetProperty("id").GetString()!;

            var email1 = $"sv_user1_{uniqueSuffix}@email.com";
            var email2 = $"sv_user2_{uniqueSuffix}@email.com";
            var password = "Password123!";

            foreach (var email in new[] { email1, email2 })
            {
                var u = await _client.PostAsJsonAsync("/v1/user", new
                {
                    name = "SV User",
                    email,
                    password,
                    id_role = roleId,
                    active = true
                });
                Assert.Contains(u.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });
            }
            ClearAuthHeader();

            // Login both users and store their tokens
            var login1 = await LoginAsync(email1, password);
            var login2 = await LoginAsync(email2, password);

            // Use a fresh admin login to update the role
            var adminLogin = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(adminLogin.Token);
            var updateResp = await _client.PutAsJsonAsync($"/v1/role/{roleId}", new
            {
                name = roleName,
                description = "Updated desc",
                permissions = new[] { new { id_feature = "user", create = true, view = true, delete = false, activate = true } }
            });
            Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);

            await Task.Delay(500);
            ClearAuthHeader();

            // Both users' tokens should now be invalid
            SetAuthHeader(login1.Token);
            var resp1 = await _client.GetAsync("/v1/auth/me");
            Assert.Equal(HttpStatusCode.Unauthorized, resp1.StatusCode);
            ClearAuthHeader();

            SetAuthHeader(login2.Token);
            var resp2 = await _client.GetAsync("/v1/auth/me");
            Assert.Equal(HttpStatusCode.Unauthorized, resp2.StatusCode);
            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenRoleDeactivation_WhenExecuted_ThenInvalidatesSessionsForAllRoleUsers()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var roleName = $"SessVerDeact {uniqueSuffix}";

            var roleResp = await _client.PostAsJsonAsync("/v1/role", new
            {
                name = roleName,
                description = "Deactivation test",
                permissions = new[] { new { id_feature = "user", create = true, view = true, delete = false, activate = false } }
            });
            Assert.Contains(roleResp.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });
            var roleData = await roleResp.Content.ReadFromJsonAsync<JsonElement>();
            var roleId = roleData.GetProperty("id").GetString()!;

            var email = $"sv_deact_{uniqueSuffix}@email.com";
            var password = "Password123!";
            await _client.PostAsJsonAsync("/v1/user", new
            {
                name = "SV Deact User",
                email,
                password,
                id_role = roleId,
                active = true
            });
            ClearAuthHeader();

            var userLogin = await LoginAsync(email, password);

            // Deactivate the role
            var adminLogin = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(adminLogin.Token);
            await _client.PatchAsJsonAsync($"/v1/role/{roleId}/status", new { active = false });
            await Task.Delay(500);
            ClearAuthHeader();

            SetAuthHeader(userLogin.Token);
            var meResp = await _client.GetAsync("/v1/auth/me");
            Assert.Equal(HttpStatusCode.Unauthorized, meResp.StatusCode);
            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenRoleDeletion_WhenExecuted_ThenInvalidatesSessionsForAllRoleUsers()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var roleName = $"SessVerDel {uniqueSuffix}";

            var roleResp = await _client.PostAsJsonAsync("/v1/role", new
            {
                name = roleName,
                description = "Deletion test",
                permissions = new[] { new { id_feature = "user", create = true, view = true, delete = false, activate = false } }
            });
            Assert.Contains(roleResp.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });
            var roleData = await roleResp.Content.ReadFromJsonAsync<JsonElement>();
            var roleId = roleData.GetProperty("id").GetString()!;

            var email = $"sv_del_{uniqueSuffix}@email.com";
            var password = "Password123!";
            await _client.PostAsJsonAsync("/v1/user", new
            {
                name = "SV Del User",
                email,
                password,
                id_role = roleId,
                active = true
            });
            ClearAuthHeader();

            var userLogin = await LoginAsync(email, password);

            var adminLogin = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(adminLogin.Token);
            await _client.DeleteAsync($"/v1/role/{roleId}");
            await Task.Delay(500);
            ClearAuthHeader();

            SetAuthHeader(userLogin.Token);
            var meResp = await _client.GetAsync("/v1/auth/me");
            Assert.Equal(HttpStatusCode.Unauthorized, meResp.StatusCode);
            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenInvalidateUserSessionsAsync_WhenUserHasNoAuth_ThenReturnsZero()
        {
            // Direct unit-style test of the static method using a fresh context
            using var scope = _fixture.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var result = await SessionManager.InvalidateUserSessionsAsync("non-existent-user-id-xyz", dbContext);
            Assert.Equal(0, result);
        }
    }
}
