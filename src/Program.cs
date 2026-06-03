using dotenv.net;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;
using MageBackend.Database;
using MageBackend.Infrastructure.Auth;
using MageBackend.Core.Middleware;
using Prometheus;
using Prometheus.DotNetRuntime;
using FluentValidation;
using MageBackend.Infrastructure.Messaging;
using MageBackend.Infrastructure.Storage;
using MageBackend.Infrastructure.Pdf;
using Serilog;
using Serilog.Events;

var envFiles = new[] { "../.env", ".env" };
DotEnv.Load(options: new DotEnvOptions(envFilePaths: envFiles, ignoreExceptions: true));


Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("Starting MageBackend API...");

    var builder = WebApplication.CreateBuilder(args);

    if (builder.Environment.EnvironmentName != "Testing")
    {
        DotNetRuntimeStatsBuilder.Default().StartCollecting();
    }

    builder.Host.UseSerilog();

    var port = Environment.GetEnvironmentVariable("PORT") ?? "8888";
#pragma warning disable S5332
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
#pragma warning restore S5332

    var dbUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
    if (string.IsNullOrWhiteSpace(dbUrl))
    {
        throw new InvalidOperationException("DATABASE_URL environment variable is not set.");
    }
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseSqlServer(dbUrl));

    var redisUrl = Environment.GetEnvironmentVariable("REDIS_URL");
    if (string.IsNullOrWhiteSpace(redisUrl))
    {
        redisUrl = Environment.GetEnvironmentVariable("REDIS_HOST");
    }
    if (string.IsNullOrWhiteSpace(redisUrl))
    {
        throw new InvalidOperationException("REDIS_URL or REDIS_HOST environment variable is not set.");
    }
    RedisProvider.Initialize(redisUrl);

    var jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET");
    if (string.IsNullOrWhiteSpace(jwtSecret))
    {
        throw new InvalidOperationException("JWT_SECRET environment variable is not set.");
    }
    builder.Services.AddSingleton(new JwtProvider(jwtSecret));

    var rabbitUrl = Environment.GetEnvironmentVariable("RABBIT_URL");
    if (string.IsNullOrWhiteSpace(rabbitUrl))
    {
        throw new InvalidOperationException("RABBIT_URL environment variable is not set.");
    }
    Environment.SetEnvironmentVariable("RABBIT_URL", rabbitUrl);
    builder.Services.AddSingleton<RabbitMQProvider>();
    builder.Services.AddSingleton<IStorageProvider, LocalStorageProvider>();
    builder.Services.AddHttpClient<IPdfProvider, PdfProvider>();

    builder.Services.AddValidatorsFromAssemblyContaining<Program>();
    builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());
    builder.Services.AddSingleton<MageBackend.Core.IEntityMapper<MageBackend.Database.Product, MageBackend.Features.Product.ProductResponseDto>, MageBackend.Features.Product.ProductEntityMapper>();
    builder.Services.AddCrudHandlers<MageBackend.Database.Product, MageBackend.Features.Product.ProductResponseDto>();

    builder.Services.AddSingleton<MageBackend.Core.IEntityMapper<MageBackend.Database.User, MageBackend.Features.User.UserResponseDto>, MageBackend.Features.User.UserEntityMapper>();
    builder.Services.AddCrudHandlers<MageBackend.Database.User, MageBackend.Features.User.UserResponseDto>();

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

    if (app.Environment.IsDevelopment() || app.Environment.EnvironmentName == "Testing")
    {
        app.MapOpenApi();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/openapi/v1.json", "v1");
            options.RoutePrefix = "v1/docs";
        });
    }

    app.UseMiddleware<ErrorHandlerMiddleware>();

    app.UseCors("AllowAll");

    app.UseMiddleware<RequestLoggingMiddleware>();

    app.UseMiddleware<RateLimitMiddleware>();

    app.UseMiddleware<JwtAuthenticationMiddleware>();

    app.UseMiddleware<TokenSessionValidationMiddleware>();

    app.UseMiddleware<AuditLogMiddleware>();

    app.UseHttpMetrics();

    app.UseRouting();
    app.MapControllers();

    app.MapGet("/health", () => Results.Ok(new { status = "UP", timestamp = DateTime.UtcNow.ToString("o") }));
    app.MapMetrics();

    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await DbInitializer.InitializeAsync(dbContext);
    }

    var rabbitProvider = app.Services.GetRequiredService<RabbitMQProvider>();
    rabbitProvider.Connect();

    Log.Information("Server ready at http://localhost:{Port} | Docs: http://localhost:{DocsPort}/v1/docs | Audit: http://localhost:{AuditPort}/admin/logs", port, port, port);

    await app.RunAsync();
}
catch (Exception ex) when (ex.GetType().Name == "HostAbortedException")
{
    /*
     * Ignorado intencionalmente: O EF Core tooling (dotnet ef) usa essa exceção
     * para interromper o Host logo após obter as configurações do DbContext.
     */
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application startup failed");
}
finally
{
    await Log.CloseAndFlushAsync();
}
