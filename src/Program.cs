using dotenv.net;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;
using MageBackend.Web;
using MageBackend.Infrastructure.Auditing;
using MageBackend.Infrastructure.Configuration;
using MageBackend.Database;
using MageBackend.Infrastructure.Auth;
using MageBackend.Web.Middleware;
using Prometheus;
using Prometheus.DotNetRuntime;
using OpenTelemetry.Trace;
using FluentValidation;
using MageBackend.Infrastructure.Messaging;
using MageBackend.Infrastructure.Storage;
using MageBackend.Infrastructure.Pdf;
using Serilog;
using Serilog.Events;
using Serilog.Context;

var envFiles = new[] { "../.env", ".env" };
DotEnv.Load(options: new DotEnvOptions(envFilePaths: envFiles, ignoreExceptions: true));


Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {TraceId}{Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("Starting MageBackend API...");

    var builder = WebApplication.CreateBuilder(args);

    const string testingEnv = "Testing";
    if (builder.Environment.EnvironmentName != testingEnv)
    {
        DotNetRuntimeStatsBuilder.Default().StartCollecting();
    }

    var shutdownTimeout = int.TryParse(Environment.GetEnvironmentVariable("SHUTDOWN_TIMEOUT_SECONDS"), out var st) && st > 0 ? st : 30;
    builder.Host.ConfigureHostOptions(o => o.ShutdownTimeout = TimeSpan.FromSeconds(shutdownTimeout));
    Log.Information("[Host] Shutdown timeout configured to {Timeout}s", shutdownTimeout);

    builder.Host.UseSerilog();

    var port = Environment.GetEnvironmentVariable("PORT") ?? "8888";
#pragma warning disable S5332
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
#pragma warning restore S5332

    var dbUrl = EnvValidator.Required("DATABASE_URL");
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseSqlServer(dbUrl));

    var redisUrl = EnvValidator.RequiredAny("REDIS_URL", "REDIS_HOST");
    RedisProvider.Initialize(redisUrl);

    var jwtSecret = EnvValidator.Required("JWT_SECRET");
    builder.Services.AddSingleton(new JwtProvider(jwtSecret));

    var rabbitUrl = EnvValidator.Required("RABBIT_URL");
    Environment.SetEnvironmentVariable("RABBIT_URL", rabbitUrl);
    builder.Services.AddSingleton<RabbitMQProvider>();
    builder.Services.AddSingleton(sp =>
    {
        var provider = sp.GetRequiredService<RabbitMQProvider>();
        var queue = Environment.GetEnvironmentVariable("RABBIT_CONSUMER_QUEUE") ?? "";
        return new RabbitMQConsumerService(provider, queue);
    });
    builder.Services.AddHostedService(sp => sp.GetRequiredService<RabbitMQConsumerService>());
    builder.Services.AddHttpClient<IPdfProvider, PdfProvider>()
        .AddStandardResilienceHandler(PdfResilienceConfig.Configure);

    builder.Services.AddValidatorsFromAssemblyContaining<Program>();
    builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());

    builder.Services.AddSingleton<IAuditLogQueue, AuditLogQueue>();
    builder.Services.AddHostedService<AuditLogBackgroundService>();
    builder.Services.AddSingleton<MageBackend.Domain.IEntityMapper<MageBackend.Database.Product, MageBackend.Features.Product.ProductResponseDto>, MageBackend.Features.Product.ProductEntityMapper>();
    builder.Services.AddCrudHandlers<MageBackend.Database.Product, MageBackend.Features.Product.ProductResponseDto>();

    builder.Services.AddSingleton<MageBackend.Domain.IEntityMapper<MageBackend.Database.User, MageBackend.Features.User.UserResponseDto>, MageBackend.Features.User.UserEntityMapper>();
    builder.Services.AddCrudHandlers<MageBackend.Database.User, MageBackend.Features.User.UserResponseDto>();

    builder.Services.AddControllers()
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });

    builder.Services.AddCors(options =>
    {
        var allowedOrigins = CorsConfig.GetAllowedOrigins(builder.Environment.EnvironmentName);

        options.AddPolicy("Default", policy =>
        {
            policy.WithOrigins(allowedOrigins.ToArray())
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        });
    });

    if (OpenTelemetryConfig.IsEnabled())
    {
        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation()
                       .AddEntityFrameworkCoreInstrumentation(o => o.SetDbStatementForText = true)
                       .AddRedisInstrumentation(RedisProvider.Connection)
                       .AddHttpClientInstrumentation()
                       .AddOtlpExporter(o =>
                       {
                           o.Endpoint = new Uri(OpenTelemetryConfig.GetOtlpEndpoint());
                       });
            });
    }

    if (builder.Environment.EnvironmentName != testingEnv)
    {
        builder.Services.AddAppHealthChecks();
    }

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

    if (app.Environment.IsDevelopment() || app.Environment.EnvironmentName == testingEnv)
    {
        app.MapOpenApi();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/openapi/v1.json", "v1");
            options.RoutePrefix = "v1/docs";
        });
    }

    app.UseMiddleware<ErrorHandlerMiddleware>();

    app.UseCors("Default");

    app.UseMiddleware<RequestLoggingMiddleware>();

    app.UseMiddleware<RateLimitMiddleware>();

    app.UseMiddleware<JwtAuthenticationMiddleware>();

    app.UseMiddleware<TokenSessionValidationMiddleware>();

    app.UseMiddleware<AuditLogMiddleware>();

    app.UseHttpMetrics();

    var authMode = Environment.GetEnvironmentVariable("AUTH_MODE") ?? "local";
    if (authMode.Equals("remote", StringComparison.OrdinalIgnoreCase))
    {
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/v1/auth"))
            {
                context.Response.StatusCode = 404;
                return;
            }
            await next();
        });
    }

    app.UseRouting();
    app.MapControllers();

    app.MapGet("/health", async (HttpContext http) =>
    {
        if (app.Environment.EnvironmentName == testingEnv)
            return Results.Ok(new { status = "UP", timestamp = DateTime.UtcNow.ToString("o") });

        return await HealthCheckConfig.RunHealthChecksAsync(http);
    });
    app.MapMetrics();

    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await DbInitializer.InitializeAsync(dbContext);
    }

    var migrateOnly = Environment.GetEnvironmentVariable("MIGRATE_ONLY") is string m && (m.Equals("true", StringComparison.OrdinalIgnoreCase) || m == "1");
    if (migrateOnly)
    {
        Log.Information("MIGRATE_ONLY=true — migrations applied, exiting.");
        return;
    }

    var rabbitProvider = app.Services.GetRequiredService<RabbitMQProvider>();
    rabbitProvider.Connect();

    var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
    lifetime.ApplicationStopping.Register(() =>
    {
        Log.Information("[Host] Shutdown requested — draining in-flight requests...");

        try
        {
            var rabbit = app.Services.GetRequiredService<RabbitMQProvider>();
            rabbit.Disconnect();
            Log.Information("[Host] RabbitMQ disconnected");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Host] Error disconnecting RabbitMQ during shutdown");
        }
    });

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
