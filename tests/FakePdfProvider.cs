using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using MageBackend.Infrastructure.Pdf;

namespace MageBackend.Tests
{
    public class FakePdfProvider : IPdfProvider
    {
        public bool ShouldThrow { get; set; } = false;

        public Task<Stream> GeneratePdfAsync(string template, object data)
        {
            if (ShouldThrow)
            {
                throw new Exception("Erro simulado no serviço de PDF");
            }

            var byteArray = Encoding.UTF8.GetBytes("fake-pdf-content");
            return Task.FromResult<Stream>(new MemoryStream(byteArray));
        }
    }
}
