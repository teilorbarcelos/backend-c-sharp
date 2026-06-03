using System.Text;
using System.Text.Json;

namespace MageBackend.Infrastructure.Pdf
{
    public class PdfProvider : IPdfProvider
    {
        private readonly HttpClient _client;
        private readonly string _pdfServiceUrl;
        private static readonly string DefaultPdfServiceUrl = new UriBuilder("http", "localhost", 8889).Uri.ToString().TrimEnd('/');

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        public PdfProvider(HttpClient client, IConfiguration configuration)
        {
            _client = client;
            _pdfServiceUrl = configuration["PDF_SERVICE_URL"] ?? DefaultPdfServiceUrl;
        }

        public async Task<Stream> GeneratePdfAsync(string template, object data)
        {
            var payload = new
            {
                template,
                data
            };

            var content = new StringContent(JsonSerializer.Serialize(payload, _jsonOptions), Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, $"{_pdfServiceUrl}/v1/pdf/generate")
            {
                Content = content
            };

            var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
            {
                var errorMsg = await response.Content.ReadAsStringAsync();
                response.Dispose();
                throw new InvalidOperationException($"Erro ao gerar PDF no serviço: {errorMsg}");
            }

            return await response.Content.ReadAsStreamAsync();
        }
    }
}
