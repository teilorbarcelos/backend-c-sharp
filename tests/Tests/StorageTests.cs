using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using MageBackend.Features.Storage;
using Xunit;
using System.Text.Json;

namespace MageBackend.Tests
{
    public class StorageTests : IntegrationTestBase
    {
        public StorageTests(IntegrationTestFixture fixture) : base(fixture) { }

        [Fact]
        public async Task GivenAdmin_WhenUploadingFile_ThenSuccessAndUrlReturned()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var fileContent = new ByteArrayContent(new byte[] { 1, 2, 3, 4 });
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");
            
            using var formData = new MultipartFormDataContent();
            formData.Add(fileContent, "file", "test-image.jpg");

            var response = await _client.PostAsync("/v1/storage/upload", formData);

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<UploadResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            Assert.NotNull(result);
            Assert.Contains("/v1/storage/", result.Url);
            Assert.Contains("test-image.jpg", result.Url);

            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenUploadedFile_WhenGettingFile_ThenReturnsStream()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            // 1. Upload
            var fileContent = new ByteArrayContent(new byte[] { 1, 2, 3, 4 });
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
            using var formData = new MultipartFormDataContent();
            formData.Add(fileContent, "file", "doc.pdf");
            var uploadRes = await _client.PostAsync("/v1/storage/upload", formData);
            uploadRes.EnsureSuccessStatusCode();
            var json = await uploadRes.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<UploadResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            
            // 2. Extract fileName
            var uri = new Uri(result!.Url);
            var fileName = Path.GetFileName(uri.LocalPath);

            ClearAuthHeader(); // Remove auth for public GET if you want, or leave it

            // 3. Get File
            var getRes = await _client.GetAsync($"/v1/storage/{fileName}");
            getRes.EnsureSuccessStatusCode();
            Assert.Equal("application/pdf", getRes.Content.Headers.ContentType?.MediaType);
            
            var bytes = await getRes.Content.ReadAsByteArrayAsync();
            Assert.Equal(4, bytes.Length);
        }

        [Fact]
        public async Task GivenAdmin_WhenDeletingFile_ThenSuccessAndFileRemoved()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            // 1. Upload
            var fileContent = new ByteArrayContent(new byte[] { 1, 2, 3, 4 });
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
            using var formData = new MultipartFormDataContent();
            formData.Add(fileContent, "file", "logo.png");
            var uploadRes = await _client.PostAsync("/v1/storage/upload", formData);
            var json = await uploadRes.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<UploadResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            var fileName = Path.GetFileName(new Uri(result!.Url).LocalPath);

            // 2. Delete
            var deleteRes = await _client.DeleteAsync($"/v1/storage/{fileName}");
            Assert.Equal(HttpStatusCode.NoContent, deleteRes.StatusCode);

            // 3. Get (Should be NotFound)
            var getRes = await _client.GetAsync($"/v1/storage/{fileName}");
            Assert.Equal(HttpStatusCode.NotFound, getRes.StatusCode);

            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenNoFile_WhenUploading_ThenReturnsBadRequest()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            using var formData = new MultipartFormDataContent();
            // Empty formData
            var response = await _client.PostAsync("/v1/storage/upload", formData);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            ClearAuthHeader();
        }

        [Fact]
        public async Task GivenInvalidFile_WhenDeleting_ThenReturnsNotFound()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var deleteRes = await _client.DeleteAsync("/v1/storage/non-existent-file.png");
            Assert.Equal(HttpStatusCode.NotFound, deleteRes.StatusCode);

            ClearAuthHeader();
        }
    }
}
