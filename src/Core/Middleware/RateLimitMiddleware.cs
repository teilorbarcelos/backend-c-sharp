using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace MageBackend.Core.Middleware
{
    public class RateLimitMiddleware
    {
        private readonly RequestDelegate _next;
        private static readonly ConcurrentDictionary<string, (int Count, DateTime WindowStart)> _cache = new();
        private const int Limit = 100;
        private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

        public RateLimitMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var disableRateLimit = Environment.GetEnvironmentVariable("DISABLE_RATE_LIMIT") == "true" ||
                                   Environment.GetEnvironmentVariable("ENVIRONMENT") == "test";

            if (disableRateLimit)
            {
                context.Response.Headers["x-ratelimit-limit"] = Limit.ToString();
                context.Response.Headers["x-ratelimit-remaining"] = Limit.ToString();
                await _next(context);
                return;
            }

            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
            var now = DateTime.UtcNow;

            var (count, windowStart) = _cache.GetOrAdd(ip, _ => (0, now));

            if (now - windowStart > Window)
            {
                /* Reset window */
                count = 1;
                windowStart = now;
                _cache[ip] = (count, windowStart);
            }
            else
            {
                count++;
                _cache[ip] = (count, windowStart);
            }

            var remaining = Math.Max(0, Limit - count);

            context.Response.Headers["x-ratelimit-limit"] = Limit.ToString();
            context.Response.Headers["x-ratelimit-remaining"] = remaining.ToString();

            if (count > Limit)
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"message\": \"Too many requests. Please try again later.\"}");
                return;
            }

            await _next(context);
        }
    }
}
