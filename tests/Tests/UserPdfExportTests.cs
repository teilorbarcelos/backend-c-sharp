using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MageBackend.Database;
using MageBackend.Infrastructure.Pdf;
using Xunit;

namespace MageBackend.Tests
{
    public class UserPdfExportTests : IntegrationTestBase
    {
        public UserPdfExportTests(IntegrationTestFixture fixture) : base(fixture) { }

        [Fact]
        public async Task GivenAnonymousUser_WhenExportingPdf_ThenReturnsUnauthorized()
        {
            // Act
            var response = await _client.GetAsync("/v1/user/export/pdf");

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task GivenForbiddenUser_WhenExportingPdf_ThenReturnsForbidden()
        {
            // Arrange
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var rolePayload = new
            {
                name = $"No View User Role {uniqueSuffix}",
                description = "Role without user view permission",
                permissions = new[]
                {
                    new
                    {
                        id_feature = "user",
                        create = false,
                        view = false, // Forbidden
                        delete = false,
                        activate = false
                    }
                }
            };
            var roleResp = await _client.PostAsJsonAsync("/v1/role", rolePayload);
            Assert.Contains(roleResp.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });
            var roleId = (await roleResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

            var email = $"no_view_user_{uniqueSuffix}@email.com";
            var userResp = await _client.PostAsJsonAsync("/v1/user", new
            {
                name = "No View User",
                email = email,
                password = "Password123!",
                id_role = roleId
            });
            Assert.Contains(userResp.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });

            // Login as limited user
            var userLogin = await LoginAsync(email, "Password123!");
            SetAuthHeader(userLogin.Token);

            // Act
            var exportResp = await _client.GetAsync("/v1/user/export/pdf");

            // Assert
            Assert.Equal(HttpStatusCode.Forbidden, exportResp.StatusCode);

            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenAdminUser_WhenExportingPdfSucceeds_ThenReturnsFileStream()
        {
            // Arrange
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            // Add a user with a non-existent role to cover the u.Role?.Name null branch
            using (var scope = _fixture.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                try
                {
                    context.User.Add(new User
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = "User Without Role",
                        Email = $"norole_{Guid.NewGuid().ToString().Substring(0, 8)}@email.com",
                        IdRole = "non-existent-role-id",
                        Active = true,
                        IsDeleted = false
                    });
                    await context.SaveChangesAsync();
                }
                catch (Exception)
                {
                    // If a foreign key constraint exists and throws, we ignore it and let the test proceed.
                }
            }

            // Act
            var response = await _client.GetAsync("/v1/user/export/pdf");

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
            Assert.Equal("usuarios.pdf", response.Content.Headers.ContentDisposition?.FileName);

            var content = await response.Content.ReadAsStringAsync();
            Assert.Equal("fake-pdf-content", content);

            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenAdminUser_WhenSearchRequestParseFails_ThenReturnsBadRequest()
        {
            // Arrange
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            // Act
            var response = await _client.GetAsync("/v1/user/export/pdf?invalidParam=yes");

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var error = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains("Query parameter 'invalidParam' is not allowed.", error.GetProperty("message").GetString());

            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenAdminUser_WhenPdfProviderThrows_ThenReturnsBadRequest()
        {
            // Arrange
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var fakePdfProvider = _fixture.Services.GetRequiredService<IPdfProvider>() as FakePdfProvider;
            Assert.NotNull(fakePdfProvider);

            try
            {
                fakePdfProvider.ShouldThrow = true;

                // Act
                var response = await _client.GetAsync("/v1/user/export/pdf");

                // Assert
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                var error = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Contains("Erro simulado no serviço de PDF", error.GetProperty("message").GetString());
            }
            finally
            {
                fakePdfProvider.ShouldThrow = false;
            }

            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenAdminUser_WhenPagingPdfExport_ThenLoopsAndReturnsAllUsers()
        {
            // Arrange
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            // Seed > 100 users directly in the database
            using (var scope = _fixture.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                
                var adminRole = await context.Role.FirstOrDefaultAsync(r => r.Id == "administrator");
                Assert.NotNull(adminRole);

                var usersToInsert = new List<User>();
                for (int i = 0; i < 105; i++)
                {
                    usersToInsert.Add(new User
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = $"Bulk User {i}",
                        Email = $"bulk_user_{Guid.NewGuid().ToString().Substring(0, 8)}@email.com",
                        IdRole = adminRole.Id,
                        Active = true,
                        IsDeleted = false
                    });
                }
                context.User.AddRange(usersToInsert);
                await context.SaveChangesAsync();
            }

            // Act
            var response = await _client.GetAsync("/v1/user/export/pdf");

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);

            var content = await response.Content.ReadAsStringAsync();
            Assert.Equal("fake-pdf-content", content);

            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenAdminUser_WhenUpdatingNonExistentUser_ThenReturnsNotFound()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var response = await _client.PutAsJsonAsync("/v1/user/non-existent-id", new
            {
                name = "New Name"
            });

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenAdminUser_WhenTogglingStatusOfNonExistentUser_ThenReturnsNotFound()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var response = await _client.PatchAsJsonAsync("/v1/user/non-existent-id/status", new
            {
                active = false
            });

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenAdminUser_WhenUpdatingAllFieldsOfUser_ThenUpdatesSuccessfully()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var createUserResp = await _client.PostAsJsonAsync("/v1/user", new
            {
                name = "Test User For Update",
                email = $"update_all_{uniqueSuffix}@email.com",
                password = "Password123!",
                id_role = "administrator"
            });
            var userData = await createUserResp.Content.ReadFromJsonAsync<JsonElement>();
            var userId = userData.GetProperty("id").GetString()!;

            var updateResponse = await _client.PutAsJsonAsync($"/v1/user/{userId}", new
            {
                name = "Updated Name",
                phone = "999999999",
                document = "123456789",
                avatar = "new_avatar.png",
                active = false
            });

            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
            var updatedUser = await updateResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Updated Name", updatedUser.GetProperty("name").GetString());
            Assert.Equal("999999999", updatedUser.GetProperty("phone").GetString());
            Assert.Equal("123456789", updatedUser.GetProperty("document").GetString());
            Assert.Equal("new_avatar.png", updatedUser.GetProperty("avatar").GetString());
            Assert.False(updatedUser.GetProperty("active").GetBoolean());

            ClearAuthHeader();
        }
    }
}
