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
using MageBackend.Web;
using MageBackend.Shared;
using MageBackend.Features.Auth;
using MageBackend.Features.User;
using MageBackend.Features.Role;
using MageBackend.Features.Product;
using MageBackend.Infrastructure.Auth;
using Xunit;

namespace MageBackend.Tests
{
    public class QueryBuilderTests : IntegrationTestBase
    {
        public QueryBuilderTests(IntegrationTestFixture fixture) : base(fixture) { }

        // ==========================================
        // --- 04. Dynamic Filters (9 tests) --------
        // ==========================================

        [Fact]
        public async Task GivenDynamicFilter_WhenExecuting_ThenReturnsFilteredResults()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var url = "/v1/user/all?page=0&size=25&searchWord=Admin&searchFields=name,email,Role.name&orderBy=name&orderDirection=asc";
            var resp = await _client.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var data = await resp.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(data.TryGetProperty("items", out _));
            Assert.True(data.TryGetProperty("total", out _));
            Assert.True(data.TryGetProperty("page", out _));

            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenMissingSearchFields_WhenFiltering_ThenHandlesGracefully()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var url = "/v1/user/all?page=0&size=25&searchWord=Admin&orderBy=name&orderDirection=asc";
            var resp = await _client.GetAsync(url);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenUnmappedSearchField_WhenFiltering_ThenReturnsBadRequest()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var url = "/v1/user/all?page=0&size=25&searchWord=Admin&searchFields=password&orderBy=name&orderDirection=asc";
            var resp = await _client.GetAsync(url);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenUnallowedFilterKey_WhenFiltering_ThenReturnsBadRequest()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var url = "/v1/user/all?invalid_filter_parameter=123";
            var resp = await _client.GetAsync(url);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenInvalidDateFormat_WhenFiltering_ThenReturnsBadRequest()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var url = "/v1/user/all?createdAt_start=2024-invalid-format";
            var resp = await _client.GetAsync(url);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenDateRange_WhenFiltering_ThenReturnsCorrectResults()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            using (var scope = _fixture.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var user = await dbContext.User.FirstOrDefaultAsync();
                Assert.NotNull(user);
                var dateStr = user.CreatedAt.ToString("yyyy-MM-dd");

                var url = $"/v1/user/all?page=0&size=25&createdAt_start={dateStr}&createdAt_end={dateStr}";
                var resp = await _client.GetAsync(url);
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                var data = await resp.Content.ReadFromJsonAsync<JsonElement>();
                Assert.True(data.TryGetProperty("items", out _));
                Assert.True(data.GetProperty("items").GetArrayLength() > 0);
            }

            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenActiveStatus_WhenFiltering_ThenReturnsCorrectResults()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var rolePayload = new
            {
                name = $"Filter Test Role {uniqueSuffix}",
                description = "Role for filtering tests",
                permissions = new List<object>()
            };
            var roleResp = await _client.PostAsJsonAsync("/v1/role", rolePayload);
            Assert.Contains(roleResp.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });
            var roleData = await roleResp.Content.ReadFromJsonAsync<JsonElement>();
            var roleId = roleData.GetProperty("id").GetString()!;

