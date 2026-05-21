using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MageBackend.Infrastructure.Storage;
using MageBackend.Core.Filters;

namespace MageBackend.Features.Storage
{
    [ApiController]
    [Route("v1/storage")]
    public class StorageController : ControllerBase
    {
        private readonly IStorageProvider _storageProvider;

        public StorageController(IStorageProvider storageProvider)
        {
            _storageProvider = storageProvider;
        }

        [HttpPost("upload")]
        [CheckPermission("storage", "create")]
        [ProducesResponseType(typeof(UploadResponse), 200)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> Upload(IFormFile? file)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { message = "File is empty or missing" });

            using var stream = file.OpenReadStream();
            var url = await _storageProvider.UploadFileAsync(file.FileName, stream, file.ContentType);

            return Ok(new UploadResponse { Url = url });
        }

        [HttpGet("{fileName}")]
        [ProducesResponseType(200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetFile(string fileName)
        {
            var stream = await _storageProvider.GetFileAsync(fileName);
            if (stream == null) return NotFound(new { message = "File not found" });

            var contentType = GetContentType(fileName);
            return File(stream, contentType);
        }

        [HttpDelete("{fileName}")]
        [CheckPermission("storage", "delete")]
        [ProducesResponseType(204)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> DeleteFile(string fileName)
        {
            var deleted = await _storageProvider.DeleteFileAsync(fileName);
            if (!deleted) return NotFound(new { message = "File not found" });
            return NoContent();
        }

        private string GetContentType(string path)
        {
            var lowerPath = path.ToLowerInvariant();
            if (lowerPath.EndsWith(".jpg") || lowerPath.EndsWith(".jpeg")) return "image/jpeg";
            if (lowerPath.EndsWith(".png")) return "image/png";
            if (lowerPath.EndsWith(".gif")) return "image/gif";
            if (lowerPath.EndsWith(".pdf")) return "application/pdf";
            return "application/octet-stream";
        }
    }

    public class UploadResponse
    {
        public string Url { get; set; } = string.Empty;
    }
}
