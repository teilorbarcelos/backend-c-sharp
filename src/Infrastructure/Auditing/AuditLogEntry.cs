using System;

namespace MageBackend.Infrastructure.Auditing
{
    /*
     * Snapshot imutável do contexto HTTP capturado pelo AuditLogMiddleware.
     * Todos os dados são extraídos do HttpContext de forma síncrona antes do
     * enqueue para evitar ObjectDisposedException quando o BackgroundService
     * processar o item após o término da request.
     */
    public sealed record AuditLogEntry
    {
        public string? IdUser { get; init; }
        public string UserName { get; init; } = "Anonymous";
        public string Method { get; init; } = string.Empty;
        public string Path { get; init; } = string.Empty;
        public string TableName { get; init; } = "System";
        public string? Params { get; init; }
        public string? ResponseBody { get; init; }
        public int StatusCode { get; init; }
        public string Host { get; init; } = string.Empty;
        public string Ip { get; init; } = "127.0.0.1";
        public string Hostname { get; init; } = string.Empty;
        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    }
}
