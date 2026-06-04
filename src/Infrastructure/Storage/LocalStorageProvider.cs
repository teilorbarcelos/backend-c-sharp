using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace MageBackend.Infrastructure.Storage
{
    public class LocalStorageProvider : IStorageProvider
    {
        private readonly string _storagePath;
        private readonly string _baseUrl;

        public LocalStorageProvider(IConfiguration configuration, IWebHostEnvironment env)
        {
            _storagePath = Path.Combine(env.ContentRootPath, "StorageData");
            EnsureStorageDirectoryExists();

            var port = configuration["PORT"] ?? "8888";
            _baseUrl = $"http://localhost:{port}";
        }

        [ExcludeFromCodeCoverage]
        private void EnsureStorageDirectoryExists()
        {
            if (!Directory.Exists(_storagePath))
            {
                Directory.CreateDirectory(_storagePath);
            }
        }

        public async Task<string> UploadFileAsync(string fileName, Stream content, string contentType)
        {
            var uniqueFileName = $"{Guid.NewGuid()}_{fileName}";
            var filePath = Path.Combine(_storagePath, uniqueFileName);

            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await content.CopyToAsync(fileStream);
            }

            return $"{_baseUrl}/v1/storage/{uniqueFileName}";
        }

        public Task<Stream?> GetFileAsync(string fileUrl)
        {
            var fileName = fileUrl.Split('/')[^1];
            var filePath = Path.Combine(_storagePath, fileName);

            if (!File.Exists(filePath))
                return Task.FromResult<Stream?>(null);

            var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Task.FromResult<Stream?>(stream);
        }

        public Task<bool> DeleteFileAsync(string fileUrl)
        {
            var fileName = fileUrl.Split('/')[^1];
            var filePath = Path.Combine(_storagePath, fileName);

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }
    }
}
