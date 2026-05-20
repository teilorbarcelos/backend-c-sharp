using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Google;
using Google.Cloud.Storage.V1;
using MageBackend.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace MageBackend.Tests
{
    public class GcsStorageProviderTests
    {
        private readonly Mock<StorageClient> _storageMock;
        private readonly GcsStorageProvider _provider;

        public GcsStorageProviderTests()
        {
            _storageMock = new Mock<StorageClient>();
            
            var configMock = new Mock<IConfiguration>();
            configMock.Setup(c => c["GCS_BUCKET_NAME"]).Returns("test-bucket");

            _provider = new GcsStorageProvider(configMock.Object, _storageMock.Object);
        }

        [Fact]
        public async Task UploadFileAsync_ShouldReturnUrl()
        {
            var obj = new Google.Apis.Storage.v1.Data.Object { MediaLink = "https://gcs.link/test.png" };
            
            _storageMock.Setup(x => x.UploadObjectAsync("test-bucket", It.IsAny<string>(), "image/png", It.IsAny<Stream>(), It.IsAny<UploadObjectOptions>(), It.IsAny<CancellationToken>(), It.IsAny<IProgress<Google.Apis.Upload.IUploadProgress>>()))
                        .ReturnsAsync(obj);

            using var ms = new MemoryStream(new byte[] { 1, 2, 3 });
            var url = await _provider.UploadFileAsync("test.png", ms, "image/png");

            Assert.Equal("https://gcs.link/test.png", url);
        }

        [Fact]
        public async Task GetFileAsync_WhenExists_ShouldReturnStream()
        {
            _storageMock.Setup(x => x.DownloadObjectAsync("test-bucket", "test.png", It.IsAny<Stream>(), It.IsAny<DownloadObjectOptions>(), It.IsAny<CancellationToken>()))
                        .Returns(Task.FromResult(new Google.Apis.Storage.v1.Data.Object()));

            var result = await _provider.GetFileAsync("https://gcs.link/test.png");

            Assert.NotNull(result);
        }

        [Fact]
        public async Task GetFileAsync_WhenNotExists_ShouldReturnNull()
        {
            _storageMock.Setup(x => x.DownloadObjectAsync("test-bucket", "test.png", It.IsAny<Stream>(), It.IsAny<DownloadObjectOptions>(), It.IsAny<CancellationToken>()))
                        .ThrowsAsync(new GoogleApiException("Storage", "Not Found") { HttpStatusCode = System.Net.HttpStatusCode.NotFound });

            var result = await _provider.GetFileAsync("https://gcs.link/test.png");

            Assert.Null(result);
        }

        [Fact]
        public async Task DeleteFileAsync_WhenExists_ShouldReturnTrue()
        {
            _storageMock.Setup(x => x.DeleteObjectAsync("test-bucket", "test.png", It.IsAny<DeleteObjectOptions>(), It.IsAny<CancellationToken>()))
                        .Returns(Task.CompletedTask);

            var result = await _provider.DeleteFileAsync("https://gcs.link/test.png");

            Assert.True(result);
        }

        [Fact]
        public async Task DeleteFileAsync_WhenNotExists_ShouldReturnFalse()
        {
            _storageMock.Setup(x => x.DeleteObjectAsync("test-bucket", "test.png", It.IsAny<DeleteObjectOptions>(), It.IsAny<CancellationToken>()))
                        .ThrowsAsync(new GoogleApiException("Storage", "Not Found") { HttpStatusCode = System.Net.HttpStatusCode.NotFound });

            var result = await _provider.DeleteFileAsync("https://gcs.link/test.png");

            Assert.False(result);
        }
    }
}
