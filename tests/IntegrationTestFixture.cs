using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.MsSql;
using Testcontainers.Redis;
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

        public async Task InitializeAsync()
        {
            await _msSqlContainer.StartAsync();
            await _redisContainer.StartAsync();

            var connectionString = _msSqlContainer.GetConnectionString();
            
            // Set environment variables for the SUT
            Environment.SetEnvironmentVariable("DATABASE_URL", connectionString);
            Environment.SetEnvironmentVariable("DATABASE_URL_AUDIT", connectionString);
            Environment.SetEnvironmentVariable("REDIS_URL", _redisContainer.GetConnectionString());
            Environment.SetEnvironmentVariable("MESSAGING_ENABLED", "false");
            Environment.SetEnvironmentVariable("DISABLE_RATE_LIMIT", "true");
        }

        public new async Task DisposeAsync()
        {
            await _msSqlContainer.DisposeAsync();
            await _redisContainer.DisposeAsync();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // WebHost configuration overrides if needed
        }
    }
}
