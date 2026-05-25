using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.MsSql;
using Testcontainers.Redis;
using Testcontainers.RabbitMq;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using Xunit;

namespace MageBackend.Tests
{
    public class IntegrationTestFixture : WebApplicationFactory<Program>, IAsyncLifetime
    {
        private readonly MsSqlContainer _msSqlContainer = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .WithPassword("YourPassword123!") // Needs to meet complexity requirements
            .Build();

        private readonly RedisContainer _redisContainer = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .Build();

        private readonly RabbitMqContainer _rabbitMqContainer = new RabbitMqBuilder()
            .WithImage("rabbitmq:3-management-alpine")
            .Build();

        public async Task InitializeAsync()
        {
            await _msSqlContainer.StartAsync();
            await _redisContainer.StartAsync();
            await _rabbitMqContainer.StartAsync();

            var connectionString = _msSqlContainer.GetConnectionString();
            
            // Set environment variables for the SUT
            Environment.SetEnvironmentVariable("DATABASE_URL", connectionString);
            Environment.SetEnvironmentVariable("DATABASE_URL_AUDIT", connectionString);
            Environment.SetEnvironmentVariable("REDIS_URL", _redisContainer.GetConnectionString());
            Environment.SetEnvironmentVariable("MESSAGING_ENABLED", "true");
            Environment.SetEnvironmentVariable("RABBIT_URL", _rabbitMqContainer.GetConnectionString());
            Environment.SetEnvironmentVariable("DISABLE_RATE_LIMIT", "true");
        }

        public new async Task DisposeAsync()
        {
            await _msSqlContainer.DisposeAsync();
            await _redisContainer.DisposeAsync();
            await _rabbitMqContainer.DisposeAsync();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(MageBackend.Infrastructure.Storage.IStorageProvider));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }
                services.AddSingleton<MageBackend.Infrastructure.Storage.IStorageProvider, MageBackend.Infrastructure.Storage.LocalStorageProvider>();

                var pdfDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(MageBackend.Infrastructure.Pdf.IPdfProvider));
                if (pdfDescriptor != null)
                {
                    services.Remove(pdfDescriptor);
                }
                services.AddSingleton<MageBackend.Infrastructure.Pdf.IPdfProvider, FakePdfProvider>();
            });
        }
    }
}
