using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MageBackend.Database;
using MageBackend.Infrastructure.Auth;
using StackExchange.Redis;
using Xunit;

namespace MageBackend.Tests
{
    /*
     * Cobre a invariante de segurança:
     *   "Quando a sessão de um user é invalidada (role update, user update,
     *    deactivation, logout, password change), o refresh token também
     *    precisa ser invalidado. Caso contrário, o /v1/auth/refresh (que
     *    está nos public paths do middleware) gera um JWT novo silenciosamente
     *    e o user fica logado."
     *
     * Os testes que existiam em SessionVersionTests só validam GET /v1/auth/me
     * (que passa pelo middleware de sv check). Aqui validamos o refresh
     * endpoint E as chaves do Redis diretamente.
     */
    public class SessionRefreshInvalidationTests : IntegrationTestBase
    {
        public SessionRefreshInvalidationTests(IntegrationTestFixture fixture) : base(fixture) { }

        private static string ComputeRefreshKey(string userId, string refreshToken)
        {
            var bytes = Encoding.UTF8.GetBytes(refreshToken);
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLower();
            return $"session:user:{userId}:refresh:{hash}";
        }

        private static long CountRefreshKeysForUser(string userId, IDatabase db)
        {
            /*
             * Usa KEYS via Lua para contagem atômica. IServer.Keys (SCAN) tem
             * comportamento flaky quando combinado com múltiplas escritas
             * concorrentes no mesmo testcontainer, então para fins de assertion
             * de teste o KEYS atômico é mais confiável.
             */
            var pattern = $"session:user:{userId}:refresh:*";
            var result = (RedisResult)db.ScriptEvaluate(
                "return #redis.call('KEYS', KEYS[1])",
                new RedisKey[] { pattern });
            return (long)result;
        }

        [Fact]
        public async Task GivenLoginCreatesRefreshTokenInRedis_WhenLogin_ThenRefreshKeyExists()
        {
            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var (roleId, _, userToken) = await CreateRoleAndUserClientAsync(uniqueSuffix);
            var login = await LoginAsync($"session_tester_{uniqueSuffix}@email.com", "Password123!");

            var db = RedisProvider.Database;
            var key = ComputeRefreshKey(login.User.Id, login.RefreshToken);
            var exists = await db.KeyExistsAsync(key);

            Assert.True(exists, $"Refresh key should exist after login: {key}");
        }

        [Fact]
        public async Task GivenActiveRefreshToken_WhenInvalidateUserSessionsDirectly_ThenRefreshKeyIsDeleted()
        {
            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var (_, userId, _) = await CreateRoleAndUserClientAsync(uniqueSuffix);
            var login = await LoginAsync($"session_tester_{uniqueSuffix}@email.com", "Password123!");

            var db = RedisProvider.Database;
            var key = ComputeRefreshKey(login.User.Id, login.RefreshToken);
            Assert.True(await db.KeyExistsAsync(key));

            using var scope = _fixture.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await SessionManager.InvalidateUserSessionsAsync(login.User.Id, dbContext);

            Assert.False(await db.KeyExistsAsync(key),
                "Refresh key should be deleted after SessionManager.InvalidateUserSessionsAsync");
        }

        [Fact]
        public async Task GivenActiveRefreshToken_WhenRoleIsUpdated_ThenRefreshKeyIsDeleted()
        {
            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var (roleId, userId, _) = await CreateRoleAndUserClientAsync(uniqueSuffix);
            var userLogin = await LoginAsync($"session_tester_{uniqueSuffix}@email.com", "Password123!");

            var db = RedisProvider.Database;
            var key = ComputeRefreshKey(userLogin.User.Id, userLogin.RefreshToken);
            Assert.True(await db.KeyExistsAsync(key), "Setup: refresh key should exist after login");

            var adminLogin = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(adminLogin.Token);
            var updateResp = await _client.PutAsJsonAsync($"/v1/role/{roleId}", new
            {
                name = $"Updated {uniqueSuffix}",
                description = "After refresh test",
                permissions = new[] { new { id_feature = "user", create = false, view = true, delete = false, activate = false } }
            });
            Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);
            await Task.Delay(300);
            ClearAuthHeader();

            Assert.False(await db.KeyExistsAsync(key),
                "Refresh key should be deleted after role update");
        }

