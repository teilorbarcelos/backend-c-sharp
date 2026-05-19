using dotenv.net;
using Microsoft.EntityFrameworkCore;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using MageBackend.Database;
using MageBackend.Infrastructure.Auth;
using MageBackend.Core.Middleware;
using Prometheus;
using FluentValidation;

DotEnv.Load(options: new DotEnvOptions(envFilePaths: new[] { "../.env", ".env" }, ignoreExceptions: true));

var builder = WebApplication.CreateBuilder(args);


// Load port from env
var port = Environment.GetEnvironmentVariable("PORT") ?? "8888";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// Database Connection
var dbUrl = Environment.GetEnvironmentVariable("DATABASE_URL") ?? "Server=localhost,1433;Database=backend_c_sharp;User Id=sa;Password=YourPassword123;TrustServerCertificate=True;";
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(dbUrl));

// Redis initialization
var redisUrl = Environment.GetEnvironmentVariable("REDIS_URL") ?? Environment.GetEnvironmentVariable("REDIS_HOST") ?? "localhost:6379";
RedisProvider.Initialize(redisUrl);

// JWT Provider
var jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET") ?? "super-secret-key-that-is-very-long-and-secure-123456";
builder.Services.AddSingleton(new JwtProvider(jwtSecret));

// FluentValidation
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

// Controllers with JSON formatting matching Fastify (camelCase by default)
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.SetIsOriginAllowed(origin => true)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Servers = new List<Microsoft.OpenApi.OpenApiServer>
        {
            new() { Url = "v1/" }
        };
        return Task.CompletedTask;
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "v1");
        options.RoutePrefix = "v1/docs";
    });
}

// Global Exception Handler must be first
app.UseMiddleware<ErrorHandlerMiddleware>();

app.UseCors("AllowAll");

// Rate limiting
app.UseMiddleware<RateLimitMiddleware>();

// JWT Authentication
app.UseMiddleware<JwtAuthenticationMiddleware>();

// Session Validation (against Redis)
app.UseMiddleware<TokenSessionValidationMiddleware>();

// Request Audit Logging
app.UseMiddleware<AuditLogMiddleware>();

// HTTP request metrics tracking for Prometheus
app.UseHttpMetrics();

app.UseRouting();
app.MapControllers();

// Observability endpoints
app.MapGet("/health", () => Results.Ok(new { status = "UP", timestamp = DateTime.UtcNow.ToString("o") }));
app.MapMetrics(); // Exposes /metrics

// Initialize and Seed Database
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await DbInitializer.InitializeAsync(dbContext);
}

app.Run();
