using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using System.Diagnostics.CodeAnalysis;
using Google.Cloud.Storage.V1;
using Google;

namespace MageBackend.Infrastructure.Storage
{
    public class GcsStorageProvider : IStorageProvider
    {
        private readonly StorageClient _storageClient;
        private readonly string _bucketName;

        [ExcludeFromCodeCoverage]
        public GcsStorageProvider(IConfiguration configuration, StorageClient? storageClient = null)
        {
            _storageClient = storageClient ?? StorageClient.Create();
            _bucketName = configuration["GCS_BUCKET_NAME"] ?? "YOUR_BUCKET_NAME";
        }

        public async Task<string> UploadFileAsync(string fileName, Stream content, string contentType)
        {
            var uniqueFileName = $"{Guid.NewGuid()}_{fileName}";
            var obj = await _storageClient.UploadObjectAsync(_bucketName, uniqueFileName, contentType, content);
            return obj.MediaLink;
        }

        public async Task<Stream?> GetFileAsync(string fileUrl)
        {
            var fileName = fileUrl.Substring(fileUrl.LastIndexOf('/') + 1);
            try
            {
                var stream = new MemoryStream();
                await _storageClient.DownloadObjectAsync(_bucketName, fileName, stream);
                stream.Position = 0;
                return stream;
            }
            catch (GoogleApiException e) when (e.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        public async Task<bool> DeleteFileAsync(string fileUrl)
        {
            var fileName = fileUrl.Substring(fileUrl.LastIndexOf('/') + 1);
            try
            {
                await _storageClient.DeleteObjectAsync(_bucketName, fileName);
                return true;
            }
            catch (GoogleApiException e) when (e.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return false;
            }
        }
    }
}