        [Fact]
        public async Task GivenActiveRefreshToken_WhenUserIsDeactivated_ThenRefreshKeyIsDeleted()
        {
            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var (_, userId, _) = await CreateRoleAndUserClientAsync(uniqueSuffix);
            var userLogin = await LoginAsync($"session_tester_{uniqueSuffix}@email.com", "Password123!");

            var db = RedisProvider.Database;
            var key = ComputeRefreshKey(userLogin.User.Id, userLogin.RefreshToken);
            Assert.True(await db.KeyExistsAsync(key), "Setup: refresh key should exist after login");

            var adminLogin = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(adminLogin.Token);
            var statusResp = await _client.PatchAsJsonAsync($"/v1/user/{userId}/status", new { active = false });
            Assert.Equal(HttpStatusCode.OK, statusResp.StatusCode);
            await Task.Delay(300);
            ClearAuthHeader();

            Assert.False(await db.KeyExistsAsync(key),
                "Refresh key should be deleted after user deactivation");
        }

        [Fact]
        public async Task GivenActiveRefreshToken_WhenUserIsUpdated_ThenRefreshKeyIsDeleted()
        {
            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var (roleId, userId, _) = await CreateRoleAndUserClientAsync(uniqueSuffix);
            var userLogin = await LoginAsync($"session_tester_{uniqueSuffix}@email.com", "Password123!");

            var db = RedisProvider.Database;
            var key = ComputeRefreshKey(userLogin.User.Id, userLogin.RefreshToken);
            Assert.True(await db.KeyExistsAsync(key), "Setup: refresh key should exist after login");

            var adminLogin = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(adminLogin.Token);
            var updateResp = await _client.PutAsJsonAsync($"/v1/user/{userId}", new
            {
                name = "Updated Name",
                email = $"updated_{uniqueSuffix}@email.com",
                id_role = roleId
            });
            Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);
            await Task.Delay(300);
            ClearAuthHeader();

