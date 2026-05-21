using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace MageBackend.Tests
{
    public class FeatureTests : IntegrationTestBase
    {
        public FeatureTests(IntegrationTestFixture fixture) : base(fixture) { }

        [Fact]
        public async Task GivenAdmin_WhenCreatingFeature_ThenSuccess()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var payload = new { id = "new-feature-1", name = "New Feature", description = "Test" };
            var resp = await _client.PostAsJsonAsync("/v1/feature", payload);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

            // Attempt to create duplicate
            var respDuplicate = await _client.PostAsJsonAsync("/v1/feature", payload);
            Assert.Equal(HttpStatusCode.BadRequest, respDuplicate.StatusCode);

            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenInvalidPayload_WhenCreatingFeature_ThenValidationFails()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var payload = new { id = "", name = "" };
            var resp = await _client.PostAsJsonAsync("/v1/feature", payload);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenAdmin_WhenUpdatingFeature_ThenSuccess()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var createPayload = new { id = "update-feature-1", name = "Update Feature", description = "Test" };
            await _client.PostAsJsonAsync("/v1/feature", createPayload);

            var updatePayload = new { name = "Updated Name", description = "Updated Test" };
            var resp = await _client.PutAsJsonAsync("/v1/feature/update-feature-1", updatePayload);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var validationResp = await _client.PutAsJsonAsync("/v1/feature/update-feature-1", new { name = "" });
            Assert.Equal(HttpStatusCode.BadRequest, validationResp.StatusCode);

            var notFoundResp = await _client.PutAsJsonAsync("/v1/feature/non-existent-feature", updatePayload);
            Assert.Equal(HttpStatusCode.NotFound, notFoundResp.StatusCode);

            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenAdmin_WhenFetchingFeatureById_ThenReturnsCorrectly()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var createPayload = new { id = "get-feature-1", name = "Get Feature", description = "Test" };
            await _client.PostAsJsonAsync("/v1/feature", createPayload);

            var resp = await _client.GetAsync("/v1/feature/get-feature-1");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var notFoundResp = await _client.GetAsync("/v1/feature/non-existent-feature");
            Assert.Equal(HttpStatusCode.NotFound, notFoundResp.StatusCode);

            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenAdmin_WhenDeletingFeature_ThenRemovesCorrectly()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var createPayload = new { id = "delete-feature-1", name = "Delete Feature", description = "Test" };
            await _client.PostAsJsonAsync("/v1/feature", createPayload);

            var resp = await _client.DeleteAsync("/v1/feature/delete-feature-1");
            Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

            var notFoundResp = await _client.DeleteAsync("/v1/feature/non-existent-feature");
            Assert.Equal(HttpStatusCode.NotFound, notFoundResp.StatusCode);

            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenAdmin_WhenListingFeatures_ThenReturnsPaginatedAndAll()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var respList = await _client.GetAsync("/v1/feature?size=10");
            Assert.Equal(HttpStatusCode.OK, respList.StatusCode);

            var respAll = await _client.GetAsync("/v1/feature/all");
            Assert.Equal(HttpStatusCode.OK, respAll.StatusCode);

            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenAdmin_WhenTogglingStatus_ThenStatusUpdates()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var createPayload = new { id = "toggle-feature-1", name = "Toggle Feature", description = "Test" };
            await _client.PostAsJsonAsync("/v1/feature", createPayload);

            var resp = await _client.PatchAsJsonAsync("/v1/feature/toggle-feature-1/status", new { active = false });
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var notFoundResp = await _client.PatchAsJsonAsync("/v1/feature/non-existent-feature/status", new { active = false });
            Assert.Equal(HttpStatusCode.NotFound, notFoundResp.StatusCode);

            ClearAuthHeader();
        }
    }
}
