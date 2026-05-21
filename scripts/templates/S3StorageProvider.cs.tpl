using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using System.Diagnostics.CodeAnalysis;
using Amazon.S3;
using Amazon.S3.Model;

namespace MageBackend.Infrastructure.Storage
{
    public class S3StorageProvider : IStorageProvider
    {
        private readonly IAmazonS3 _s3Client;
        private readonly string _bucketName;

        [ExcludeFromCodeCoverage]
        public S3StorageProvider(IConfiguration configuration, IAmazonS3? s3Client = null)
        {
            var accessKey = configuration["AWS_ACCESS_KEY"] ?? "YOUR_ACCESS_KEY";
            var secretKey = configuration["AWS_SECRET_KEY"] ?? "YOUR_SECRET_KEY";
            _bucketName = configuration["AWS_BUCKET_NAME"] ?? "YOUR_BUCKET_NAME";

            if (s3Client != null)
            {
                _s3Client = s3Client;
            }
            else
            {
                var credentials = new Amazon.Runtime.BasicAWSCredentials(accessKey, secretKey);
                _s3Client = new AmazonS3Client(credentials, Amazon.RegionEndpoint.USEast1);
            }
        }

        public async Task<string> UploadFileAsync(string fileName, Stream content, string contentType)
        {
            var uniqueFileName = $"{Guid.NewGuid()}_{fileName}";
            var request = new PutObjectRequest
            {
                InputStream = content,
                Key = uniqueFileName,
                BucketName = _bucketName,
                ContentType = contentType
            };

            await _s3Client.PutObjectAsync(request);

            return $"https://{_bucketName}.s3.amazonaws.com/{uniqueFileName}";
        }

        public async Task<Stream?> GetFileAsync(string fileUrl)
        {
            var fileName = fileUrl.Substring(fileUrl.LastIndexOf('/') + 1);
            try
            {
                var request = new GetObjectRequest
                {
                    BucketName = _bucketName,
                    Key = fileName
                };
                var response = await _s3Client.GetObjectAsync(request);
                return response.ResponseStream;
            }
            catch (AmazonS3Exception)
            {
                return null;
            }
        }

        public async Task<bool> DeleteFileAsync(string fileUrl)
        {
            var fileName = fileUrl.Substring(fileUrl.LastIndexOf('/') + 1);
            try
            {
                var request = new DeleteObjectRequest
                {
                    BucketName = _bucketName,
                    Key = fileName
                };
                await _s3Client.DeleteObjectAsync(request);
                return true;
            }
            catch (AmazonS3Exception)
            {
                return false;
            }
        }
    }
}
