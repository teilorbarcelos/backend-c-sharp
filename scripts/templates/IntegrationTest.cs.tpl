using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;

namespace MageBackend.Tests
{
    public class {{EntityName}}Tests : IntegrationTestBase
    {
        public {{EntityName}}Tests(IntegrationTestFixture fixture) : base(fixture) { }

        [Fact]
        public async Task Create{{EntityName}}_ValidPayload_Returns201()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var payload = new Dictionary<string, object>
            {
{{MockPayloadCreate}}
            };

            var response = await _client.PostAsJsonAsync("/v1/{{EntityNameLower}}", payload);
            response.StatusCode.Should().Be(HttpStatusCode.Created);

            var content = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
            content.Should().NotBeNull();
            content.Should().ContainKey("id");
        }

        [Fact]
        public async Task Get{{EntityName}}_ExistingId_Returns200()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var payload = new Dictionary<string, object>
            {
{{MockPayloadCreate}}
            };

            var createResp = await _client.PostAsJsonAsync("/v1/{{EntityNameLower}}", payload);
            var created = await createResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
            var id = created!["id"].ToString();

            var response = await _client.GetAsync($"/v1/{{EntityNameLower}}/{id}");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            
            var content = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
            content!["id"].ToString().Should().Be(id);
        }

        [Fact]
        public async Task Get{{EntityName}}_NonExistingId_Returns404()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var response = await _client.GetAsync("/v1/{{EntityNameLower}}/non-existing-id");
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task List{{EntityName}}s_Returns200AndPagination()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var payload = new Dictionary<string, object>
            {
{{MockPayloadCreate}}
            };
            await _client.PostAsJsonAsync("/v1/{{EntityNameLower}}", payload);

            var response = await _client.GetAsync("/v1/{{EntityNameLower}}?page=0&size=10");
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var content = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
            content.Should().ContainKey("items");
            content.Should().ContainKey("total");
        }

        [Fact]
        public async Task ListAll{{EntityName}}s_Returns200()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var payload = new Dictionary<string, object>
            {
{{MockPayloadCreate}}
            };
            await _client.PostAsJsonAsync("/v1/{{EntityNameLower}}", payload);

            var response = await _client.GetAsync("/v1/{{EntityNameLower}}/all?page=0&size=10");
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var content = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
            content.Should().ContainKey("items");
        }

        [Fact]
        public async Task Update{{EntityName}}_ValidPayload_Returns200()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var payload = new Dictionary<string, object>
            {
{{MockPayloadCreate}}
            };

            var createResp = await _client.PostAsJsonAsync("/v1/{{EntityNameLower}}", payload);
            var created = await createResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
            var id = created!["id"].ToString();

            var updatePayload = new Dictionary<string, object>
            {
{{MockPayloadUpdate}}
            };

            var response = await _client.PutAsJsonAsync($"/v1/{{EntityNameLower}}/{id}", updatePayload);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [Fact]
        public async Task Update{{EntityName}}_NonExistingId_Returns404()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var updatePayload = new Dictionary<string, object>
            {
{{MockPayloadUpdate}}
            };

            var response = await _client.PutAsJsonAsync("/v1/{{EntityNameLower}}/non-existing-id", updatePayload);
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task Delete{{EntityName}}_ExistingId_Returns204()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var payload = new Dictionary<string, object>
            {
{{MockPayloadCreate}}
            };

            var createResp = await _client.PostAsJsonAsync("/v1/{{EntityNameLower}}", payload);
            var created = await createResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
            var id = created!["id"].ToString();

            var response = await _client.DeleteAsync($"/v1/{{EntityNameLower}}/{id}");
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);

            var getResp = await _client.GetAsync($"/v1/{{EntityNameLower}}/{id}");
            getResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task Delete{{EntityName}}_NonExistingId_Returns404()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var response = await _client.DeleteAsync("/v1/{{EntityNameLower}}/non-existing-id");
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task ToggleStatus_ValidPayload_Returns200()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var payload = new Dictionary<string, object>
            {
{{MockPayloadCreate}}
            };

            var createResp = await _client.PostAsJsonAsync("/v1/{{EntityNameLower}}", payload);
            var created = await createResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
            var id = created!["id"].ToString();

            var togglePayload = new { active = false };
            var response = await _client.PatchAsJsonAsync($"/v1/{{EntityNameLower}}/{id}/status", togglePayload);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [Fact]
        public async Task ToggleStatus_NonExistingId_Returns404()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var togglePayload = new { active = false };
            var response = await _client.PatchAsJsonAsync("/v1/{{EntityNameLower}}/non-existing-id/status", togglePayload);
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task Create{{EntityName}}_InvalidPayload_Returns400()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var payload = new Dictionary<string, object>
            {
                // Missing required fields
            };

            var response = await _client.PostAsJsonAsync("/v1/{{EntityNameLower}}", payload);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
    }
}
