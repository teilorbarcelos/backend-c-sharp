using System;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using MageBackend.Features.Dashboard;
using Xunit;
using FluentAssertions;

namespace MageBackend.Tests
{
    [Collection("Integration Tests")]
    public class DashboardTests : IntegrationTestBase
    {
        public DashboardTests(IntegrationTestFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public async Task GivenAuthenticatedAdmin_WhenRequestingDashboardStats_ThenReturnsValidAggregations()
        {
            // Arrange
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            // Act
            var response = await _client.GetAsync("/v1/dashboard/stats");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var result = await response.Content.ReadFromJsonAsync<DashboardStatsResponseDto>();
            
            result.Should().NotBeNull();
            result!.UserCreationStats.Should().NotBeNull();
            result.ProductCreationStats.Should().NotBeNull();
            result.ProductsPerUser.Should().NotBeNull();
        }

        [Fact]
        public async Task GivenUnauthenticatedUser_WhenRequestingDashboardStats_ThenReturnsUnauthorized()
        {
            // Arrange
            _client.DefaultRequestHeaders.Authorization = null;

            // Act
            var response = await _client.GetAsync("/v1/dashboard/stats");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }
}
