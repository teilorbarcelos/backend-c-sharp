using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Text.Json;
using System.Threading.Tasks;
using MageBackend.Database;
using FluentValidation;
using System.Linq;
using Serilog;

namespace MageBackend.Core.Middleware
{
    public class AppException : Exception
    {
        public int StatusCode { get; }
        public object? Details { get; }

        public AppException(string message, int statusCode = 400, object? details = null) : base(message)
        {
            StatusCode = statusCode;
            Details = details;
        }
    }

    public class ErrorHandlerMiddleware
    {
        private readonly RequestDelegate _next;

        public ErrorHandlerMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(context, ex);
            }
        }

        private async Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            var statusCode = StatusCodes.Status500InternalServerError;
            string message = "Internal Server Error";
            object? errors = null;

            if (exception is AppException appEx)
            {
                statusCode = appEx.StatusCode;
                message = appEx.Message;
                errors = appEx.Details;
            }
            else if (exception is ValidationException valEx)
            {
                statusCode = StatusCodes.Status400BadRequest;
                message = "Validation failed";
                errors = valEx.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(
                        g => ConvertToCamelCase(g.Key),
                        g => g.Select(e => e.ErrorMessage).ToArray()
                    );
            }

            /* Log non-controlled errors or ANY error if authenticated user is present */
            var isControlledError = exception is AppException || exception is ValidationException;
            var userId = context.User?.FindFirst("id")?.Value;

            if (!isControlledError || userId != null)
            {
                try
                {
                    /* Create db context scoped to this error logging operation */
                    var dbContext = context.RequestServices.GetRequiredService<ApplicationDbContext>();
                    var errorLog = new ErrorLog
                    {
                        IdUser = userId,
                        Source = $"{context.Request.Method} {context.Request.Path}",
                        ErrorMessage = exception.Message ?? "Unknown Error",
                        ErrorData = JsonSerializer.Serialize(new
                        {
                            name = exception.GetType().Name,
                            statusCode = statusCode,
                            details = errors,
                            stack = exception.StackTrace
                        }),
                        CreatedAt = DateTime.UtcNow
                    };
                    dbContext.ErrorLog.Add(errorLog);
                    await dbContext.SaveChangesAsync();
                }
                catch (Exception dbEx)
                {
                    Log.Error(dbEx, "[ErrorHandler] Failed to write error log to DB");
                }
            }

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = statusCode;

            object responseObj;
            if (statusCode == 401)
            {
                responseObj = new
                {
                    error = "UnauthorizedError",
                    message = message
                };
            }
            else
            {
                responseObj = new
                {
                    message = message,
                    errors = errors
                };
            }

            var responseBody = JsonSerializer.Serialize(responseObj);
            await context.Response.WriteAsync(responseBody);
        }

        private static string ConvertToCamelCase(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            return char.ToLower(input[0]) + input.Substring(1);
        }
    }
}
