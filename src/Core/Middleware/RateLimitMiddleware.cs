using MageBackend.Infrastructure.Auth;
using Microsoft.AspNetCore.Http;
using Serilog;
using StackExchange.Redis;
using System;
using System.Threading.Tasks;

namespace MageBackend.Core.Middleware
{
    public class RateLimitMiddleware
    {
        private const int DefaultLimit = 100;
        private const int DefaultWindowSeconds = 60;
        private const string KeyPrefix = "ratelimit:ip:";

        /*
         * Script Lua atômico: INCR + EXPIRE (apenas no 1º hit) + retorna [count, ttl].
         * Garante que todas as réplicas compartilhem o mesmo contador via Redis,
         * eliminando o vetor de bypass "N réplicas × limite" do contador in-memory anterior.
         */
        private static readonly LuaScript RateLimitScript = LuaScript.Prepare(
            "local current = redis.call('INCR', @key)\n" +
            "if current == 1 then\n" +
            "  redis.call('EXPIRE', @key, @window)\n" +
            "end\n" +
            "local ttl = redis.call('TTL', @key)\n" +
            "return {current, ttl}");

        internal static Func<IDatabase>? DatabaseAccessor { get; set; }

        private readonly RequestDelegate _next;

        public RateLimitMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var limit = ReadEnvInt("RATE_LIMIT_MAX", DefaultLimit);
            var windowSeconds = ReadEnvInt("RATE_LIMIT_WINDOW_SECONDS", DefaultWindowSeconds);

            if (IsDisabled())
            {
                WriteHeaders(context, limit, limit, windowSeconds);
                await _next(context);
                return;
            }

            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
            var key = KeyPrefix + ip;

            var (count, ttl) = await TryIncrementAsync(key, windowSeconds);

            if (count < 0)
            {
                WriteHeaders(context, limit, limit, windowSeconds);
                await _next(context);
                return;
            }

            var remaining = (int)Math.Max(0, limit - count);
            var resetSeconds = ttl > 0 ? (int)ttl : windowSeconds;
            WriteHeaders(context, limit, remaining, resetSeconds);

            if (count > limit)
            {
                await Reject429Async(context);
                return;
            }

            await _next(context);
        }

        internal static async Task<(long count, long ttl)> TryIncrementAsync(string key, int windowSeconds)
        {
            try
            {
                var db = (DatabaseAccessor ?? (() => RedisProvider.Database))();
                var result = await db.ScriptEvaluateAsync(
                    RateLimitScript,
                    new { key = (RedisKey)key, window = windowSeconds });

                var values = (long[])result!;
                return (values[0], values[1]);
            }
#pragma warning disable S2221
            /*
             * Catch genérico justificado: o rate limit é defesa best-effort.
             * Qualquer falha (Redis down, timeout, payload inesperado, NRE) deve
             * fazer fail-open ao invés de derrubar a request com 500 — preserva
             * a disponibilidade do API mesmo em degradação do Redis.
             */
            catch (Exception ex)
            {
                Log.Warning(ex, "[RateLimit] Redis call failed for key {Key}; failing open", key);
                return (-1, 0);
            }
#pragma warning restore S2221
        }

        private static bool IsDisabled()
        {
            return Environment.GetEnvironmentVariable("DISABLE_RATE_LIMIT") == "true" ||
                   Environment.GetEnvironmentVariable("ENVIRONMENT") == "test";
        }

        private static void WriteHeaders(HttpContext context, int limit, int remaining, int resetSeconds)
        {
            context.Response.Headers["x-ratelimit-limit"] = limit.ToString();
            context.Response.Headers["x-ratelimit-remaining"] = remaining.ToString();
            context.Response.Headers["x-ratelimit-reset"] = resetSeconds.ToString();
        }

        private static async Task Reject429Async(HttpContext context)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"message\": \"Too many requests. Please try again later.\"}");
        }

        private static int ReadEnvInt(string name, int defaultValue)
        {
            var raw = Environment.GetEnvironmentVariable(name);
            return int.TryParse(raw, out var parsed) && parsed > 0 ? parsed : defaultValue;
        }
    }
}
