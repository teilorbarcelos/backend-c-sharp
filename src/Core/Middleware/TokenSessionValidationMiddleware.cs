using Microsoft.AspNetCore.Http;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using MageBackend.Infrastructure.Auth;

namespace MageBackend.Core.Middleware
{
    public class TokenSessionValidationMiddleware
    {
        private readonly RequestDelegate _next;

        public TokenSessionValidationMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        private bool IsPublicPath(string? path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var p = path.ToLower();
            return p == "/health" ||
                   p == "/metrics" ||
                   p == "/v1/auth/login" ||
                   p == "/v1/auth/refresh" ||
                   p.StartsWith("/v1/auth/password/");
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (IsPublicPath(context.Request.Path.Value))
            {
                await _next(context);
                return;
            }

            if (context.User.Identity?.IsAuthenticated == true)
            {
                var authHeader = context.Request.Headers["Authorization"].ToString();
                if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    var token = authHeader.Substring(7).Trim();
                    var userId = context.User.FindFirst("id")?.Value;

                    if (!string.IsNullOrEmpty(userId))
                    {
                        using var sha256 = SHA256.Create();
                        var tokenBytes = Encoding.UTF8.GetBytes(token);
                        var hashBytes = sha256.ComputeHash(tokenBytes);
                        var tokenHash = Convert.ToHexString(hashBytes).ToLower();

                        var sessionKey = $"session:user:{userId}:access:{tokenHash}";
                        var redisDb = RedisProvider.Database;

                        var sessionExists = await redisDb.KeyExistsAsync(sessionKey);
                        if (!sessionExists)
                        {
                            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            context.Response.ContentType = "application/json";
                            await context.Response.WriteAsync("{\"error\": \"UnauthorizedError\", \"message\": \"Sessão inválida ou expirada. Faça login novamente.\"}");
                            return;
                        }
                    }
                }
            }

            await _next(context);
        }
    }
}
