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

        private static bool IsPublicPath(string? path)
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
                var token = context.Request.Headers.Authorization.FirstOrDefault()?.Split(" ")[^1];
                if (token != null)
                {
                    var userId = context.User.FindFirst("id")?.Value;

                    if (!string.IsNullOrEmpty(userId))
                    {
                        var tokenBytes = Encoding.UTF8.GetBytes(token);
                        var hashBytes = SHA256.HashData(tokenBytes);
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
