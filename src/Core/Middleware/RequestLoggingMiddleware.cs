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
            }
            finally
            {
                sw.Stop();

                var statusCode = context.Response.StatusCode;
                Serilog.Events.LogEventLevel level;
                if (statusCode >= 500)
                {
                    level = Serilog.Events.LogEventLevel.Error;
                }
                else if (statusCode >= 400)
                {
                    level = Serilog.Events.LogEventLevel.Warning;
                }
                else
                {
                    level = Serilog.Events.LogEventLevel.Information;
                }

                Log.Write(level,
                    "{Method} {Path}{Query} → {StatusCode} ({Duration}ms)",
                    method, path, queryString, statusCode, sw.ElapsedMilliseconds);
            }
        }
    }
}
