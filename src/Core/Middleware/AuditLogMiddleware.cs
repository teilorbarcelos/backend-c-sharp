using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MageBackend.Database;

namespace MageBackend.Core.Middleware
{
    public class AuditLogMiddleware
    {
        private readonly RequestDelegate _next;

        public AuditLogMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var method = context.Request.Method;
            var path = context.Request.Path.Value ?? "";

            var mutatingMethods = new[] { "POST", "PUT", "DELETE", "PATCH" };
            var excludedPaths = new[]
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

            var shouldAudit = mutatingMethods.Contains(method) &&
                              !excludedPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));

            if (!shouldAudit)
            {
                await _next(context);
                return;
            }

            // Enable buffering so the request body can be read multiple times
            context.Request.EnableBuffering();

            string? requestBody = null;
            if (context.Request.ContentLength > 0)
            {
                using (var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true))
                {
                    requestBody = await reader.ReadToEndAsync();
                    context.Request.Body.Position = 0;
                }
            }

            var sanitizedParams = SanitizeBody(requestBody);

            // Capture the response stream to audit response diff
            var originalResponseBodyStream = context.Response.Body;
            using (var responseBodyMemoryStream = new MemoryStream())
            {
                try
                {
                    await _next(context);
                }
                finally
                {
                    context.Response.Body = originalResponseBodyStream;
                }

                responseBodyMemoryStream.Seek(0, SeekOrigin.Begin);
                var responseBodyText = await new StreamReader(responseBodyMemoryStream).ReadToEndAsync();
                responseBodyMemoryStream.Seek(0, SeekOrigin.Begin);
                await responseBodyMemoryStream.CopyToAsync(originalResponseBodyStream);

                // Capture required HTTP contexts before thread dispatch to avoid ObjectDisposedException
                var scopeFactory = context.RequestServices.GetRequiredService<IServiceScopeFactory>();
                var userId = context.User?.FindFirst("id")?.Value;
                var userName = context.User?.FindFirst("email")?.Value ?? "Anonymous";
                var statusCode = context.Response.StatusCode;
                var hostHeader = context.Request.Headers["Host"].ToString();
                var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
                var requestHost = context.Request.Host.Host;

                // Perform async write to db in the background
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using (var scope = scopeFactory.CreateScope())
                        {
                            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                            // Determine table name from URL path segments
                            var tableName = "System";
                            var segments = path.Split('/');
                            foreach (var seg in segments)
                            {
                                if (seg.Equals("user", StringComparison.OrdinalIgnoreCase)) tableName = "User";
                                else if (seg.Equals("role", StringComparison.OrdinalIgnoreCase)) tableName = "Role";
                                else if (seg.Equals("product", StringComparison.OrdinalIgnoreCase)) tableName = "Product";
                                else if (seg.Equals("feature", StringComparison.OrdinalIgnoreCase)) tableName = "Feature";
                            }

                            var auditLog = new Audit
                            {
                                IdUser = userId,
                                UserName = userName,
                                ActionType = "HTTP_REQUEST",
                                ExecuteType = method,
                                Method = method,
                                Class = tableName,
                                Function = path,
                                Params = sanitizedParams,
                                Raw = sanitizedParams,
                                TableName = tableName,
                                DiffValue = !string.IsNullOrEmpty(responseBodyText) ? responseBodyText : JsonSerializer.Serialize(new { statusCode = statusCode }),
                                Host = hostHeader,
                                Ip = remoteIp,
                                BaseUrl = path,
                                Hostname = requestHost,
                                OriginalUrl = path,
                                CreatedAt = DateTime.UtcNow
                            };

                            dbContext.Audit.Add(auditLog);
                            await dbContext.SaveChangesAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Audit System] Failed to write audit log to DB: {ex}");
                    }
                });
            }
        }

        private string? SanitizeBody(string? body)
        {
            if (string.IsNullOrEmpty(body)) return null;

            try
            {
                using (var document = JsonDocument.Parse(body))
                {
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) return body;

                    var dictionary = root.EnumerateObject().ToDictionary(
                        prop => prop.Name,
                        prop =>
                        {
                            var lowerName = prop.Name.ToLower();
                            if (lowerName.Contains("password") || lowerName.Contains("token"))
                            {
                                return "******";
                            }
                            return prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : prop.Value.GetRawText();
                        }
                    );

                    return JsonSerializer.Serialize(dictionary);
                }
            }
            catch
            {
                return body;
            }
        }
    }
}
