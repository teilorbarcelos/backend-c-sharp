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
using MageBackend.Web.Filters;
using MageBackend.Web.Middleware;
using MageBackend.Features.Auth;
using MageBackend.Features.User;
using MageBackend.Features.Role;
using MageBackend.Features.Product;
using MageBackend.Infrastructure.Auth;
using Xunit;

namespace MageBackend.Tests
{
    public class RbacTests : IntegrationTestBase
    {
        public RbacTests(IntegrationTestFixture fixture) : base(fixture) { }

        // ==========================================
        // --- 02. RBAC Permissions (2 tests) -------
        // ==========================================

        [Fact]
        public async Task GivenForbiddenAction_WhenExecuted_ThenReturnsForbidden()
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
        public async Task GivenAllowedAction_WhenExecuted_ThenProceedsSuccessfully()
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
        // --- 10. Role Features (4 tests) ----------
        // ==========================================

        [Fact]
        public async Task GivenAdminRole_WhenListingFeatures_ThenReturnsAllFeatures()
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
        public async Task GivenForbiddenUser_WhenListingFeatures_ThenReturnsForbidden()
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
        public async Task GivenAllowedUser_WhenListingFeatures_ThenReturnsAllFeatures()
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
        public async Task GivenValidRoleId_WhenFetching_ThenReturnsCompliantSchema()
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
            Assert.Equal(1, roleFeatures.GetArrayLength());
            var rf = roleFeatures[0];
            Assert.Equal("product", rf.GetProperty("id_feature").GetString());
            Assert.True(rf.GetProperty("create").GetBoolean());
            Assert.True(rf.GetProperty("view").GetBoolean());
            Assert.False(rf.GetProperty("delete").GetBoolean());
            Assert.False(rf.GetProperty("activate").GetBoolean());

            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenAdminRole_WhenUpdatingRole_ThenUpdatesSuccessfully()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);
            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);

            var rolePayload = new
            {
                name = $"Update Role {uniqueSuffix}",
                description = "To be updated",
                permissions = new List<object>()
            };
            var roleResp = await _client.PostAsJsonAsync("/v1/role", rolePayload);
            var roleId = (await roleResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

            var updatePayload = new
            {
                name = $"Updated Role {uniqueSuffix}",
                description = "Updated Description",
                permissions = new[]
                {
                    new
                    {
                        id_feature = "product",
                        create = true,
                        view = true,
                        delete = true,
                        activate = true
                    }
                }
            };
            var updateResp = await _client.PutAsJsonAsync($"/v1/role/{roleId}", updatePayload);
            Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);

            var updateFailResp = await _client.PutAsJsonAsync("/v1/role/non-existent-role", updatePayload);
            Assert.Equal(HttpStatusCode.NotFound, updateFailResp.StatusCode);

            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenAdminRole_WhenDeletingRole_ThenSoftDeletesSuccessfully()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);
            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);

            var rolePayload = new
            {
                name = $"Delete Role {uniqueSuffix}",
                description = "To be deleted",
                permissions = new List<object>()
            };
            var roleResp = await _client.PostAsJsonAsync("/v1/role", rolePayload);
            var roleId = (await roleResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

            var delResp = await _client.DeleteAsync($"/v1/role/{roleId}");
            Assert.Equal(HttpStatusCode.NoContent, delResp.StatusCode);

            var delFailResp = await _client.DeleteAsync("/v1/role/non-existent-role");
            Assert.Equal(HttpStatusCode.NotFound, delFailResp.StatusCode);

            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenUnauthenticatedUser_WhenAccessingPermissionProtectedEndpoint_ThenReturns401()
        {
            var resp = await _client.GetAsync("/v1/user");
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }

        [Fact]
        public async Task GivenAdminRole_WhenListingRoles_ThenReturnsList()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var resp = await _client.GetAsync("/v1/role?page=0&size=10");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var respAll = await _client.GetAsync("/v1/role/all");
            Assert.Equal(HttpStatusCode.OK, respAll.StatusCode);

            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenAdminRole_WhenUpdatingRoleWithoutPermissions_ThenRetainsExistingPermissions()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);
            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);

            var rolePayload = new
            {
                name = $"Update Perm Role {uniqueSuffix}",
                description = "To be updated",
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
            var roleId = (await roleResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

            var updatePayload = new
            {
                name = $"Updated Perm Role {uniqueSuffix}",
                description = "Retained permissions description"
            };
            var updateResp = await _client.PutAsJsonAsync($"/v1/role/{roleId}", updatePayload);
            Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);

            var getResp = await _client.GetAsync($"/v1/role/{roleId}");
            Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
            var data = await getResp.Content.ReadFromJsonAsync<JsonElement>();
            var roleFeatures = data.GetProperty("RoleFeature");
            Assert.Equal(1, roleFeatures.GetArrayLength());

            ClearAuthHeader();
        }

        [Fact]
        public void GivenAuthenticatedUserWithoutPermissionsClaim_WhenAuthorizing_ThenThrows403()
        {
            var attr = new CheckPermissionAttribute("product", "view");

            var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();

            // Authenticated user WITHOUT a "permissions" claim
            var claims = new List<System.Security.Claims.Claim>
            {
                new System.Security.Claims.Claim("id", "123"),
                new System.Security.Claims.Claim("email", "noperm@test.com")
            };
            var identity = new System.Security.Claims.ClaimsIdentity(claims, "TestAuth");
            httpContext.User = new System.Security.Claims.ClaimsPrincipal(identity);

            var actionContext = new Microsoft.AspNetCore.Mvc.ActionContext(
                httpContext,
                new Microsoft.AspNetCore.Routing.RouteData(),
                new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor()
            );
            var filterContext = new Microsoft.AspNetCore.Mvc.Filters.AuthorizationFilterContext(
                actionContext,
                new List<Microsoft.AspNetCore.Mvc.Filters.IFilterMetadata>()
            );

            var exception = Assert.Throws<AppException>(() => attr.OnAuthorization(filterContext));
            Assert.Equal(403, exception.StatusCode);
            Assert.Contains("Sem permissão", exception.Message);
        }

        [Fact]
        public void GivenInvalidAction_WhenAuthorizing_ThenThrows403AppException()
        {
            var attr = new CheckPermissionAttribute("product", "invalid_action");

            var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();

            var claims = new List<System.Security.Claims.Claim>
            {
                new System.Security.Claims.Claim("id", "123"),
                new System.Security.Claims.Claim("email", "test@test.com"),
                new System.Security.Claims.Claim("permissions", JsonSerializer.Serialize(new List<PermissionClaim>
                {
                    new PermissionClaim { Feature = "product", Create = true, View = true, Delete = true, Activate = true }
                }))
            };
            var identity = new System.Security.Claims.ClaimsIdentity(claims, "TestAuth");
            httpContext.User = new System.Security.Claims.ClaimsPrincipal(identity);

            var actionContext = new Microsoft.AspNetCore.Mvc.ActionContext(
                httpContext,
                new Microsoft.AspNetCore.Routing.RouteData(),
                new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor()
            );
            var filterContext = new Microsoft.AspNetCore.Mvc.Filters.AuthorizationFilterContext(
                actionContext,
                new List<Microsoft.AspNetCore.Mvc.Filters.IFilterMetadata>()
            );

            var exception = Assert.Throws<AppException>(() => attr.OnAuthorization(filterContext));
            Assert.Equal(403, exception.StatusCode);
            Assert.Contains("Sem permissão", exception.Message);
        }

        [Fact]
        public async Task GivenInvalidRolePayload_WhenUpdatingRole_ThenThrowsValidationException()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var createRoleResp = await _client.PostAsJsonAsync("/v1/role", new
            {
                name = $"Rbac Val {uniqueSuffix}",
                description = "Role for validation test",
                permissions = new List<object>()
            });
            var roleData = await createRoleResp.Content.ReadFromJsonAsync<JsonElement>();
            var roleId = roleData.GetProperty("id").GetString()!;

            // Update with invalid payload (empty name)
            var updatePayload = new
            {
                name = "", // Invalid
                description = "Updated role description"
            };
            var updateResp = await _client.PutAsJsonAsync($"/v1/role/{roleId}", updatePayload);
            Assert.Equal(HttpStatusCode.BadRequest, updateResp.StatusCode);

            ClearAuthHeader();
        }
    }
}