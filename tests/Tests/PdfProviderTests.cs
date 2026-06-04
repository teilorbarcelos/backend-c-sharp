using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using MageBackend.Infrastructure.Pdf;
using Xunit;

namespace MageBackend.Tests
{
    public class PdfProviderTests
    {
        private class MockHttpMessageHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _sendAsync;

            public MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
            {
                _sendAsync = sendAsync;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return _sendAsync(request, cancellationToken);
            }
        }

        [Fact]
        public async Task GivenPdfProvider_WhenGeneratingPdfSucceeds_ThenReturnsStream()
        {
            // Arrange
            var mockResponse = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("pdf-stream-content"))
            };

            HttpRequestMessage? capturedRequest = null;

            var mockHandler = new MockHttpMessageHandler((req, token) =>
            {
                capturedRequest = req;
                return Task.FromResult(mockResponse);
            });

            var client = new HttpClient(mockHandler);

            var configSettings = new Dictionary<string, string?>
            {
                { "PDF_SERVICE_URL", "http://pdf-service-url:9999" }
            };
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configSettings)
                .Build();

            var provider = new PdfProvider(client, configuration);

            // Act
            var resultStream = await provider.GeneratePdfAsync("my-template", new { name = "John" });

            // Assert
            Assert.NotNull(resultStream);
            using var reader = new StreamReader(resultStream);
            var content = await reader.ReadToEndAsync();
            Assert.Equal("pdf-stream-content", content);

            Assert.NotNull(capturedRequest);
            Assert.Equal(HttpMethod.Post, capturedRequest.Method);
            Assert.Equal("http://pdf-service-url:9999/v1/pdf/generate", capturedRequest.RequestUri?.ToString());
        }

        [Fact]
        public async Task GivenPdfProviderWithoutConfig_WhenGeneratingPdf_ThenUsesFallbackUrl()
        {
            // Arrange
            var mockResponse = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("fallback-content"))
            };

            HttpRequestMessage? capturedRequest = null;

            var mockHandler = new MockHttpMessageHandler((req, token) =>
            {
                capturedRequest = req;
                return Task.FromResult(mockResponse);
            });

            var client = new HttpClient(mockHandler);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>())
                .Build();

            var provider = new PdfProvider(client, configuration);

            // Act
            var resultStream = await provider.GeneratePdfAsync("my-template", new { });

            // Assert
            Assert.NotNull(resultStream);
            Assert.NotNull(capturedRequest);
            Assert.Equal("http://localhost:8889/v1/pdf/generate", capturedRequest.RequestUri?.ToString());
        }

        [Fact]
        public async Task GivenPdfProvider_WhenServiceReturnsError_ThenThrowsException()
        {
            // Arrange
            var mockResponse = new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("Template not found or invalid payload")
            };

            var mockHandler = new MockHttpMessageHandler((req, token) => Task.FromResult(mockResponse));
            var client = new HttpClient(mockHandler);
            var configuration = new ConfigurationBuilder().Build();
            var provider = new PdfProvider(client, configuration);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GeneratePdfAsync("invalid-template", new { }));
            Assert.Contains("Erro ao gerar PDF no serviço: Template not found or invalid payload", exception.Message);
        }

        [Fact]
        public async Task GivenPdfProvider_WhenBuildingRequest_ThenTargetsConfiguredEndpointWithJsonPayload()
        {
            var client = new HttpClient(new MockHttpMessageHandler((req, token) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));
            var configSettings = new Dictionary<string, string?>
            {
                { "PDF_SERVICE_URL", "http://custom-host:5555" }
            };
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(configSettings).Build();
            var provider = new PdfProvider(client, configuration);

            var request = provider.BuildRequest("invoice", new { userId = 42 });

            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("http://custom-host:5555/v1/pdf/generate", request.RequestUri?.ToString());
            Assert.NotNull(request.Content);
            Assert.Equal("application/json", request.Content.Headers.ContentType?.MediaType);
            var payload = await request.Content.ReadAsStringAsync();
            Assert.Contains("\"template\":\"invoice\"", payload);
            Assert.Contains("\"userId\":42", payload);
        }

        [Fact]
        public async Task GivenErrorResponse_WhenReadingErrorBody_ThenReturnsBodyString()
        {
            var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("upstream crashed")
            };

            var body = await PdfProvider.ReadErrorBodyAsync(response);

            Assert.Equal("upstream crashed", body);
        }
    }
}
