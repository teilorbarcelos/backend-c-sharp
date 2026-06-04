using MageBackend.Core;
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
        /*
         * Limites por endpoint definidos em RateLimitConfig (single source of truth).
         * Cada endpoint tem bucket próprio no Redis (chave ratelimit:{Key}:{ip}),
         * então esgotar o limite de login não afeta a listagem de user, e
         * vice-versa. Defense em profundidade contra DoS coordenado em
         * múltiplos endpoints.
         *
         * Defaults globais ainda são env-overridable (RATE_LIMIT_MAX /
         * RATE_LIMIT_WINDOW_SECONDS) para tuning em runtime sem deploy.
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
            var path = context.Request.Path.Value;

            if (RateLimitConfig.IsExempt(path))
            {
                await _next(context);
                return;
            }

            var limit = RateLimitConfig.GetFor(path);
            var max = ApplyEnvOverride(limit.Max);
            var windowSeconds = ApplyEnvOverrideWindow(limit.WindowSeconds);

            /*
             * Mesmo quando o rate limit está desabilitado (kill switch) ou o
             * Redis está fora, expomos os headers com limit=remaining para
             * que o cliente saiba qual seria o teto. Não incrementamos o
             * contador — só exibimos o valor configurado.
             */
            if (IsDisabled())
            {
                WriteHeaders(context, max, max, windowSeconds);
                await _next(context);
                return;
            }

            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
            var key = $"ratelimit:{limit.Key}:{ip}";

            var (count, ttl) = await TryIncrementAsync(key, windowSeconds);

            if (count < 0)
            {
                WriteHeaders(context, max, max, windowSeconds);
                await _next(context);
                return;
            }

            var remaining = (int)Math.Max(0, max - count);
            var resetSeconds = ttl > 0 ? (int)ttl : windowSeconds;
            WriteHeaders(context, max, remaining, resetSeconds);

            if (count > max)
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
                var db = DatabaseAccessor != null ? DatabaseAccessor() : RedisProvider.Database;
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

        /*
         * Env vars sobrescrevem apenas o limite/window do bucket default.
         * Endpoints específicos (login, refresh, etc.) sempre usam os
         * valores hard-coded de RateLimitConfig — são decisões de segurança,
         * não de tuning operacional.
         */
        private static int ApplyEnvOverride(int defaultMax)
        {
            var raw = Environment.GetEnvironmentVariable("RATE_LIMIT_MAX");
            return int.TryParse(raw, out var parsed) && parsed > 0 ? parsed : defaultMax;
        }

        private static int ApplyEnvOverrideWindow(int defaultWindow)
        {
            var raw = Environment.GetEnvironmentVariable("RATE_LIMIT_WINDOW_SECONDS");
            return int.TryParse(raw, out var parsed) && parsed > 0 ? parsed : defaultWindow;
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
    }
}
