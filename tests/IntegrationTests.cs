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
    public class IntegrationTests : IClassFixture<IntegrationTestFixture>
    {
        private readonly IntegrationTestFixture _fixture;
        private readonly HttpClient _client;

        public IntegrationTests(IntegrationTestFixture fixture)
        {
            _fixture = fixture;
            _client = _fixture.CreateClient();
        }

        private async Task<LoginResponse> LoginAsync(string email, string password)
        {
            var response = await _client.PostAsJsonAsync("/v1/auth/login", new { email, password });
            response.EnsureSuccessStatusCode();
            var data = await response.Content.ReadFromJsonAsync<LoginResponse>();
            Assert.NotNull(data);
            return data;
        }

        private void SetAuthHeader(string token)
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        private void ClearAuthHeader()
        {
            _client.DefaultRequestHeaders.Authorization = null;
        }

        private async Task<(string roleId, string userId, string token)> CreateRoleAndUserClientAsync(string uniqueSuffix)
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            // 1. Create a role
            var rolePayload = new
            {
                name = $"Tester Role {uniqueSuffix}",
                description = "Verification Role",
                permissions = new[]
                {
                    new
                    {
                        id_feature = "product",
                        create = true,
                        view = true,
                        delete = false,
                        activate = false
                    }
                }
            };
            var roleResp = await _client.PostAsJsonAsync("/v1/role", rolePayload);
            Assert.Contains(roleResp.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });
            var roleData = await roleResp.Content.ReadFromJsonAsync<JsonElement>();
            var roleId = roleData.GetProperty("id").GetString()!;

            // 2. Create a user assigned to this role
            var email = $"session_tester_{uniqueSuffix}@email.com";
            var userPayload = new
            {
                name = "Session Tester",
                email = email,
                password = "Password123!",
                id_role = roleId,
                active = true
            };
            var userResp = await _client.PostAsJsonAsync("/v1/user", userPayload);
            Assert.Contains(userResp.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });
            var userData = await userResp.Content.ReadFromJsonAsync<JsonElement>();
            var userId = userData.GetProperty("id").GetString()!;

            // 3. Log in to get active session token
            var loginResp = await _client.PostAsJsonAsync("/v1/auth/login", new { email = email, password = "Password123!" });
            Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
            var loginBody = await loginResp.Content.ReadFromJsonAsync<LoginResponse>();
            Assert.NotNull(loginBody);

            return (roleId, userId, loginBody.Token);
        }

        // ==========================================
        // --- 07. Observability (2 tests) ----------
        // ==========================================

        [Fact]
        public async Task Test_07_Health_Check_Endpoint()
        {
            var healthResp = await _client.GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, healthResp.StatusCode);
            var healthData = await healthResp.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("UP", healthData.GetProperty("status").GetString());
        }

        [Fact]
        public async Task Test_07_Prometheus_Metrics_Endpoint()
        {
            var metricsResp = await _client.GetAsync("/metrics");
            Assert.Equal(HttpStatusCode.OK, metricsResp.StatusCode);
            var metricsText = await metricsResp.Content.ReadAsStringAsync();
            Assert.Contains("http_request_duration_seconds", metricsText);
        }

        // ==========================================
        // --- 08. Rate Limit (1 test) --------------
        // ==========================================

        [Fact]
        public async Task Test_08_Rate_Limit_Headers()
        {
            var resp = await _client.GetAsync("/health");
            Assert.True(resp.Headers.Contains("x-ratelimit-limit"), "Missing x-ratelimit-limit header");
            Assert.True(resp.Headers.Contains("x-ratelimit-remaining"), "Missing x-ratelimit-remaining header");
        }

        // ==========================================
        // --- 01. Auth & Session (9 tests) ---------
        // ==========================================

        [Fact]
        public async Task Test_01_Login_Invalid_Credentials()
        {
            var invalidResp = await _client.PostAsJsonAsync("/v1/auth/login", new { email = "wrong@example.com", password = "wrong" });
            Assert.Equal(HttpStatusCode.Unauthorized, invalidResp.StatusCode);
        }

        [Fact]
        public async Task Test_01_Login_Success()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            Assert.NotEmpty(loginData.Token);
            Assert.NotEmpty(loginData.RefreshToken);
            Assert.Equal("admin@email.com", loginData.User.Email);
        }

        [Fact]
        public async Task Test_01_Redis_Session_Created()
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
        public async Task Test_01_Refresh_Token()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            var refreshResp = await _client.PostAsJsonAsync("/v1/auth/refresh", new { refreshToken = loginData.RefreshToken });
            Assert.Equal(HttpStatusCode.OK, refreshResp.StatusCode);
            var refreshData = await refreshResp.Content.ReadFromJsonAsync<LoginResponse>();
            Assert.NotNull(refreshData);
            Assert.NotEmpty(refreshData.Token);
        }

        [Fact]
        public async Task Test_01_Invalid_Tokens_Return_Unauthorized_Error()
        {
            SetAuthHeader("invalid-token");
            var meInvalidResp = await _client.GetAsync("/v1/auth/me");
            Assert.Equal(HttpStatusCode.Unauthorized, meInvalidResp.StatusCode);
            ClearAuthHeader();
        }

        [Fact]
        public async Task Test_01_Get_Me_Structure()
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
        public async Task Test_01_Session_Invalidation_On_Mutation()
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
        public async Task Test_01_Login_Inactive_User()
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
        public async Task Test_01_Login_Inactive_Role()
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

        // ==========================================
        // --- 02. RBAC Permissions (2 tests) -------
        // ==========================================

        [Fact]
        public async Task Test_02_Rbac_Forbidden_Action()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var uniqueId = Guid.NewGuid().ToString().Substring(0, 8);
            var roleId = "restricted-role";
            var createRoleResp = await _client.PostAsJsonAsync("/v1/role", new
            {
                name = "Restricted Role",
                description = "Role with zero permissions",
                permissions = new List<object>()
            });
            Assert.Contains(createRoleResp.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK, HttpStatusCode.BadRequest });

            var restrictedEmail = $"restricted_{uniqueId}@email.com";
            var createUserResp = await _client.PostAsJsonAsync("/v1/user", new
            {
                name = "Restricted User",
                email = restrictedEmail,
                password = "Password123!",
                id_role = roleId
            });
            Assert.Contains(createUserResp.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });

            var restrictedLogin = await LoginAsync(restrictedEmail, "Password123!");
            SetAuthHeader(restrictedLogin.Token);

            var usersResp = await _client.GetAsync("/v1/user");
            Assert.Equal(HttpStatusCode.Forbidden, usersResp.StatusCode);

            ClearAuthHeader();
        }

        [Fact]
        public async Task Test_02_Rbac_Allowed_Action()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var uniqueId = Guid.NewGuid().ToString().Substring(0, 8);
            var rolePayload = new
            {
                name = $"Allowed Role {uniqueId}",
                description = "Role with permissions to view users",
                permissions = new[]
                {
                    new
                    {
                        id_feature = "user",
                        create = false,
                        view = true,
                        delete = false,
                        activate = false
                    }
                }
            };
            var createRoleResp = await _client.PostAsJsonAsync("/v1/role", rolePayload);
            Assert.Contains(createRoleResp.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });
            var roleData = await createRoleResp.Content.ReadFromJsonAsync<JsonElement>();
            var roleId = roleData.GetProperty("id").GetString()!;

            var email = $"allowed_{uniqueId}@email.com";
            var createUserResp = await _client.PostAsJsonAsync("/v1/user", new
            {
                name = "Allowed User",
                email = email,
                password = "Password123!",
                id_role = roleId
            });
            Assert.Contains(createUserResp.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });

            var allowedLogin = await LoginAsync(email, "Password123!");
            SetAuthHeader(allowedLogin.Token);

            var usersResp = await _client.GetAsync("/v1/user?page=0&size=5");
            Assert.Equal(HttpStatusCode.OK, usersResp.StatusCode);

            ClearAuthHeader();
        }

        // ==========================================
        // --- 03. Schema Validation (2 tests) ------
        // ==========================================

        [Fact]
        public async Task Test_03_Schema_Missing_Required_Field()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var invalidRoleResp = await _client.PostAsJsonAsync("/v1/role", new
            {
                description = "Missing name role"
            });
            Assert.Equal(HttpStatusCode.BadRequest, invalidRoleResp.StatusCode);

            ClearAuthHeader();
        }

        [Fact]
        public async Task Test_03_Schema_Unknown_Field_Rejection()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var rolePayload = new Dictionary<string, object>
            {
                { "name", $"Strict Role {uniqueSuffix}" },
                { "description", "Role to test strict schema" },
                { "permissions", new List<object>() },
                { "hacker_field", "This should be ignored or rejected" }
            };

            var resp = await _client.PostAsJsonAsync("/v1/role", rolePayload);
            Assert.Contains(resp.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Created, HttpStatusCode.BadRequest });

            if (resp.StatusCode == HttpStatusCode.OK || resp.StatusCode == HttpStatusCode.Created)
            {
                var body = await resp.Content.ReadAsStringAsync();
                Assert.DoesNotContain("hacker_field", body);
            }

            ClearAuthHeader();
        }

        // ==========================================
        // --- 04. Dynamic Filters (9 tests) --------
        // ==========================================

        [Fact]
        public async Task Test_04_Dynamic_Filter_Success()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var url = "/v1/user/all?page=0&size=25&searchWord=Admin&searchFields=name,email,Role.name&orderBy=name&orderDirection=asc";
            var resp = await _client.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var data = await resp.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(data.TryGetProperty("items", out _));
            Assert.True(data.TryGetProperty("total", out _));
            Assert.True(data.TryGetProperty("page", out _));

            ClearAuthHeader();
        }

        [Fact]
        public async Task Test_04_Dynamic_Filter_Missing_Search_Fields()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var url = "/v1/user/all?page=0&size=25&searchWord=Admin&orderBy=name&orderDirection=asc";
            var resp = await _client.GetAsync(url);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

            ClearAuthHeader();
        }

        [Fact]
        public async Task Test_04_Dynamic_Filter_Unmapped_Search_Field()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var url = "/v1/user/all?page=0&size=25&searchWord=Admin&searchFields=password&orderBy=name&orderDirection=asc";
            var resp = await _client.GetAsync(url);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

            ClearAuthHeader();
        }

        [Fact]
        public async Task Test_04_Dynamic_Filter_Unallowed_Filter_Key()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var url = "/v1/user/all?invalid_filter_parameter=123";
            var resp = await _client.GetAsync(url);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

            ClearAuthHeader();
        }

        [Fact]
        public async Task Test_04_Dynamic_Filter_Invalid_Date_Format()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var url = "/v1/user/all?createdAt_start=2024-invalid-format";
            var resp = await _client.GetAsync(url);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

            ClearAuthHeader();
        }

        [Fact]
        public async Task Test_04_Dynamic_Filter_Date_Range()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            using (var scope = _fixture.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var user = await dbContext.User.FirstOrDefaultAsync();
                Assert.NotNull(user);
                var dateStr = user.CreatedAt.ToString("yyyy-MM-dd");

                var url = $"/v1/user/all?page=0&size=25&createdAt_start={dateStr}&createdAt_end={dateStr}";
                var resp = await _client.GetAsync(url);
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                var data = await resp.Content.ReadFromJsonAsync<JsonElement>();
                Assert.True(data.TryGetProperty("items", out _));
                Assert.True(data.GetProperty("items").GetArrayLength() > 0);
            }

            ClearAuthHeader();
        }

        [Fact]
        public async Task Test_04_Dynamic_Filter_Active_Status()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var rolePayload = new
            {
                name = $"Filter Test Role {uniqueSuffix}",
                description = "Role for filtering tests",
                permissions = new List<object>()
            };
            var roleResp = await _client.PostAsJsonAsync("/v1/role", rolePayload);
            Assert.Contains(roleResp.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });
            var roleData = await roleResp.Content.ReadFromJsonAsync<JsonElement>();
            var roleId = roleData.GetProperty("id").GetString()!;

            var email = $"filter_test_{uniqueSuffix}@email.com";
            var userPayload = new
            {
                name = $"Filter Test User {uniqueSuffix}",
                email = email,
                password = "Password123!",
                id_role = roleId,
                active = true
            };
            var userResp = await _client.PostAsJsonAsync("/v1/user", userPayload);
            Assert.Contains(userResp.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });
            var userData = await userResp.Content.ReadFromJsonAsync<JsonElement>();
            var userId = userData.GetProperty("id").GetString()!;

            try
            {
                var defaultAllResp = await _client.GetAsync("/v1/user/all?page=0&size=100");
                Assert.Equal(HttpStatusCode.OK, defaultAllResp.StatusCode);
                var defaultAllData = await defaultAllResp.Content.ReadFromJsonAsync<PaginatedResponse<UserResponse>>();
                Assert.Contains(defaultAllData!.Items, u => u.Id == userId);

                var defaultRootResp = await _client.GetAsync("/v1/user?page=0&size=100");
                Assert.Equal(HttpStatusCode.OK, defaultRootResp.StatusCode);
                var defaultRootData = await defaultRootResp.Content.ReadFromJsonAsync<PaginatedResponse<UserResponse>>();
                Assert.Contains(defaultRootData!.Items, u => u.Id == userId);

                // Deactivate user
                var deactResp = await _client.PatchAsJsonAsync($"/v1/user/{userId}/status", new { active = false });
                Assert.Equal(HttpStatusCode.OK, deactResp.StatusCode);

                // Query root without active param: deactivated user should NOT be present
                var rootNoParamResp = await _client.GetAsync("/v1/user?page=0&size=100");
                Assert.Equal(HttpStatusCode.OK, rootNoParamResp.StatusCode);
                var rootNoParamData = await rootNoParamResp.Content.ReadFromJsonAsync<PaginatedResponse<UserResponse>>();
                Assert.DoesNotContain(rootNoParamData!.Items, u => u.Id == userId);

                // Query all without active param: deactivated user SHOULD be present
                var allNoParamResp = await _client.GetAsync("/v1/user/all?page=0&size=100");
                Assert.Equal(HttpStatusCode.OK, allNoParamResp.StatusCode);
                var allNoParamData = await allNoParamResp.Content.ReadFromJsonAsync<PaginatedResponse<UserResponse>>();
                Assert.Contains(allNoParamData!.Items, u => u.Id == userId);

                // Query active=true: user should NOT be present
                var activeResp = await _client.GetAsync("/v1/user/all?page=0&size=100&active=true");
                Assert.Equal(HttpStatusCode.OK, activeResp.StatusCode);
                var activeData = await activeResp.Content.ReadFromJsonAsync<PaginatedResponse<UserResponse>>();
                Assert.DoesNotContain(activeData!.Items, u => u.Id == userId);

                // Query active=false: user should be present
                var inactiveResp = await _client.GetAsync("/v1/user/all?page=0&size=100&active=false");
                Assert.Equal(HttpStatusCode.OK, inactiveResp.StatusCode);
                var inactiveData = await inactiveResp.Content.ReadFromJsonAsync<PaginatedResponse<UserResponse>>();
                Assert.Contains(inactiveData!.Items, u => u.Id == userId);
            }
            finally
            {
                await _client.DeleteAsync($"/v1/user/{userId}");
                await _client.DeleteAsync($"/v1/role/{roleId}");
            }

            ClearAuthHeader();
        }

        [Fact]
        public async Task Test_04_Pagination_Size_Limit()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var respOk = await _client.GetAsync("/v1/user/all?page=0&size=100");
            Assert.Equal(HttpStatusCode.OK, respOk.StatusCode);

            var respBad = await _client.GetAsync("/v1/user/all?page=0&size=101");
            Assert.Equal(HttpStatusCode.BadRequest, respBad.StatusCode);

            var respRootOk = await _client.GetAsync("/v1/user?page=0&size=100");
            Assert.Equal(HttpStatusCode.OK, respRootOk.StatusCode);

            var respRootBad = await _client.GetAsync("/v1/user?page=0&size=101");
            Assert.Equal(HttpStatusCode.BadRequest, respRootBad.StatusCode);

            ClearAuthHeader();
        }

        [Fact]
        public async Task Test_04_Listing_Sorting()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var respAsc = await _client.GetAsync("/v1/user/all?page=0&size=100&orderBy=name&orderDirection=asc");
            Assert.Equal(HttpStatusCode.OK, respAsc.StatusCode);
            var dataAsc = await respAsc.Content.ReadFromJsonAsync<PaginatedResponse<UserResponse>>();
            Assert.NotNull(dataAsc);
            var namesAsc = new List<string>();
            foreach (var item in dataAsc.Items) namesAsc.Add(item.Name.ToLower());
            var expectedAsc = new List<string>(namesAsc);
            expectedAsc.Sort();
            Assert.Equal(expectedAsc, namesAsc);

            var respDesc = await _client.GetAsync("/v1/user/all?page=0&size=100&orderBy=name&orderDirection=desc");
            Assert.Equal(HttpStatusCode.OK, respDesc.StatusCode);
            var dataDesc = await respDesc.Content.ReadFromJsonAsync<PaginatedResponse<UserResponse>>();
            Assert.NotNull(dataDesc);
            var namesDesc = new List<string>();
            foreach (var item in dataDesc.Items) namesDesc.Add(item.Name.ToLower());
            var expectedDesc = new List<string>(namesDesc);
            expectedDesc.Sort();
            expectedDesc.Reverse();
            Assert.Equal(expectedDesc, namesDesc);

            var respBad = await _client.GetAsync("/v1/user/all?page=0&size=10&orderBy=invalid_column_name");
            Assert.Equal(HttpStatusCode.BadRequest, respBad.StatusCode);

            ClearAuthHeader();
        }

        // ==========================================
        // --- 05. Audit Logs (3 tests) -------------
        // ==========================================

        [Fact]
        public async Task Test_05_Audit_Log_Created_On_Mutation()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var uniqueId = Guid.NewGuid().ToString().Substring(0, 8);
            var productName = $"Audit Product {uniqueId}";

            var createProdResp = await _client.PostAsJsonAsync("/v1/product", new
            {
                name = productName,
                sku = $"sku-audit-{uniqueId}",
                category = "audit-cat",
                description = "Testing audit",
                price = 19.99
            });
            Assert.Equal(HttpStatusCode.Created, createProdResp.StatusCode);

            await Task.Delay(600);
            using (var scope = _fixture.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var auditLog = await dbContext.Audit
                    .FirstOrDefaultAsync(a => a.TableName == "Product" && a.ExecuteType == "POST");
                Assert.NotNull(auditLog);
                Assert.Equal(loginData.User.Id, auditLog.IdUser);
            }

            ClearAuthHeader();
        }

        [Fact]
        public async Task Test_05_Audit_Log_Ignores_Unauthenticated_Requests()
        {
            ClearAuthHeader();
            var payload = new { email = "invalid@example.com", password = "wrong" };
            await _client.PostAsJsonAsync("/v1/auth/login", payload);

            await Task.Delay(600);
            using (var scope = _fixture.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var auditLog = await dbContext.Audit
                    .FirstOrDefaultAsync(a => a.Method == "POST" && a.OriginalUrl == "/v1/auth/login");
                Assert.Null(auditLog);
            }
        }

        [Fact]
        public async Task Test_05_Audit_Log_Password_Scrubbing()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var testPassword = $"SuperSecret{uniqueSuffix}!";

            using (var scope = _fixture.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var role = await dbContext.Role.FirstOrDefaultAsync();
                Assert.NotNull(role);

                var payload = new
                {
                    name = "Audit Test User",
                    email = $"audit_test_{uniqueSuffix}@email.com",
                    password = testPassword,
                    id_role = role.Id
                };

                var resp = await _client.PostAsJsonAsync("/v1/user", payload);
                Assert.Contains(resp.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });

                await Task.Delay(600);
                var auditRecord = await dbContext.Audit
                    .FirstOrDefaultAsync(a => a.TableName == "User" && a.ExecuteType == "POST" && a.Params!.Contains($"audit_test_{uniqueSuffix}"));
                Assert.NotNull(auditRecord);

                var rawData = auditRecord.Raw ?? "";
                var paramsData = auditRecord.Params ?? "";

                Assert.DoesNotContain(testPassword, rawData);
                Assert.DoesNotContain(testPassword, paramsData);
            }

            ClearAuthHeader();
        }

        // ==========================================
        // --- 06. Soft Delete (2 tests) ------------
        // ==========================================

        [Fact]
        public async Task Test_06_Lgpd_User_Anonymization()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var email = $"anonymize_{uniqueSuffix}@email.com";
            var createUserResp = await _client.PostAsJsonAsync("/v1/user", new
            {
                name = "LGPD Test User",
                email = email,
                password = "Password123!",
                id_role = "administrator"
            });
            Assert.Contains(createUserResp.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });
            var userData = await createUserResp.Content.ReadFromJsonAsync<JsonElement>();
            var userId = userData.GetProperty("id").GetString()!;

            // Delete user
            var deleteResp = await _client.DeleteAsync($"/v1/user/{userId}");
            Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

            // Verify database contains anonymized values and is soft deleted
            using (var scope = _fixture.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var dbUser = await dbContext.User
                    .IgnoreQueryFilters()
                    .Include(u => u.Auth)
                    .FirstOrDefaultAsync(u => u.Id == userId);
                
                Assert.NotNull(dbUser);
                Assert.True(dbUser.IsDeleted);
                Assert.Contains("deleted-", dbUser.Email);
                Assert.Contains("anonymized", dbUser.Email);
                Assert.Equal("Deleted User", dbUser.Name);
                Assert.NotNull(dbUser.DeletedAt);
            }

            ClearAuthHeader();
        }

        [Fact]
        public async Task Test_06_Soft_Delete_Behavior()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var uniqueId = Guid.NewGuid().ToString().Substring(0, 8);
            var productName = $"Soft Delete Product {uniqueId}";

            var createProdResp = await _client.PostAsJsonAsync("/v1/product", new
            {
                name = productName,
                sku = $"sku-soft-{uniqueId}",
                category = "soft-cat",
                description = "Testing soft delete",
                price = 45.50
            });
            var product = await createProdResp.Content.ReadFromJsonAsync<ProductResponse>();
            Assert.NotNull(product);

            var deleteResp = await _client.DeleteAsync($"/v1/product/{product.Id}");
            Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

            using (var scope = _fixture.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var dbProduct = await dbContext.Product
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(p => p.Id == product.Id);
                
                Assert.NotNull(dbProduct);
                Assert.True(dbProduct.IsDeleted);
                Assert.NotNull(dbProduct.DeletedAt);
            }

            ClearAuthHeader();
        }

        // ==========================================
        // --- 09. Status (9 tests) -----------------
        // ==========================================

        [Fact]
        public async Task Test_09_Toggle_Product_Status_By_Admin()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var uniqueId = Guid.NewGuid().ToString().Substring(0, 8);
            var createProdResp = await _client.PostAsJsonAsync("/v1/product", new
            {
                name = $"Toggle Product {uniqueId}",
                sku = $"sku-toggle-{uniqueId}",
                category = "toggle-cat",
                description = "Testing status toggle",
                price = 100.00
            });
            var product = await createProdResp.Content.ReadFromJsonAsync<ProductResponse>();
            Assert.NotNull(product);
            Assert.True(product.Active);

            var toggleResp = await _client.PatchAsJsonAsync($"/v1/product/{product.Id}/status", new { active = false });
            Assert.Equal(HttpStatusCode.OK, toggleResp.StatusCode);
            var toggledProduct = await toggleResp.Content.ReadFromJsonAsync<ProductResponse>();
            Assert.False(toggledProduct!.Active);

            ClearAuthHeader();
        }

        [Fact]
        public async Task Test_09_Toggle_Role_Status_By_Admin()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var createRoleResp = await _client.PostAsJsonAsync("/v1/role", new
            {
                name = $"Toggle Role {uniqueSuffix}",
                description = "Testing status toggle",
                permissions = new List<object>()
            });
            Assert.Contains(createRoleResp.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });
            var roleData = await createRoleResp.Content.ReadFromJsonAsync<JsonElement>();
            var roleId = roleData.GetProperty("id").GetString()!;

            var toggleResp = await _client.PatchAsJsonAsync($"/v1/role/{roleId}/status", new { active = false });
            Assert.Equal(HttpStatusCode.OK, toggleResp.StatusCode);
            var toggledRole = await toggleResp.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(toggledRole.GetProperty("active").GetBoolean());

            ClearAuthHeader();
        }

        [Fact]
        public async Task Test_09_Toggle_User_Status_By_Admin()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var email = $"toggle_user_{uniqueSuffix}@email.com";
            var createUserResp = await _client.PostAsJsonAsync("/v1/user", new
            {
                name = "Toggle User Test",
                email = email,
                password = "Password123!",
                id_role = "administrator",
                active = true
            });
            Assert.Contains(createUserResp.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });
            var userData = await createUserResp.Content.ReadFromJsonAsync<JsonElement>();
            var userId = userData.GetProperty("id").GetString()!;

            var toggleResp = await _client.PatchAsJsonAsync($"/v1/user/{userId}/status", new { active = false });
            Assert.Equal(HttpStatusCode.OK, toggleResp.StatusCode);
            var toggledUser = await toggleResp.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(toggledUser.GetProperty("active").GetBoolean());

            ClearAuthHeader();
        }

        [Fact]
        public async Task Test_09_Toggle_Product_Status_Rbac_Forbidden()
        {
            // Create a custom user that does not have permissions to activate/deactivate products
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var rolePayload = new
            {
                name = $"No Act Product Role {uniqueSuffix}",
                description = "Role without product status toggle permission",
                permissions = new[]
                {
                    new
                    {
                        id_feature = "product",
                        create = true,
                        view = true,
                        delete = false,
                        activate = false // Forbidden
                    }
                }
            };
            var roleResp = await _client.PostAsJsonAsync("/v1/role", rolePayload);
            Assert.Contains(roleResp.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });
            var roleId = (await roleResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

            var email = $"no_act_prod_{uniqueSuffix}@email.com";
            var userResp = await _client.PostAsJsonAsync("/v1/user", new
            {
                name = "No Act Product User",
                email = email,
                password = "Password123!",
                id_role = roleId
            });
            Assert.Contains(userResp.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });

            // Create a product to toggle
            var prodResp = await _client.PostAsJsonAsync("/v1/product", new
            {
                name = $"Forbidden Product {uniqueSuffix}",
                sku = $"sku-forb-{uniqueSuffix}",
                category = "forb-cat",
                description = "Product",
                price = 10.00
            });
            var product = await prodResp.Content.ReadFromJsonAsync<ProductResponse>();

            // Login as the limited user
            var userLogin = await LoginAsync(email, "Password123!");
            SetAuthHeader(userLogin.Token);

            // Attempt status toggle; must return 403
            var toggleResp = await _client.PatchAsJsonAsync($"/v1/product/{product!.Id}/status", new { active = false });
            Assert.Equal(HttpStatusCode.Forbidden, toggleResp.StatusCode);

            ClearAuthHeader();
        }

        [Fact]
        public async Task Test_09_Toggle_Product_Status_Rbac_Allowed()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var rolePayload = new
            {
                name = $"Act Product Role {uniqueSuffix}",
                description = "Role with product status toggle permission",
                permissions = new[]
                {
                    new
                    {
                        id_feature = "product",
                        create = true,
                        view = true,
                        delete = false,
                        activate = true // Allowed
                    }
                }
            };
            var roleResp = await _client.PostAsJsonAsync("/v1/role", rolePayload);
            Assert.Contains(roleResp.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });
            var roleId = (await roleResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

            var email = $"act_prod_{uniqueSuffix}@email.com";
            var userResp = await _client.PostAsJsonAsync("/v1/user", new
            {
                name = "Act Product User",
                email = email,
                password = "Password123!",
                id_role = roleId
            });
            Assert.Contains(userResp.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });

            // Create a product to toggle
            var prodResp = await _client.PostAsJsonAsync("/v1/product", new
            {
                name = $"Allowed Product {uniqueSuffix}",
                sku = $"sku-allw-{uniqueSuffix}",
                category = "allw-cat",
                description = "Product",
                price = 10.00
            });
            var product = await prodResp.Content.ReadFromJsonAsync<ProductResponse>();

            // Login as the limited user
            var userLogin = await LoginAsync(email, "Password123!");
            SetAuthHeader(userLogin.Token);

            // Attempt status toggle; must succeed (200)
            var toggleResp = await _client.PatchAsJsonAsync($"/v1/product/{product!.Id}/status", new { active = false });
            Assert.Equal(HttpStatusCode.OK, toggleResp.StatusCode);

            ClearAuthHeader();
        }

        [Fact]
        public async Task Test_09_Toggle_Role_Status_Rbac_Forbidden()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var rolePayload = new
            {
                name = $"No Act Role Role {uniqueSuffix}",
                permissions = new[]
                {
                    new
                    {
                        id_feature = "role",
                        create = true,
                        view = true,
                        delete = false,
                        activate = false // Forbidden
                    }
                }
            };
            var roleResp = await _client.PostAsJsonAsync("/v1/role", rolePayload);
            Assert.Contains(roleResp.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });
            var roleId = (await roleResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

            var email = $"no_act_role_{uniqueSuffix}@email.com";
            var userResp = await _client.PostAsJsonAsync("/v1/user", new
            {
                name = "No Act Role User",
                email = email,
                password = "Password123!",
                id_role = roleId
            });
            Assert.Contains(userResp.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });

            // Login as the limited user
            var userLogin = await LoginAsync(email, "Password123!");
            SetAuthHeader(userLogin.Token);

            // Attempt status toggle on role; must return 403
            var toggleResp = await _client.PatchAsJsonAsync($"/v1/role/{roleId}/status", new { active = false });
            Assert.Equal(HttpStatusCode.Forbidden, toggleResp.StatusCode);

            ClearAuthHeader();
        }

        [Fact]
        public async Task Test_09_Toggle_Role_Status_Rbac_Allowed()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var rolePayload = new
            {
                name = $"Act Role Role {uniqueSuffix}",
                permissions = new[]
                {
                    new
                    {
                        id_feature = "role",
                        create = true,
                        view = true,
                        delete = false,
                        activate = true // Allowed
                    }
                }
            };
            var roleResp = await _client.PostAsJsonAsync("/v1/role", rolePayload);
            Assert.Contains(roleResp.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });
            var roleId = (await roleResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

            var email = $"act_role_{uniqueSuffix}@email.com";
            var userResp = await _client.PostAsJsonAsync("/v1/user", new
            {
                name = "Act Role User",
                email = email,
                password = "Password123!",
                id_role = roleId
            });
            Assert.Contains(userResp.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });

            // Login as the limited user
            var userLogin = await LoginAsync(email, "Password123!");
            SetAuthHeader(userLogin.Token);

            // Attempt status toggle on role; must succeed (200)
            var toggleResp = await _client.PatchAsJsonAsync($"/v1/role/{roleId}/status", new { active = false });
            Assert.Equal(HttpStatusCode.OK, toggleResp.StatusCode);

            ClearAuthHeader();
        }

        [Fact]
        public async Task Test_09_Toggle_User_Status_Rbac_Forbidden()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var rolePayload = new
            {
                name = $"No Act User Role {uniqueSuffix}",
                permissions = new[]
                {
                    new
                    {
                        id_feature = "user",
                        create = true,
                        view = true,
                        delete = false,
                        activate = false // Forbidden
                    }
                }
            };
            var roleResp = await _client.PostAsJsonAsync("/v1/role", rolePayload);
            Assert.Contains(roleResp.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });
            var roleId = (await roleResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

            var email = $"no_act_user_{uniqueSuffix}@email.com";
            var userResp = await _client.PostAsJsonAsync("/v1/user", new
            {
                name = "No Act User User",
                email = email,
                password = "Password123!",
                id_role = roleId
            });
            Assert.Contains(userResp.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });
            var userId = (await userResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

            // Login as the limited user
            var userLogin = await LoginAsync(email, "Password123!");
            SetAuthHeader(userLogin.Token);

            // Attempt status toggle on user; must return 403
            var toggleResp = await _client.PatchAsJsonAsync($"/v1/user/{userId}/status", new { active = false });
            Assert.Equal(HttpStatusCode.Forbidden, toggleResp.StatusCode);

            ClearAuthHeader();
        }

        [Fact]
        public async Task Test_09_Toggle_User_Status_Rbac_Allowed()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var rolePayload = new
            {
                name = $"Act User Role {uniqueSuffix}",
                permissions = new[]
                {
                    new
                    {
                        id_feature = "user",
                        create = true,
                        view = true,
                        delete = false,
                        activate = true // Allowed
                    }
                }
            };
            var roleResp = await _client.PostAsJsonAsync("/v1/role", rolePayload);
            Assert.Contains(roleResp.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });
            var roleId = (await roleResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

            var email = $"act_user_{uniqueSuffix}@email.com";
            var userResp = await _client.PostAsJsonAsync("/v1/user", new
            {
                name = "Act User User",
                email = email,
                password = "Password123!",
                id_role = roleId
            });
            Assert.Contains(userResp.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });
            var userId = (await userResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

            // Login as the limited user
            var userLogin = await LoginAsync(email, "Password123!");
            SetAuthHeader(userLogin.Token);

            // Attempt status toggle on user; must succeed (200)
            var toggleResp = await _client.PatchAsJsonAsync($"/v1/user/{userId}/status", new { active = false });
            Assert.Equal(HttpStatusCode.OK, toggleResp.StatusCode);

            ClearAuthHeader();
        }

        // ==========================================
        // --- 10. Role Features (4 tests) ----------
        // ==========================================

        [Fact]
        public async Task Test_10_List_Role_Features_By_Admin()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var resp = await _client.GetAsync("/v1/role/features");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var features = await resp.Content.ReadFromJsonAsync<List<JsonElement>>();
            Assert.NotNull(features);
            Assert.True(features.Count > 0);

            var featureIds = new HashSet<string>();
            foreach (var f in features)
            {
                Assert.True(f.TryGetProperty("id", out _));
                Assert.True(f.TryGetProperty("name", out _));
                featureIds.Add(f.GetProperty("id").GetString()!);
            }

            Assert.Contains("product", featureIds);
            Assert.Contains("role", featureIds);
            Assert.Contains("user", featureIds);

            ClearAuthHeader();
        }

        [Fact]
        public async Task Test_10_List_Role_Features_Rbac_Forbidden()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);
            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);

            var rolePayload = new
            {
                name = $"No Features Role {uniqueSuffix}",
                description = "Test Role",
                permissions = new[]
                {
                    new
                    {
                        id_feature = "role",
                        create = false,
                        view = false, // Forbidden
                        delete = false,
                        activate = false
                    }
                }
            };
            var roleResp = await _client.PostAsJsonAsync("/v1/role", rolePayload);
            Assert.Contains(roleResp.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });
            var roleId = (await roleResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

            var email = $"no_features_user_{uniqueSuffix}@email.com";
            var createUserResp = await _client.PostAsJsonAsync("/v1/user", new
            {
                name = "No Features User",
                email = email,
                password = "Password123!",
                id_role = roleId,
                active = true
            });
            Assert.Contains(createUserResp.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });

            var loginResp = await _client.PostAsJsonAsync("/v1/auth/login", new { email = email, password = "Password123!" });
            var token = (await loginResp.Content.ReadFromJsonAsync<LoginResponse>())!.Token;

            SetAuthHeader(token);
            var resp = await _client.GetAsync("/v1/role/features");
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);

            ClearAuthHeader();
        }

        [Fact]
        public async Task Test_10_List_Role_Features_Rbac_Allowed()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);
            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);

            var rolePayload = new
            {
                name = $"Features Role {uniqueSuffix}",
                description = "Test Role",
                permissions = new[]
                {
                    new
                    {
                        id_feature = "role",
                        create = false,
                        view = true, // Allowed
                        delete = false,
                        activate = false
                    }
                }
            };
            var roleResp = await _client.PostAsJsonAsync("/v1/role", rolePayload);
            Assert.Contains(roleResp.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });
            var roleId = (await roleResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

            var email = $"features_user_{uniqueSuffix}@email.com";
            var createUserResp = await _client.PostAsJsonAsync("/v1/user", new
            {
                name = "Features User",
                email = email,
                password = "Password123!",
                id_role = roleId,
                active = true
            });
            Assert.Contains(createUserResp.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });

            var loginResp = await _client.PostAsJsonAsync("/v1/auth/login", new { email = email, password = "Password123!" });
            var token = (await loginResp.Content.ReadFromJsonAsync<LoginResponse>())!.Token;

            SetAuthHeader(token);
            var resp = await _client.GetAsync("/v1/role/features");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var features = await resp.Content.ReadFromJsonAsync<List<JsonElement>>();
            Assert.NotNull(features);

            ClearAuthHeader();
        }

        [Fact]
        public async Task Test_10_Get_Role_By_Id_Schema_Compliance()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);
            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);

            var rolePayload = new
            {
                name = $"Schema Role {uniqueSuffix}",
                description = "Verification Role",
                permissions = new[]
                {
                    new
                    {
                        id_feature = "product",
                        create = true,
                        view = true,
                        delete = false,
                        activate = false
                    }
                }
            };
            var roleResp = await _client.PostAsJsonAsync("/v1/role", rolePayload);
            Assert.Contains(roleResp.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });
            var roleId = (await roleResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

            var getResp = await _client.GetAsync($"/v1/role/{roleId}");
            Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
            var data = await getResp.Content.ReadFromJsonAsync<JsonElement>();

            Assert.True(data.TryGetProperty("id", out _));
            Assert.True(data.TryGetProperty("name", out _));
            Assert.True(data.TryGetProperty("description", out _));
            Assert.True(data.TryGetProperty("active", out _));
            Assert.True(data.TryGetProperty("created_at", out _));
            Assert.True(data.TryGetProperty("updated_at", out _));
            Assert.True(data.TryGetProperty("is_deleted", out _));
            Assert.True(data.TryGetProperty("deleted_at", out _));
            Assert.True(data.TryGetProperty("RoleFeature", out _));

            var roleFeatures = data.GetProperty("RoleFeature");
            Assert.Equal(JsonValueKind.Array, roleFeatures.ValueKind);
            Assert.True(roleFeatures.GetArrayLength() == 1);
            var rf = roleFeatures[0];
            Assert.Equal("product", rf.GetProperty("id_feature").GetString());
            Assert.True(rf.GetProperty("create").GetBoolean());
            Assert.True(rf.GetProperty("view").GetBoolean());
            Assert.False(rf.GetProperty("delete").GetBoolean());
            Assert.False(rf.GetProperty("activate").GetBoolean());

            ClearAuthHeader();
        }

        // ==========================================
        // --- 11. Session Invalidation (4 tests) ---
        // ==========================================

        [Fact]
        public async Task Test_11_Session_Invalidated_On_Role_Deactivation()
        {
            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var (roleId, userId, token) = await CreateRoleAndUserClientAsync(uniqueSuffix);

            // Verify user token is active
            SetAuthHeader(token);
            var meResp = await _client.GetAsync("/v1/auth/me");
            Assert.Equal(HttpStatusCode.OK, meResp.StatusCode);

            // Deactivate the role using admin token
            var adminLogin = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(adminLogin.Token);
            var statusResp = await _client.PatchAsJsonAsync($"/v1/role/{roleId}/status", new { active = false });
            Assert.Equal(HttpStatusCode.OK, statusResp.StatusCode);

            await Task.Delay(600);

            // Verify user token is now invalidated
            SetAuthHeader(token);
            var meResp2 = await _client.GetAsync("/v1/auth/me");
            Assert.Equal(HttpStatusCode.Unauthorized, meResp2.StatusCode);

            ClearAuthHeader();
        }

        [Fact]
        public async Task Test_11_Session_Invalidated_On_Role_Update()
        {
            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var (roleId, userId, token) = await CreateRoleAndUserClientAsync(uniqueSuffix);

            // Verify user token is active
            SetAuthHeader(token);
            var meResp = await _client.GetAsync("/v1/auth/me");
            Assert.Equal(HttpStatusCode.OK, meResp.StatusCode);

            // Update role using admin token
            var adminLogin = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(adminLogin.Token);
            var updatePayload = new
            {
                name = $"Tester Role Updated {uniqueSuffix}",
                description = "Updated Role",
                permissions = new[]
                {
                    new
                    {
                        id_feature = "product",
                        create = false,
                        view = true,
                        delete = false,
                        activate = false
                    }
                }
            };
            var updateResp = await _client.PutAsJsonAsync($"/v1/role/{roleId}", updatePayload);
            Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);

            await Task.Delay(600);

            // Verify user token is now invalidated
            SetAuthHeader(token);
            var meResp2 = await _client.GetAsync("/v1/auth/me");
            Assert.Equal(HttpStatusCode.Unauthorized, meResp2.StatusCode);

            ClearAuthHeader();
        }

        [Fact]
        public async Task Test_11_Session_Invalidated_On_User_Deactivation()
        {
            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var (roleId, userId, token) = await CreateRoleAndUserClientAsync(uniqueSuffix);

            // Verify user token is active
            SetAuthHeader(token);
            var meResp = await _client.GetAsync("/v1/auth/me");
            Assert.Equal(HttpStatusCode.OK, meResp.StatusCode);

            // Deactivate the user using admin token
            var adminLogin = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(adminLogin.Token);
            var statusResp = await _client.PatchAsJsonAsync($"/v1/user/{userId}/status", new { active = false });
            Assert.Equal(HttpStatusCode.OK, statusResp.StatusCode);

            await Task.Delay(600);

            // Verify user token is now invalidated
            SetAuthHeader(token);
            var meResp2 = await _client.GetAsync("/v1/auth/me");
            Assert.Equal(HttpStatusCode.Unauthorized, meResp2.StatusCode);

            ClearAuthHeader();
        }

        [Fact]
        public async Task Test_11_Session_Invalidated_On_User_Update()
        {
            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var (roleId, userId, token) = await CreateRoleAndUserClientAsync(uniqueSuffix);

            // Verify user token is active
            SetAuthHeader(token);
            var meResp = await _client.GetAsync("/v1/auth/me");
            Assert.Equal(HttpStatusCode.OK, meResp.StatusCode);

            // Update user using admin token
            var adminLogin = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(adminLogin.Token);
            var updatePayload = new
            {
                name = "Session Tester Updated",
                email = $"session_tester_new_{uniqueSuffix}@email.com",
                id_role = roleId
            };
            var updateResp = await _client.PutAsJsonAsync($"/v1/user/{userId}", updatePayload);
            Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);

            await Task.Delay(600);

            // Verify user token is now invalidated
            SetAuthHeader(token);
            var meResp2 = await _client.GetAsync("/v1/auth/me");
            Assert.Equal(HttpStatusCode.Unauthorized, meResp2.StatusCode);

            ClearAuthHeader();
        }

        // ==========================================
        // --- 12. Error Logs (1 test) --------------
        // ==========================================

        [Fact]
        public async Task Test_12_Unhandled_Error_Logged_To_Db()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            // Send payload with missing name to trigger a ValidationException in RoleController
            var resp = await _client.PostAsJsonAsync("/v1/role", new { description = "Triggering validation error" });
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

            await Task.Delay(600);
            using (var scope = _fixture.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var errorLog = await dbContext.ErrorLog
                    .FirstOrDefaultAsync(e => e.IdUser == loginData.User.Id && e.Source.Contains("POST /v1/role"));
                Assert.NotNull(errorLog);
                Assert.NotNull(errorLog.ErrorMessage);
                Assert.Contains("validation", errorLog.ErrorMessage.ToLower());
            }

            ClearAuthHeader();
        }

        // ==========================================
        // --- 13. PDF Debug (1 test) ---------------
        // ==========================================

        [Fact]
        public async Task Test_13_Pdf_Debug_Endpoints()
        {
            var resp = await _client.GetAsync("/v1/debug/pdf");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            Assert.Equal("application/pdf", resp.Content.Headers.ContentType?.MediaType);
        }

        // Helpers classes matching API structures
        public record LoginResponse
        {
            public string Token { get; init; } = string.Empty;
            public string RefreshToken { get; init; } = string.Empty;
            public UserResponse User { get; init; } = new();
        }

        public record UserResponse
        {
            public string Id { get; init; } = string.Empty;
            public string Email { get; init; } = string.Empty;
            public string Name { get; init; } = string.Empty;
            public RoleResponse? Role { get; init; }
        }

        public record RoleResponse
        {
            public string Id { get; init; } = string.Empty;
            public string Name { get; init; } = string.Empty;
        }

        public record ProductResponse
        {
            public string Id { get; init; } = string.Empty;
            public string Name { get; init; } = string.Empty;
            public bool Active { get; init; }
        }

        public record PaginatedResponse<T>
        {
            public List<T> Items { get; init; } = new();
            public int Total { get; init; }
            public int Page { get; init; }
        }
    }
}
