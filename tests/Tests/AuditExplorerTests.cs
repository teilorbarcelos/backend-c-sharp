using System;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using MageBackend.Database;
using Xunit;


namespace MageBackend.Tests
{
    public class AuditExplorerTests : IntegrationTestBase
    {
        public AuditExplorerTests(IntegrationTestFixture fixture) : base(fixture) { }

        [Fact]
        public async Task GivenAdminUser_WhenFetchingLogs_ThenReturnsAuditData()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var auditResp = await _client.GetAsync("/admin/api/audit?page=0&size=15&search=user");
            Assert.Equal(HttpStatusCode.OK, auditResp.StatusCode);

            var errorResp = await _client.GetAsync("/admin/api/errors?page=0&size=15&search=test");
            Assert.Equal(HttpStatusCode.OK, errorResp.StatusCode);

            var htmlResp = await _client.GetAsync("/admin/logs");
            Assert.Equal(HttpStatusCode.OK, htmlResp.StatusCode);
            Assert.Equal("text/html", htmlResp.Content.Headers.ContentType?.MediaType);

            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenStandardUser_WhenFetchingLogs_ThenReturnsForbidden()
        {
            var (_, _, token) = await CreateRoleAndUserClientAsync("audit-explorer");
            SetAuthHeader(token);

            var auditResp = await _client.GetAsync("/admin/api/audit");
            Assert.Equal(HttpStatusCode.Forbidden, auditResp.StatusCode);

            var errorResp = await _client.GetAsync("/admin/api/errors");
            Assert.Equal(HttpStatusCode.Forbidden, errorResp.StatusCode);

            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenUnauthenticatedUser_WhenAccessingAuditExplorerEndpoints_ThenReturns401()
        {
            var auditResp = await _client.GetAsync("/admin/api/audit");
            Assert.Equal(HttpStatusCode.Unauthorized, auditResp.StatusCode);

            var errorResp = await _client.GetAsync("/admin/api/errors");
            Assert.Equal(HttpStatusCode.Unauthorized, errorResp.StatusCode);
        }

        [Fact]
        public async Task GivenAdminUser_WhenFetchingLogs_ThenDeserializesAndValidatesAllFields()
        {
            using (var scope = _fixture.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                
                var audit = new Audit
                {
                    Id = Guid.NewGuid().ToString(),
                    IdUser = "admin-id",
                    ActionType = "create",
                    TableName = "product",
                    Params = "prod-xyz",
                    DiffValue = "{}",
                    Error = "Some error",
                    CreatedAt = DateTime.UtcNow
                };
                
                var errorLog = new ErrorLog
                {
                    Id = Guid.NewGuid().ToString(),
                    IdUser = "admin-id",
                    Source = "GET /test",
                    ErrorMessage = "Test Error Message",
                    ErrorData = "{}",
                    CreatedAt = DateTime.UtcNow
                };

                dbContext.Audit.Add(audit);
                dbContext.ErrorLog.Add(errorLog);
                await dbContext.SaveChangesAsync();
            }

            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var auditResp = await _client.GetAsync("/admin/api/audit?page=0&size=10");
            Assert.Equal(HttpStatusCode.OK, auditResp.StatusCode);
            var auditData = await auditResp.Content.ReadFromJsonAsync<JsonElement>();
            var auditItems = auditData.GetProperty("items");
            Assert.True(auditItems.GetArrayLength() > 0);
            
            var firstAudit = auditItems[0];
            Assert.NotEmpty(firstAudit.GetProperty("id").GetString()!);
            Assert.NotEmpty(firstAudit.GetProperty("action_type").GetString()!);
            Assert.NotEmpty(firstAudit.GetProperty("table_name").GetString()!);
            Assert.NotEmpty(firstAudit.GetProperty("params").GetString()!);
            Assert.NotNull(firstAudit.GetProperty("diff_value").GetString());
            Assert.NotNull(firstAudit.GetProperty("error").GetString());
            Assert.NotEmpty(firstAudit.GetProperty("created_at").GetString()!);

            var errorResp = await _client.GetAsync("/admin/api/errors?page=0&size=10");
            Assert.Equal(HttpStatusCode.OK, errorResp.StatusCode);
            var errorData = await errorResp.Content.ReadFromJsonAsync<JsonElement>();
            var errorItems = errorData.GetProperty("items");
            Assert.True(errorItems.GetArrayLength() > 0);

            var firstError = errorItems[0];
            Assert.NotEmpty(firstError.GetProperty("id").GetString()!);
            Assert.NotEmpty(firstError.GetProperty("source").GetString()!);
            Assert.NotEmpty(firstError.GetProperty("error_message").GetString()!);
            Assert.NotEmpty(firstError.GetProperty("error_data").GetString()!);
            Assert.NotEmpty(firstError.GetProperty("created_at").GetString()!);

            ClearAuthHeader();
        }
    }
}
