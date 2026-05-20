using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
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
    }
}
