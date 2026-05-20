using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace MageBackend.Tests
{
    public class ProductTests : IntegrationTestBase
    {
        public ProductTests(IntegrationTestFixture fixture) : base(fixture) { }

        [Fact]
        public async Task GivenAdmin_WhenManagingProduct_ThenCreatesUpdatesAndDeletesSuccessfully()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            // Create
            var payload = new { name = "Test Product", sku = "test-sku-1", category = "cat-1", price = 100.0, stock = 10 };
            var createResp = await _client.PostAsJsonAsync("/v1/product", payload);
            Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
            var productData = await createResp.Content.ReadFromJsonAsync<JsonElement>();
            var productId = productData.GetProperty("id").GetString()!;

            // Validation Fail Create
            var createFailResp = await _client.PostAsJsonAsync("/v1/product", new { name = "" });
            Assert.Equal(HttpStatusCode.BadRequest, createFailResp.StatusCode);

            // Duplicate Create
            var createDupResp = await _client.PostAsJsonAsync("/v1/product", payload);
            Assert.Equal(HttpStatusCode.BadRequest, createDupResp.StatusCode);

            // Get By Id
            var getResp = await _client.GetAsync($"/v1/product/{productId}");
            Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);

            // Get By Id Fail
            var getFailResp = await _client.GetAsync("/v1/product/non-existent-product");
            Assert.Equal(HttpStatusCode.NotFound, getFailResp.StatusCode);

            // List
            var listResp = await _client.GetAsync("/v1/product");
            Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);

            // Update
            var updatePayload = new { name = "Updated Product", sku = "test-sku-1", category = "cat-1", price = 100.0, stock = 10 };
            var updateResp = await _client.PutAsJsonAsync($"/v1/product/{productId}", updatePayload);
            Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);

            // Update Fail NotFound
            var updateFailResp = await _client.PutAsJsonAsync("/v1/product/non-existent-product", updatePayload);
            Assert.Equal(HttpStatusCode.NotFound, updateFailResp.StatusCode);

            // Delete
            var deleteResp = await _client.DeleteAsync($"/v1/product/{productId}");
            Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

            // Delete Fail NotFound
            var deleteFailResp = await _client.DeleteAsync($"/v1/product/{productId}"); // Already deleted
            Assert.Equal(HttpStatusCode.NotFound, deleteFailResp.StatusCode);

            ClearAuthHeader();
        }
    }
}
