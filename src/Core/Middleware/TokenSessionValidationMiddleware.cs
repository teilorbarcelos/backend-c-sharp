using Microsoft.AspNetCore.Http;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using MageBackend.Infrastructure.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace MageBackend.Core.Middleware
{
    public class TokenSessionValidationMiddleware
    {
        private readonly RequestDelegate _next;
        private static readonly TimeSpan SessionVersionTtl = TimeSpan.FromDays(7);

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

            if (context.User.Identity?.IsAuthenticated != true)
            {
                await _next(context);
                return;
            }

            var userId = context.User.FindFirst("id")?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                await _next(context);
                return;
            }

            var tokenVersion = ParseTokenVersion(context.User.FindFirst("sv")?.Value);
            var currentVersion = await GetCurrentVersionAsync(context, userId);

            if (currentVersion == null || tokenVersion != currentVersion.Value)
            {
                await RejectAsync(context);
                return;
            }

            await _next(context);
        }

        private static int ParseTokenVersion(string? svClaim)
        {
            if (string.IsNullOrEmpty(svClaim)) return 1;
            return int.TryParse(svClaim, out var v) ? v : 1;
        }

        private static async Task<int?> GetCurrentVersionAsync(HttpContext context, string userId)
        {
            var sessionKey = $"session:user:{userId}:version";
            var redisDb = RedisProvider.Database;
            var redisValue = await redisDb.StringGetAsync(sessionKey);

            if (redisValue.HasValue && int.TryParse(redisValue.ToString(), out var cached))
            {
                return cached;
            }

            return await HydrateFromDatabaseAsync(context, userId, redisDb, sessionKey);
        }

        private static async Task<int?> HydrateFromDatabaseAsync(
            HttpContext context, string userId, IDatabase redisDb, string sessionKey)
        {
            using var scope = context.RequestServices.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<MageBackend.Database.ApplicationDbContext>();
            var user = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
                Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AsNoTracking(
                    Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.Include(dbContext.User, u => u.Auth)
                ),
                u => u.Id == userId
            );

            if (user?.Auth == null) return null;

            var version = user.Auth.SessionVersion;
            await redisDb.StringSetAsync(sessionKey, version.ToString(), SessionVersionTtl);
            return version;
        }

        private static async Task RejectAsync(HttpContext context)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"error\": \"UnauthorizedError\", \"message\": \"Sessão inválida ou expirada. Faça login novamente.\"}");
        }
    }
}
