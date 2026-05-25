namespace MageBackend.Infrastructure.Pdf
{
    public interface IPdfProvider
    {
        Task<Stream> GeneratePdfAsync(string template, object data);
    }
}
