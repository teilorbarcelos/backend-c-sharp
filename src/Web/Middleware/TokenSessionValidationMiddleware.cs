using Microsoft.AspNetCore.Http;
using System;
using System.Threading.Tasks;
using MageBackend.Infrastructure.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace MageBackend.Web.Middleware
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

            using var scope = context.RequestServices.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<MageBackend.Database.ApplicationDbContext>();
            var currentVersion = await SessionManager.GetCurrentVersionAsync(userId, dbContext);

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

        private static async Task RejectAsync(HttpContext context)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"error\": \"UnauthorizedError\", \"message\": \"Sess\u00e3o inv\u00e1lida ou expirada. Fa\u00e7a login novamente.\"}");
        }
    }
}
