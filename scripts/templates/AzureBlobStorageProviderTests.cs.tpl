using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using MageBackend.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace MageBackend.Tests
{
    public class AzureBlobStorageProviderTests
    {
        private readonly Mock<BlobServiceClient> _serviceMock;
        private readonly Mock<BlobContainerClient> _containerMock;
        private readonly Mock<BlobClient> _blobMock;
        private readonly AzureBlobStorageProvider _provider;

        public AzureBlobStorageProviderTests()
        {
            _serviceMock = new Mock<BlobServiceClient>();
            _containerMock = new Mock<BlobContainerClient>();
            _blobMock = new Mock<BlobClient>();

            _serviceMock.Setup(x => x.GetBlobContainerClient(It.IsAny<string>()))
                        .Returns(_containerMock.Object);

            _containerMock.Setup(x => x.GetBlobClient(It.IsAny<string>()))
                          .Returns(_blobMock.Object);
            
            _blobMock.Setup(x => x.Uri).Returns(new Uri("https://azure.com/test.png"));

            var configMock = new Mock<IConfiguration>();
            configMock.Setup(c => c["AZURE_STORAGE_CONNECTION_STRING"]).Returns("UseDevelopmentStorage=true");
            configMock.Setup(c => c["AZURE_CONTAINER_NAME"]).Returns("test-container");

            _provider = new AzureBlobStorageProvider(configMock.Object, _serviceMock.Object);
        }

        [Fact]
        public async Task UploadFileAsync_ShouldReturnUrl()
        {
            _blobMock.Setup(x => x.UploadAsync(It.IsAny<Stream>(), It.IsAny<BlobHttpHeaders>(), null, null, null, null, default))
                     .ReturnsAsync((Response<BlobContentInfo>?)null);

            using var ms = new MemoryStream(new byte[] { 1, 2, 3 });
            var url = await _provider.UploadFileAsync("test.png", ms, "image/png");

            Assert.Equal("https://azure.com/test.png", url);
        }

        [Fact]
        public async Task GetFileAsync_WhenExists_ShouldReturnStream()
        {
            _blobMock.Setup(x => x.ExistsAsync(default)).ReturnsAsync(Response.FromValue(true, null!));
            
            var expectedStream = new MemoryStream();
            var blobDownloadInfo = BlobsModelFactory.BlobDownloadStreamingResult(expectedStream, null);
            
            _blobMock.Setup(x => x.DownloadStreamingAsync(default, default))
                     .ReturnsAsync(Response.FromValue(blobDownloadInfo, null!));

            var result = await _provider.GetFileAsync("https://azure.com/test.png");

            Assert.Same(expectedStream, result);
        }

        [Fact]
        public async Task GetFileAsync_WhenNotExists_ShouldReturnNull()
        {
            _blobMock.Setup(x => x.ExistsAsync(default)).ReturnsAsync(Response.FromValue(false, null!));

            var result = await _provider.GetFileAsync("https://azure.com/test.png");

            Assert.Null(result);
        }

        [Fact]
        public async Task DeleteFileAsync_WhenExists_ShouldReturnTrue()
        {
            _blobMock.Setup(x => x.DeleteIfExistsAsync(default, null, default))
                     .ReturnsAsync(Response.FromValue(true, null!));

            var result = await _provider.DeleteFileAsync("https://azure.com/test.png");

            Assert.True(result);
        }

        [Fact]
        public async Task DeleteFileAsync_WhenNotExists_ShouldReturnFalse()
        {
            _blobMock.Setup(x => x.DeleteIfExistsAsync(default, null, default))
                     .ReturnsAsync(Response.FromValue(false, null!));

            var result = await _provider.DeleteFileAsync("https://azure.com/test.png");

            Assert.False(result);
        }
    }
}
