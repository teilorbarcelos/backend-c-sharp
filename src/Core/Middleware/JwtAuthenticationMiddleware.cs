using Microsoft.AspNetCore.Http;
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using MageBackend.Infrastructure.Auth;

namespace MageBackend.Core.Middleware
{
    public class JwtAuthenticationMiddleware
    {
        private readonly RequestDelegate _next;

        public JwtAuthenticationMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, JwtProvider jwtProvider)
        {
            var authHeader = context.Request.Headers["Authorization"].ToString();
            if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = authHeader.Substring(7).Trim();
                try
                {
                    var payload = jwtProvider.VerifyToken(token);
                    if (payload != null)
                    {
                        var claims = new[]
                        {
                            new Claim("id", payload.Id),
                            new Claim("email", payload.Email),
                            new Claim("roleId", payload.RoleId),
                            new Claim("permissions", System.Text.Json.JsonSerializer.Serialize(payload.Permissions))
                        };
                        var identity = new ClaimsIdentity(claims, "jwt");
                        context.User = new ClaimsPrincipal(identity);
                    }
                }
                catch
                {
                    /* Token is invalid. Let downstream authorization filters handle it if the endpoint is protected. */
                }
            }

            await _next(context);
        }
    }
}
