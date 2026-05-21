using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using System.Diagnostics.CodeAnalysis;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure;

namespace MageBackend.Infrastructure.Storage
{
    public class AzureBlobStorageProvider : IStorageProvider
    {
        private readonly BlobContainerClient _containerClient;

        [ExcludeFromCodeCoverage]
        public AzureBlobStorageProvider(IConfiguration configuration, BlobServiceClient? blobServiceClient = null)
        {
            var connectionString = configuration["AZURE_STORAGE_CONNECTION_STRING"] ?? "UseDevelopmentStorage=true";
            var containerName = configuration["AZURE_CONTAINER_NAME"] ?? "uploads";
            
            var client = blobServiceClient ?? new BlobServiceClient(connectionString);
            _containerClient = client.GetBlobContainerClient(containerName);
            
            if (blobServiceClient == null)
            {
                _containerClient.CreateIfNotExists(PublicAccessType.Blob);
            }
        }

        public async Task<string> UploadFileAsync(string fileName, Stream content, string contentType)
        {
            var uniqueFileName = $"{Guid.NewGuid()}_{fileName}";
            var blobClient = _containerClient.GetBlobClient(uniqueFileName);
            
            await blobClient.UploadAsync(content, new BlobHttpHeaders { ContentType = contentType });
            return blobClient.Uri.ToString();
        }

        public async Task<Stream?> GetFileAsync(string fileUrl)
        {
            var fileName = fileUrl.Substring(fileUrl.LastIndexOf('/') + 1);
            var blobClient = _containerClient.GetBlobClient(fileName);
            
            try
            {
                if (!await blobClient.ExistsAsync()) return null;
                
                var response = await blobClient.DownloadStreamingAsync();
                return response.Value.Content;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        public async Task<bool> DeleteFileAsync(string fileUrl)
        {
            var fileName = fileUrl.Substring(fileUrl.LastIndexOf('/') + 1);
            var blobClient = _containerClient.GetBlobClient(fileName);
            return await blobClient.DeleteIfExistsAsync();
        }
    }
}
