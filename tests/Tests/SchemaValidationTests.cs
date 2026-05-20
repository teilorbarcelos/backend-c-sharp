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
    public class SchemaValidationTests : IntegrationTestBase
    {
        public SchemaValidationTests(IntegrationTestFixture fixture) : base(fixture) { }

        // ==========================================
        // --- 03. Schema Validation (2 tests) ------
        // ==========================================

        [Fact]
        public async Task GivenMissingRequiredField_WhenSubmitting_ThenReturnsBadRequest()
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
        public async Task GivenUnknownField_WhenSubmitting_ThenRejectsPayload()
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

    }
}