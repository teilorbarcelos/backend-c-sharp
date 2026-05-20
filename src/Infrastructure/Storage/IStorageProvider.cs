using System.IO;
using System.Threading.Tasks;

namespace MageBackend.Infrastructure.Storage
{
    public interface IStorageProvider
    {
        Task<string> UploadFileAsync(string fileName, Stream content, string contentType);
        Task<Stream?> GetFileAsync(string fileUrl);
        Task<bool> DeleteFileAsync(string fileUrl);
    }
}
