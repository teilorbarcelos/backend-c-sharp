using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;
using MageBackend.Features.Storage;
using MageBackend.Features.Product;
using MageBackend.Features.Product.Commands;
using MageBackend.Features.Auth;
using MageBackend.Core;
using MageBackend.Infrastructure.Storage;
using MageBackend.Database;
using MediatR;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;
using MageBackend.Core.Middleware;
using System.Security.Claims;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using MageBackend.Infrastructure.Auth;
namespace MageBackend.Tests
{
    public class InfrastructureTests : IntegrationTestBase
    {
        public InfrastructureTests(IntegrationTestFixture fixture) : base(fixture) { }

        [Fact]
        public void GivenRedisProvider_WhenInitializingWithRedisPrefixAndResetting_ThenHandlesCorrectly()
        {
            var field = typeof(RedisProvider).GetField("_lazyConnection", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.NotNull(field);

            var originalLazy = field.GetValue(null);
            try
            {
                RedisProvider.Initialize("redis://localhost:6379");
                var conn = RedisProvider.Connection;
                Assert.NotNull(conn);

                field.SetValue(null, null);
                Assert.Throws<InvalidOperationException>(() => RedisProvider.Connection);
            }
            finally
            {
                field.SetValue(null, originalLazy);
            }
        }

        [Fact]
        public void GivenEntities_WhenTestingGettersAndSetters_ThenSucceeds()
        {
            var audit = new Audit
            {
                Id = "1",
                IdUser = "user1",
                ActionType = "create",
                TableName = "product",
                Params = "prod1",
                DiffValue = "{}",
                Error = "Some error",
                CreatedAt = DateTime.UtcNow
            };
            Assert.Equal("Some error", audit.Error);

            var role = new Role { Id = "role1", Name = "Test Role" };
            var feature = new Feature { Id = "feat1", Name = "Test Feature" };
            var rf = new RoleFeature
            {
                IdRole = "role1",
                IdFeature = "feat1",
                Create = true,
                View = true,
                Delete = false,
                Activate = false,
                Role = role,
                Feature = feature
            };
            Assert.Equal(role, rf.Role);
            Assert.Equal(feature, rf.Feature);

            var user = new User
            {
                Id = "user1",
                Name = "John Doe",
                Email = "john@example.com",
                Phone = "123",
                Document = "456",
                Avatar = "avatar.png",
                IdRole = "role1",
                Active = true,
                CognitoId = "cognito-123",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            Assert.Equal("cognito-123", user.CognitoId);
        }

        [Fact]
        public async Task GivenOpenApiEndpoint_WhenRequestingSpec_ThenReturnsValidSpec()
        {
            var resp = await _client.GetAsync("/openapi/v1.json");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var json = await resp.Content.ReadAsStringAsync();
            Assert.Contains("openapi", json);
        }

        [Fact]
        public async Task GivenMalformedBody_WhenMakingPostRequest_ThenSanitizesAndHandlesError()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var content = new StringContent("{\"invalid-json: true", System.Text.Encoding.UTF8, "application/json");
            var resp = await _client.PostAsync("/v1/product", content);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

            ClearAuthHeader();
        }
    }
}
