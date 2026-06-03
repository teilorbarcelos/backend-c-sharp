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
    public class UserManagementTests : IntegrationTestBase
    {
        public UserManagementTests(IntegrationTestFixture fixture) : base(fixture) { }

        // ==========================================
        // --- 09. Status (9 tests) -----------------
        // ==========================================

        [Fact]
        public async Task GivenAdminUser_WhenTogglingProductStatus_ThenStatusIsChanged()
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
                price = 100.00,
                stock = 10
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
        public async Task GivenAdminUser_WhenTogglingRoleStatus_ThenStatusIsChanged()
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
        public async Task GivenAdminUser_WhenTogglingUserStatus_ThenStatusIsChanged()
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
        public async Task GivenForbiddenUser_WhenTogglingProductStatus_ThenReturnsForbidden()
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
                price = 10.00,
                stock = 10
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
        public async Task GivenAllowedUser_WhenTogglingProductStatus_ThenStatusIsChanged()
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
                price = 10.00,
                stock = 10
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
        public async Task GivenForbiddenUser_WhenTogglingRoleStatus_ThenReturnsForbidden()
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
        public async Task GivenAllowedUser_WhenTogglingRoleStatus_ThenStatusIsChanged()
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
        public async Task GivenForbiddenUser_WhenTogglingUserStatus_ThenReturnsForbidden()
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
        public async Task GivenAllowedUser_WhenTogglingUserStatus_ThenStatusIsChanged()
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
        // --- 10. CRUD operations (missing ops) ---
        // ==========================================

        [Fact]
        public async Task GivenAdminUser_WhenFetchingUserById_ThenReturnsUser()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var resp = await _client.GetAsync($"/v1/user/{loginData.User.Id}");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var notFoundResp = await _client.GetAsync("/v1/user/non-existent-user");
            Assert.Equal(HttpStatusCode.NotFound, notFoundResp.StatusCode);

            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenAdminUser_WhenDeletingUser_ThenSoftDeletesUser()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var createUserResp = await _client.PostAsJsonAsync("/v1/user", new
            {
                name = "Delete User Test",
                email = $"delete_user_{uniqueSuffix}@email.com",
                password = "Password123!",
                id_role = "administrator"
            });
            var userData = await createUserResp.Content.ReadFromJsonAsync<JsonElement>();
            var userId = userData.GetProperty("id").GetString()!;

            var delResp = await _client.DeleteAsync($"/v1/user/{userId}");
            Assert.Equal(HttpStatusCode.NoContent, delResp.StatusCode);

            var notFoundResp = await _client.DeleteAsync("/v1/user/non-existent-user");
            Assert.Equal(HttpStatusCode.NotFound, notFoundResp.StatusCode);

            ClearAuthHeader();
        }

        // ==========================================
        // --- 11. Session Invalidation (4 tests) ---
        // ==========================================

        [Fact]
        public async Task GivenRoleDeactivation_WhenExecuted_ThenInvalidatesUserSessions()
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
        public async Task GivenRoleUpdate_WhenExecuted_ThenInvalidatesUserSessions()
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
        public async Task GivenUserDeactivation_WhenExecuted_ThenInvalidatesSession()
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
        public async Task GivenUserUpdate_WhenExecuted_ThenInvalidatesSession()
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

        [Fact]
        public async Task GivenInvalidUserCreation_WhenCreatingOrUpdating_ThenHandlesErrors()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);

            var createNonExistentRoleResp = await _client.PostAsJsonAsync("/v1/user", new
            {
                name = "No Role User",
                email = $"norole_{uniqueSuffix}@email.com",
                password = "Password123!",
                id_role = "non-existent-role"
            });
            Assert.Equal(HttpStatusCode.BadRequest, createNonExistentRoleResp.StatusCode);

            var userEmail = $"user_errors_{uniqueSuffix}@email.com";
            var userResp = await _client.PostAsJsonAsync("/v1/user", new
            {
                name = "User Errors Test",
                email = userEmail,
                password = "Password123!",
                id_role = "administrator"
            });
            var userData = await userResp.Content.ReadFromJsonAsync<JsonElement>();
            var userId = userData.GetProperty("id").GetString()!;

            var valFailResp = await _client.PutAsJsonAsync($"/v1/user/{userId}", new { password = "123" });
            Assert.Equal(HttpStatusCode.BadRequest, valFailResp.StatusCode);

            var emailInUseResp = await _client.PutAsJsonAsync($"/v1/user/{userId}", new { email = "admin@email.com" });
            Assert.Equal(HttpStatusCode.BadRequest, emailInUseResp.StatusCode);

            var updateNonExistentRoleResp = await _client.PutAsJsonAsync($"/v1/user/{userId}", new { id_role = "non-existent-role" });
            Assert.Equal(HttpStatusCode.BadRequest, updateNonExistentRoleResp.StatusCode);

            var updatePasswordResp = await _client.PutAsJsonAsync($"/v1/user/{userId}", new { password = "NewPassword123!" });
            Assert.Equal(HttpStatusCode.OK, updatePasswordResp.StatusCode);

            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenInitialAdminUser_WhenUpdatingPasswordDeletingOrDeactivating_ThenEnforcesProtectionRules()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var adminId = loginData.User.Id;

            var updatePasswordResp = await _client.PutAsJsonAsync($"/v1/user/{adminId}", new { password = "admin@123" });
            Assert.Equal(HttpStatusCode.OK, updatePasswordResp.StatusCode);

            // Re-login because password update invalidates all active sessions
            var reloginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(reloginData.Token);

            var deleteResp = await _client.DeleteAsync($"/v1/user/{adminId}");
            Assert.Equal(HttpStatusCode.BadRequest, deleteResp.StatusCode);

            var toggleResp = await _client.PatchAsJsonAsync($"/v1/user/{adminId}/status", new { active = false });
            Assert.Equal(HttpStatusCode.BadRequest, toggleResp.StatusCode);

            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenDuplicateEmail_WhenCreatingUser_ThenReturnsBadRequest()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var createResp = await _client.PostAsJsonAsync("/v1/user", new
            {
                name = "Duplicate Email User",
                email = "admin@email.com",
                password = "Password123!",
                id_role = "administrator"
            });
            Assert.Equal(HttpStatusCode.BadRequest, createResp.StatusCode);

            var content = await createResp.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains("Email already in use.", content.GetProperty("message").GetString());

            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenInvalidUserPayload_WhenCreatingUser_ThenReturnsBadRequest()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var createResp = await _client.PostAsJsonAsync("/v1/user", new
            {
                name = "Invalid User",
                email = "not-an-email",
                password = "123",
                id_role = "administrator"
            });
            Assert.Equal(HttpStatusCode.BadRequest, createResp.StatusCode);

            ClearAuthHeader();
        }
    }
}