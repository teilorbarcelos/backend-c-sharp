using System.Diagnostics;
using Serilog;

namespace MageBackend.Core.Middleware
{
    public class RequestLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private static readonly HashSet<string> _silentPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            "/health",
            "/metrics"
        };

        public RequestLoggingMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var path = context.Request.Path.Value ?? "/";

            /* Skip logging for high-frequency observability endpoints */
            if (_silentPaths.Contains(path))
            {
                await _next(context);
                return;
            }

            var method = context.Request.Method;
            var queryString = context.Request.QueryString.HasValue ? context.Request.QueryString.Value : "";
            var sw = Stopwatch.StartNew();

            try
            {
                await _next(context);
                sw.Stop();

                var statusCode = context.Response.StatusCode;
                var level = Serilog.Events.LogEventLevel.Information;
                if (statusCode >= 500)
                {
                    level = Serilog.Events.LogEventLevel.Error;
                }
                else if (statusCode >= 400)
                {
                    level = Serilog.Events.LogEventLevel.Warning;
                }

                Log.Write(level,
                    "{Method} {Path}{Query} → {StatusCode} ({Duration}ms)",
                    method, path, queryString, statusCode, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                sw.Stop();
                LogError(method, path, queryString, sw.ElapsedMilliseconds, ex);
                throw;
            }
        }

        private static void LogError(string method, string path, string queryString, long duration, Exception ex)
        {
            Log.Error(ex,
                "{Method} {Path}{Query} → 500 ({Duration}ms) Unhandled exception",
                method, path, queryString, duration);
        }
    }
}