            var email = $"filter_test_{uniqueSuffix}@email.com";
            var userPayload = new
            {
                name = $"Filter Test User {uniqueSuffix}",
                email = email,
                password = "Password123!",
                id_role = roleId,
                active = true
            };
            var userResp = await _client.PostAsJsonAsync("/v1/user", userPayload);
            Assert.Contains(userResp.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });
            var userData = await userResp.Content.ReadFromJsonAsync<JsonElement>();
            var userId = userData.GetProperty("id").GetString()!;

            try
            {
                var defaultAllResp = await _client.GetAsync("/v1/user/all?page=0&size=100");
                Assert.Equal(HttpStatusCode.OK, defaultAllResp.StatusCode);
                var defaultAllData = await defaultAllResp.Content.ReadFromJsonAsync<PaginatedResponse<UserResponse>>();
                Assert.Contains(defaultAllData!.Items, u => u.Id == userId);

                var defaultRootResp = await _client.GetAsync("/v1/user?page=0&size=100");
                Assert.Equal(HttpStatusCode.OK, defaultRootResp.StatusCode);
                var defaultRootData = await defaultRootResp.Content.ReadFromJsonAsync<PaginatedResponse<UserResponse>>();
                Assert.Contains(defaultRootData!.Items, u => u.Id == userId);

                // Deactivate user
                var deactResp = await _client.PatchAsJsonAsync($"/v1/user/{userId}/status", new { active = false });
                Assert.Equal(HttpStatusCode.OK, deactResp.StatusCode);

                // Query root without active param: deactivated user should NOT be present
                var rootNoParamResp = await _client.GetAsync("/v1/user?page=0&size=100");
                Assert.Equal(HttpStatusCode.OK, rootNoParamResp.StatusCode);
                var rootNoParamData = await rootNoParamResp.Content.ReadFromJsonAsync<PaginatedResponse<UserResponse>>();
                Assert.DoesNotContain(rootNoParamData!.Items, u => u.Id == userId);

                // Query all without active param: deactivated user SHOULD be present
                var allNoParamResp = await _client.GetAsync("/v1/user/all?page=0&size=100");
                Assert.Equal(HttpStatusCode.OK, allNoParamResp.StatusCode);
                var allNoParamData = await allNoParamResp.Content.ReadFromJsonAsync<PaginatedResponse<UserResponse>>();
                Assert.Contains(allNoParamData!.Items, u => u.Id == userId);

                // Query active=true: user should NOT be present
                var activeResp = await _client.GetAsync("/v1/user/all?page=0&size=100&active=true");
                Assert.Equal(HttpStatusCode.OK, activeResp.StatusCode);
                var activeData = await activeResp.Content.ReadFromJsonAsync<PaginatedResponse<UserResponse>>();
                Assert.DoesNotContain(activeData!.Items, u => u.Id == userId);

                // Query active=false: user should be present
                var inactiveResp = await _client.GetAsync("/v1/user/all?page=0&size=100&active=false");
                Assert.Equal(HttpStatusCode.OK, inactiveResp.StatusCode);
                var inactiveData = await inactiveResp.Content.ReadFromJsonAsync<PaginatedResponse<UserResponse>>();
                Assert.Contains(inactiveData!.Items, u => u.Id == userId);
            }
            finally
            {
                await _client.DeleteAsync($"/v1/user/{userId}");
                await _client.DeleteAsync($"/v1/role/{roleId}");
            }

            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenPaginationLimits_WhenRequestingTooMany_ThenRestrictsSize()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var respOk = await _client.GetAsync("/v1/user/all?page=0&size=100");
            Assert.Equal(HttpStatusCode.OK, respOk.StatusCode);

            var respBad = await _client.GetAsync("/v1/user/all?page=0&size=101");
            Assert.Equal(HttpStatusCode.BadRequest, respBad.StatusCode);

            var respRootOk = await _client.GetAsync("/v1/user?page=0&size=100");
            Assert.Equal(HttpStatusCode.OK, respRootOk.StatusCode);

            var respRootBad = await _client.GetAsync("/v1/user?page=0&size=101");
            Assert.Equal(HttpStatusCode.BadRequest, respRootBad.StatusCode);

            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenSortingParameters_WhenListing_ThenReturnsSortedResults()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var respAsc = await _client.GetAsync("/v1/user/all?page=0&size=100&orderBy=name&orderDirection=asc");
            Assert.Equal(HttpStatusCode.OK, respAsc.StatusCode);
            var dataAsc = await respAsc.Content.ReadFromJsonAsync<PaginatedResponse<UserResponse>>();
            Assert.NotNull(dataAsc);
            var namesAsc = new List<string>();
            foreach (var item in dataAsc.Items) namesAsc.Add(item.Name.ToLower());
            var expectedAsc = new List<string>(namesAsc);
            expectedAsc.Sort();
            Assert.Equal(expectedAsc, namesAsc);

            var respDesc = await _client.GetAsync("/v1/user/all?page=0&size=100&orderBy=name&orderDirection=desc");
            Assert.Equal(HttpStatusCode.OK, respDesc.StatusCode);
            var dataDesc = await respDesc.Content.ReadFromJsonAsync<PaginatedResponse<UserResponse>>();
            Assert.NotNull(dataDesc);
            var namesDesc = new List<string>();
            foreach (var item in dataDesc.Items) namesDesc.Add(item.Name.ToLower());
            var expectedDesc = new List<string>(namesDesc);
            expectedDesc.Sort();
            expectedDesc.Reverse();
            Assert.Equal(expectedDesc, namesDesc);

            var respBad = await _client.GetAsync("/v1/user/all?page=0&size=10&orderBy=invalid_column_name");
            Assert.Equal(HttpStatusCode.BadRequest, respBad.StatusCode);

            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenAlternativeActiveValues_WhenListing_ThenParsesCorrectly()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var resp1 = await _client.GetAsync("/v1/user/all?active=1");
            Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);

            var resp0 = await _client.GetAsync("/v1/user/all?active=0");
            Assert.Equal(HttpStatusCode.OK, resp0.StatusCode);

            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenInvalidEndDate_WhenListing_ThenReturnsBadRequest()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var resp = await _client.GetAsync("/v1/user/all?createdAt_end=invalid-date");
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

            ClearAuthHeader();
        }

        [Fact]
        public void GivenQueryable_WhenApplyingSearchWithNonExistentField_ThenSkipsField()
        {
            var query = new List<Product>().AsQueryable();
            var result = query.ApplySearch("test", "nonexistentfield,name");
            Assert.NotNull(result);
        }

        [Fact]
        public void GivenQueryableWithoutCreatedAt_WhenOrderingWithoutField_ThenReturnsOriginalQuery()
        {
            var query = new List<string>().AsQueryable();
            var result = query.ApplyOrdering("", "desc");
            Assert.NotNull(result);
        }
    }
}