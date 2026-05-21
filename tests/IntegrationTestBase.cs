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
public abstract class IntegrationTestBase : IClassFixture<IntegrationTestFixture>
    {
        protected readonly IntegrationTestFixture _fixture;
        protected readonly HttpClient _client;

        protected IntegrationTestBase(IntegrationTestFixture fixture)
        {
            _fixture = fixture;
            _client = _fixture.CreateClient();
        }

        protected async Task<LoginResponse> LoginAsync(string email, string password)
        {
            var response = await _client.PostAsJsonAsync("/v1/auth/login", new { email, password });
            response.EnsureSuccessStatusCode();
            var data = await response.Content.ReadFromJsonAsync<LoginResponse>();
            Assert.NotNull(data);
            return data;
        }

        protected void SetAuthHeader(string token)
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        protected void ClearAuthHeader()
        {
            _client.DefaultRequestHeaders.Authorization = null;
        }

        protected async Task<(string roleId, string userId, string token)> CreateRoleAndUserClientAsync(string uniqueSuffix)
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