            Assert.False(await db.KeyExistsAsync(key),
                "Refresh key should be deleted after user update");
        }

        [Fact]
        public async Task GivenActiveRefreshToken_WhenRoleIsDeactivated_ThenRefreshKeyIsDeleted()
        {
            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var (roleId, _, _) = await CreateRoleAndUserClientAsync(uniqueSuffix);
            var userLogin = await LoginAsync($"session_tester_{uniqueSuffix}@email.com", "Password123!");

            var db = RedisProvider.Database;
            var key = ComputeRefreshKey(userLogin.User.Id, userLogin.RefreshToken);
            Assert.True(await db.KeyExistsAsync(key), "Setup: refresh key should exist after login");

            var adminLogin = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(adminLogin.Token);
            var patchResp = await _client.PatchAsJsonAsync($"/v1/role/{roleId}/status", new { active = false });
            Assert.Equal(HttpStatusCode.OK, patchResp.StatusCode);
            await Task.Delay(300);
            ClearAuthHeader();

            Assert.False(await db.KeyExistsAsync(key),
                "Refresh key should be deleted after role deactivation");
        }

        [Fact]
        public async Task GivenActiveRefreshToken_WhenRoleIsDeleted_ThenRefreshKeyIsDeleted()
        {
            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var (roleId, _, _) = await CreateRoleAndUserClientAsync(uniqueSuffix);
            var userLogin = await LoginAsync($"session_tester_{uniqueSuffix}@email.com", "Password123!");

            var db = RedisProvider.Database;
            var key = ComputeRefreshKey(userLogin.User.Id, userLogin.RefreshToken);
            Assert.True(await db.KeyExistsAsync(key), "Setup: refresh key should exist after login");

            var adminLogin = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(adminLogin.Token);
            var delResp = await _client.DeleteAsync($"/v1/role/{roleId}");
            Assert.Contains(delResp.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.NoContent });
            await Task.Delay(300);
            ClearAuthHeader();

            Assert.False(await db.KeyExistsAsync(key),
                "Refresh key should be deleted after role deletion");
        }

        [Fact]
        public async Task GivenActiveRefreshToken_WhenUserLogsOut_ThenRefreshKeyIsDeleted()
        {
            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var (_, _, _) = await CreateRoleAndUserClientAsync(uniqueSuffix);
            var userLogin = await LoginAsync($"session_tester_{uniqueSuffix}@email.com", "Password123!");

            var db = RedisProvider.Database;
            var key = ComputeRefreshKey(userLogin.User.Id, userLogin.RefreshToken);
            Assert.True(await db.KeyExistsAsync(key), "Setup: refresh key should exist after login");

            SetAuthHeader(userLogin.Token);
            var logoutResp = await _client.PostAsync("/v1/auth/logout", null);
            Assert.Equal(HttpStatusCode.OK, logoutResp.StatusCode);
            ClearAuthHeader();

            Assert.False(await db.KeyExistsAsync(key),
                "Refresh key should be deleted after logout");
        }

        [Fact]
        public async Task GivenActiveRefreshToken_WhenSessionIsInvalidated_ThenRefreshEndpointReturns401()
        {
            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var (roleId, _, _) = await CreateRoleAndUserClientAsync(uniqueSuffix);
            var userLogin = await LoginAsync($"session_tester_{uniqueSuffix}@email.com", "Password123!");
            var refreshToken = userLogin.RefreshToken;
            Assert.NotEmpty(refreshToken);

            var preCheck = await _client.PostAsJsonAsync("/v1/auth/refresh", new { refreshToken });
            Assert.Equal(HttpStatusCode.OK, preCheck.StatusCode);

            var adminLogin = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(adminLogin.Token);
            var updateResp = await _client.PutAsJsonAsync($"/v1/role/{roleId}", new
            {
                name = $"Updated {uniqueSuffix}",
                description = "Refresh block test",
                permissions = new[] { new { id_feature = "user", create = false, view = true, delete = false, activate = false } }
            });
            Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);
            await Task.Delay(300);
            ClearAuthHeader();

            var postCheck = await _client.PostAsJsonAsync("/v1/auth/refresh", new { refreshToken });
            Assert.Equal(HttpStatusCode.Unauthorized, postCheck.StatusCode);
        }

        [Fact]
        public async Task GivenUserLoggedInOnMultipleDevices_WhenSessionIsInvalidated_ThenAllRefreshKeysAreDeleted()
        {
            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var (_, _, _) = await CreateRoleAndUserClientAsync(uniqueSuffix);
            var email = $"session_tester_{uniqueSuffix}@email.com";
            var password = "Password123!";

            var login1 = await LoginAsync(email, password);
            var login2 = await LoginAsync(email, password);
            var login3 = await LoginAsync(email, password);

            var db = RedisProvider.Database;
            var key1 = ComputeRefreshKey(login1.User.Id, login1.RefreshToken);
            var key2 = ComputeRefreshKey(login2.User.Id, login2.RefreshToken);
            var key3 = ComputeRefreshKey(login3.User.Id, login3.RefreshToken);

            Assert.True(await db.KeyExistsAsync(key1));
            Assert.True(await db.KeyExistsAsync(key2));
            Assert.True(await db.KeyExistsAsync(key3));

            // CreateRoleAndUserClientAsync já loga o user (1 chave) + 3 logins
            // explícitos = 4 chaves no total. Com jti no JWT, cada login produz
            // um refresh token único.
            Assert.Equal(4, CountRefreshKeysForUser(login1.User.Id, db));

            using var scope = _fixture.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await SessionManager.InvalidateUserSessionsAsync(login1.User.Id, dbContext);

            Assert.False(await db.KeyExistsAsync(key1));
            Assert.False(await db.KeyExistsAsync(key2));
            Assert.False(await db.KeyExistsAsync(key3));
            Assert.Equal(0, CountRefreshKeysForUser(login1.User.Id, db));
        }

        [Fact]
        public async Task GivenManyUsersInvalidated_WhenBatchInvoked_ThenAllRefreshKeysAreDeleted()
        {
            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var (roleId, _, _) = await CreateRoleAndUserClientAsync(uniqueSuffix);

            var suffix1 = uniqueSuffix + "_a";
            var suffix2 = uniqueSuffix + "_b";

            var (_, u1, _) = await CreateRoleAndUserClientAsync(suffix1);
            var (_, u2, _) = await CreateRoleAndUserClientAsync(suffix2);

            var login1 = await LoginAsync($"session_tester_{suffix1}@email.com", "Password123!");
            var login2 = await LoginAsync($"session_tester_{suffix2}@email.com", "Password123!");

            var db = RedisProvider.Database;
            Assert.Equal(2, CountRefreshKeysForUser(login1.User.Id, db));
            Assert.Equal(2, CountRefreshKeysForUser(login2.User.Id, db));

            using var scope = _fixture.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await SessionManager.InvalidateManyUsersSessionsAsync(
                new[] { login1.User.Id, login2.User.Id },
                dbContext);

            Assert.Equal(0, CountRefreshKeysForUser(login1.User.Id, db));
            Assert.Equal(0, CountRefreshKeysForUser(login2.User.Id, db));
        }

        /*
         * ============================================================
         *  Defesa em profundidade: sv claim do refresh é validado
         *  mesmo se a chave de refresh existir no Redis.
         * ============================================================
         */

        [Fact]
        public async Task GivenStaleRefreshKeyInRedis_WhenRefreshing_ThenReturns401EvenThoughKeyExists()
        {
            /*
             * Cenário hipotético de bypass: a chave session:user:{id}:refresh:*
             * sobreviveu a uma invalidação (bug, race, refactor). A defesa
             * primária (KeyExistsAsync) passa, mas a defesa em profundidade
             * (sv check) deve detectar o sv stale e bloquear.
             */
            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var (_, _, _) = await CreateRoleAndUserClientAsync(uniqueSuffix);
            var userLogin = await LoginAsync($"session_tester_{uniqueSuffix}@email.com", "Password123!");
            var refreshToken = userLogin.RefreshToken;

            var preCheck = await _client.PostAsJsonAsync("/v1/auth/refresh", new { refreshToken });
            Assert.Equal(HttpStatusCode.OK, preCheck.StatusCode);

            var db = RedisProvider.Database;
            var versionKey = $"session:user:{userLogin.User.Id}:version";
            var refreshedKey = await db.StringGetAsync(versionKey);
            Assert.True(refreshedKey.HasValue, "Setup: version key should be in Redis after login");
            var currentSv = int.Parse(refreshedKey.ToString());

            await db.StringSetAsync(versionKey, (currentSv + 50).ToString(), TimeSpan.FromDays(7));

            var postCheck = await _client.PostAsJsonAsync("/v1/auth/refresh", new { refreshToken });
            Assert.Equal(HttpStatusCode.Unauthorized, postCheck.StatusCode);
        }

        [Fact]
        public async Task GivenRefreshKeyExistsAndSvMatches_WhenRefreshing_ThenSucceeds()
        {
            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var (_, _, _) = await CreateRoleAndUserClientAsync(uniqueSuffix);
            var userLogin = await LoginAsync($"session_tester_{uniqueSuffix}@email.com", "Password123!");

            var preCheck = await _client.PostAsJsonAsync("/v1/auth/refresh", new { refreshToken = userLogin.RefreshToken });
            Assert.Equal(HttpStatusCode.OK, preCheck.StatusCode);
        }

        [Fact]
        public async Task GivenActiveSession_WhenVersionKeyDeletedFromRedis_ThenSvCheckFallsBackToDatabase()
        {
            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var (_, _, _) = await CreateRoleAndUserClientAsync(uniqueSuffix);
            var userLogin = await LoginAsync($"session_tester_{uniqueSuffix}@email.com", "Password123!");

            var db = RedisProvider.Database;
            var versionKey = $"session:user:{userLogin.User.Id}:version";
            await db.KeyDeleteAsync(versionKey);
            Assert.False(await db.KeyExistsAsync(versionKey), "Setup: version key should be deleted from Redis");

            var resp = await _client.PostAsJsonAsync("/v1/auth/refresh", new { refreshToken = userLogin.RefreshToken });
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            Assert.True(await db.KeyExistsAsync(versionKey),
                "Version key should be rehydrated from DB on cache miss");
        }

        [Fact]
        public async Task GivenNonExistentUser_WhenGetCurrentVersionAsync_ThenReturnsNull()
        {
            using var scope = _fixture.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var result = await SessionManager.GetCurrentVersionAsync("non-existent-user-zzz", dbContext);

            Assert.Null(result);
        }

        [Fact]
        public async Task GivenUserWithAuth_WhenGetCurrentVersionAsyncAndRedisEmpty_ThenHydratesFromDb()
        {
            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var (_, _, _) = await CreateRoleAndUserClientAsync(uniqueSuffix);
            var userLogin = await LoginAsync($"session_tester_{uniqueSuffix}@email.com", "Password123!");

            var db = RedisProvider.Database;
            var versionKey = $"session:user:{userLogin.User.Id}:version";
            await db.KeyDeleteAsync(versionKey);

            int expectedVersion;
            using (var scope = _fixture.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var user = await dbContext.User.Include(u => u.Auth).FirstOrDefaultAsync(u => u.Id == userLogin.User.Id);
                Assert.NotNull(user?.Auth);
                expectedVersion = user!.Auth!.SessionVersion;
            }

            using var scope2 = _fixture.Services.CreateScope();
            var dbContext2 = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var version = await SessionManager.GetCurrentVersionAsync(userLogin.User.Id, dbContext2);

            Assert.NotNull(version);
            Assert.Equal(expectedVersion, version.Value);
            Assert.True(await db.KeyExistsAsync(versionKey), "Helper should rehydrate Redis cache");
        }
    }
}
