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
    public class ComplianceTests : IntegrationTestBase
    {
        public ComplianceTests(IntegrationTestFixture fixture) : base(fixture) { }

        // ==========================================
        // --- 05. Audit Logs (3 tests) -------------
        // ==========================================

        [Fact]
        public async Task GivenMutationAction_WhenExecuted_ThenCreatesAuditLog()
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
        public async Task GivenUnauthenticatedRequest_WhenExecuted_ThenIgnoresAuditLog()
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
        public async Task GivenMutationAction_WhenLogging_ThenScrubsPasswordsFromLogs()
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
        public async Task GivenLgpdRequest_WhenAnonymizing_ThenScramblesUserData()
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
        public async Task GivenSoftDeleteRequest_WhenDeleting_ThenMarksAsDeletedInsteadOfHardDelete()
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

    }
}