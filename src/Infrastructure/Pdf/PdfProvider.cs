using System.Text;
using System.Text.Json;

namespace MageBackend.Infrastructure.Pdf
{
    /*
     * Cliente HTTP do serviço de PDF.
     *
     * A resiliência (retry/timeout/circuit-breaker/rate-limit) é aplicada de
     * forma transparente pelo delegating handler Polly registrado em
     * Program.cs via AddStandardResilienceHandler() — este provider desconhece
     * o pipeline, mas se beneficia dele em produção.
     *
     * URL configurável via PDF_SERVICE_URL (env / appsettings), com fallback
     * http://localhost:8889 em dev.
     */
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
            var request = BuildRequest(template, data);
            var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
            {
                var errorMsg = await ReadErrorBodyAsync(response);
                response.Dispose();
                throw new InvalidOperationException($"Erro ao gerar PDF no serviço: {errorMsg}");
            }

            return await response.Content.ReadAsStreamAsync();
        }

        internal HttpRequestMessage BuildRequest(string template, object data)
        {
            var payload = new
            {
                template,
                data
            };

            var content = new StringContent(JsonSerializer.Serialize(payload, _jsonOptions), Encoding.UTF8, "application/json");

            return new HttpRequestMessage(HttpMethod.Post, $"{_pdfServiceUrl}/v1/pdf/generate")
            {
                Content = content
            };
        }

        internal static async Task<string> ReadErrorBodyAsync(HttpResponseMessage response)
        {
            return await response.Content.ReadAsStringAsync();
        }
    }
}
