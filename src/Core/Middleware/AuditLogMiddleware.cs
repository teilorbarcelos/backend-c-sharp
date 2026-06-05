using Microsoft.AspNetCore.Http;
using System;
using System.IO;
using System.Linq;
using Serilog;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MageBackend.Infrastructure.Auditing;

namespace MageBackend.Core.Middleware
{
    public class AuditLogMiddleware
    {
        private static readonly string[] MutatingMethods = { "POST", "PUT", "DELETE", "PATCH" };
        private static readonly string[] ExcludedPaths =
        {
            "/v1/auth/login",
            "/v1/auth/refresh",
            "/v1/auth/me",
            "/admin",
            "/health",
            "/liveness",
            "/docs",
            "/metrics"
        };

        private readonly RequestDelegate _next;

        public AuditLogMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, IAuditLogQueue? queue = null)
        {
            var method = context.Request.Method;
            var path = context.Request.Path.Value ?? "";

            if (!ShouldAudit(method, path))
            {
                await _next(context);
                return;
            }

            context.Request.EnableBuffering();
            var requestBody = await ReadRequestBodyAsync(context);
            var sanitizedParams = SanitizeBody(requestBody);

            /*
             * Mantém paridade com o middleware antigo: o response body NÃO é
             * realmente capturado (o stream original nunca é swapado). O
             * DiffValue acaba sendo o sentinela {statusCode}. Captura completa
             * de body fica como melhoria separada — fora do escopo deste refac.
             */
            await _next(context);

            var entry = BuildEntry(context, method, path, sanitizedParams, responseBodyText: string.Empty);

            EnqueueOrWarn(context, queue, entry);
        }

        private static void EnqueueOrWarn(HttpContext context, IAuditLogQueue? queue, AuditLogEntry entry)
        {
            var effectiveQueue = queue ?? context.RequestServices.GetService(typeof(IAuditLogQueue)) as IAuditLogQueue;

            if (effectiveQueue is null)
            {
                Log.Warning("[Audit] IAuditLogQueue not registered; dropping entry for {Method} {Path}",
                    entry.Method, entry.Path);
                return;
            }

            effectiveQueue.TryEnqueue(entry);
        }

        internal static bool ShouldAudit(string method, string path)
        {
            return Array.IndexOf(MutatingMethods, method) >= 0 &&
                   !Array.Exists(ExcludedPaths, p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        }

        internal static async Task<string?> ReadRequestBodyAsync(HttpContext context)
        {
            if (context.Request.ContentLength is null or 0)
            {
                return null;
            }

            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;
            return body;
        }

        internal static AuditLogEntry BuildEntry(
            HttpContext context,
            string method,
            string path,
            string? sanitizedParams,
            string responseBodyText)
        {
            return new AuditLogEntry
            {
                IdUser = context.User?.FindFirst("id")?.Value,
                UserName = context.User?.FindFirst("email")?.Value ?? "Anonymous",
                Method = method,
                Path = path,
                TableName = ResolveTableName(path),
                Params = sanitizedParams,
                ResponseBody = responseBodyText,
                StatusCode = context.Response.StatusCode,
                Host = context.Request.Host.ToString(),
                Ip = context.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1",
                Hostname = context.Request.Host.Host,
                CreatedAt = DateTime.UtcNow
            };
        }

        internal static string ResolveTableName(string path)
        {
            foreach (var seg in path.Split('/'))
            {
                if (seg.Equals("user", StringComparison.OrdinalIgnoreCase)) return "User";
                if (seg.Equals("role", StringComparison.OrdinalIgnoreCase)) return "Role";
                if (seg.Equals("product", StringComparison.OrdinalIgnoreCase)) return "Product";
                if (seg.Equals("feature", StringComparison.OrdinalIgnoreCase)) return "Feature";
            }
            return "System";
        }

        internal static string? SanitizeBody(string? body)
        {
            if (string.IsNullOrEmpty(body)) return null;

            try
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return body;

                var dictionary = root.EnumerateObject().ToDictionary(
                    prop => prop.Name,
                    prop =>
                    {
                        var lowerName = prop.Name.ToLowerInvariant();
                        if (lowerName.Contains("password") || lowerName.Contains("token"))
                        {
                            return "******";
                        }
                        return prop.Value.ValueKind == JsonValueKind.String
                            ? prop.Value.GetString()
                            : prop.Value.GetRawText();
                    });

                return JsonSerializer.Serialize(dictionary);
            }
#pragma warning disable S2221
            /*
             * Body malformado (não-JSON, fragmento, etc.) volta cru. Catch
             * genérico é intencional: a auditoria não pode derrubar a request
             * por causa de payload inesperado.
             */
            catch
            {
                return body;
            }
#pragma warning restore S2221
        }
    }
}
