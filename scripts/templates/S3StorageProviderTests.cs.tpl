using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using MageBackend.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace MageBackend.Tests
{
    public class S3StorageProviderTests
    {
        private readonly Mock<IAmazonS3> _s3Mock;
        private readonly S3StorageProvider _provider;

        public S3StorageProviderTests()
        {
            _s3Mock = new Mock<IAmazonS3>();
            
            var configMock = new Mock<IConfiguration>();
            configMock.Setup(c => c["AWS_ACCESS_KEY"]).Returns("test-key");
            configMock.Setup(c => c["AWS_SECRET_KEY"]).Returns("test-secret");
            configMock.Setup(c => c["AWS_BUCKET_NAME"]).Returns("test-bucket");

            _provider = new S3StorageProvider(configMock.Object, _s3Mock.Object);
        }

        [Fact]
        public void Constructor_WithoutMock_ShouldCreateClient()
        {
            var configMock = new Mock<IConfiguration>();
            configMock.Setup(c => c["AWS_ACCESS_KEY"]).Returns("test-key");
            configMock.Setup(c => c["AWS_SECRET_KEY"]).Returns("test-secret");
            configMock.Setup(c => c["AWS_BUCKET_NAME"]).Returns("test-bucket");

            var provider = new S3StorageProvider(configMock.Object);
            Assert.NotNull(provider);
        }

        [Fact]
        public async Task UploadFileAsync_ShouldReturnUrl()
        {
            _s3Mock.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default))
                   .ReturnsAsync(new PutObjectResponse());

            using var ms = new MemoryStream(new byte[] { 1, 2, 3 });
            var url = await _provider.UploadFileAsync("test.png", ms, "image/png");

            Assert.Contains("test-bucket.s3.amazonaws.com", url);
            Assert.Contains("test.png", url);
        }

        [Fact]
        public async Task GetFileAsync_WhenExists_ShouldReturnStream()
        {
            var expectedStream = new MemoryStream();
            _s3Mock.Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), default))
                   .ReturnsAsync(new GetObjectResponse { ResponseStream = expectedStream });

            var result = await _provider.GetFileAsync("https://test-bucket.s3.amazonaws.com/test.png");

            Assert.Same(expectedStream, result);
        }

        [Fact]
        public async Task GetFileAsync_WhenNotExists_ShouldReturnNull()
        {
            _s3Mock.Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), default))
                   .ThrowsAsync(new AmazonS3Exception("Not Found"));

            var result = await _provider.GetFileAsync("https://test-bucket.s3.amazonaws.com/test.png");

            Assert.Null(result);
        }

        [Fact]
        public async Task DeleteFileAsync_WhenExists_ShouldReturnTrue()
        {
            _s3Mock.Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), default))
                   .ReturnsAsync(new DeleteObjectResponse());

            var result = await _provider.DeleteFileAsync("https://test-bucket.s3.amazonaws.com/test.png");

            Assert.True(result);
        }

        [Fact]
        public async Task DeleteFileAsync_WhenNotExists_ShouldReturnFalse()
        {
            _s3Mock.Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), default))
                   .ThrowsAsync(new AmazonS3Exception("Not Found"));

            var result = await _provider.DeleteFileAsync("https://test-bucket.s3.amazonaws.com/test.png");

            Assert.False(result);
        }
    }
}
