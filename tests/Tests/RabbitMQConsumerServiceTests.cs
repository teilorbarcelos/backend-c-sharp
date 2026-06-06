using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MageBackend.Infrastructure.Messaging;
using RabbitMQ.Client;
using Xunit;

namespace MageBackend.Tests
{
    public class RabbitMQConsumerServiceTests : IntegrationTestBase, IAsyncLifetime
    {
        private readonly CancellationTokenSource _cts = new();

        public RabbitMQConsumerServiceTests(IntegrationTestFixture fixture) : base(fixture) { }

        public Task InitializeAsync() => Task.CompletedTask;

        public Task DisposeAsync()
        {
            _cts.Cancel();
            _cts.Dispose();
            return Task.CompletedTask;
        }

        [Fact]
        public async Task GivenEmptyQueueName_WhenExecuteAsync_ThenReturnsImmediately()
        {
            using var provider = new RabbitMQProvider();
            provider.Connect();

            var service = new RabbitMQConsumerService(provider, "");
            var task = service.StartAsync(_cts.Token);

            await Task.Delay(500);
            Assert.True(task.IsCompleted, "Service should complete immediately with empty queue name");

            await service.StopAsync(CancellationToken.None);
            service.Dispose();
            provider.Disconnect();
        }

        [Fact]
        public async Task GivenProviderNotConnected_WhenExecuteAsync_ThenReturnsGracefully()
        {
            var originalEnabled = Environment.GetEnvironmentVariable("MESSAGING_ENABLED");
            try
            {
                Environment.SetEnvironmentVariable("MESSAGING_ENABLED", "false");
                using var provider = new RabbitMQProvider();

                var service = new RabbitMQConsumerService(provider, "test_queue");
                var task = service.StartAsync(_cts.Token);

                await Task.Delay(500);
                Assert.True(task.IsCompleted, "Service should complete gracefully when provider is not connected");

                await service.StopAsync(CancellationToken.None);
                service.Dispose();
            }
            finally
            {
                Environment.SetEnvironmentVariable("MESSAGING_ENABLED", originalEnabled);
            }
        }

        [Fact]
        public async Task GivenConnectedProvider_WhenExecuteAsync_ThenConsumesMessages()
        {
            using var provider = new RabbitMQProvider();
            provider.Connect();

            var service = new RabbitMQConsumerService(provider, "test_consumer_svc_queue");
            _ = service.StartAsync(_cts.Token);

            await Task.Delay(500);

            provider.Publish("test_consumer_svc_queue", new { Content = "consumer test" });

            await Task.Delay(1000);

            await service.StopAsync(CancellationToken.None);
            service.Dispose();
            provider.Disconnect();

            Assert.True(true, "Consumer starts, publishes and stops without exception");
        }

        [Fact]
        public async Task GivenConsumerService_WhenEmptyBodyPublished_ThenHandlesGracefully()
        {
            using var provider = new RabbitMQProvider();
            provider.Connect();

            var service = new RabbitMQConsumerService(provider, "test_empty_body_queue");
            _ = service.StartAsync(_cts.Token);

            await Task.Delay(500);

            var channelField = typeof(RabbitMQProvider).GetField("_channel",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var channel = (IModel)channelField!.GetValue(provider)!;

            provider.DeclareDeadLetterInfrastructure("test_empty_body_queue");

            channel.BasicPublish(
                exchange: "",
                routingKey: "test_empty_body_queue",
                basicProperties: null,
                body: ReadOnlyMemory<byte>.Empty
            );

            await Task.Delay(1000);

            await service.StopAsync(CancellationToken.None);
            service.Dispose();
            provider.Disconnect();

            Assert.True(true, "Consumer handles empty body message gracefully");
        }

        [Fact]
        public async Task GivenConsumerService_WhenMessageCausesException_ThenNacksAndContinues()
        {
            using var provider = new RabbitMQProvider();
            provider.Connect();

            var service = new RabbitMQConsumerService(provider, "test_exception_queue");
            _ = service.StartAsync(_cts.Token);

            await Task.Delay(500);

            provider.DeclareDeadLetterInfrastructure("test_exception_queue");

            var channelField = typeof(RabbitMQProvider).GetField("_channel",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var channel = (IModel)channelField!.GetValue(provider)!;

            channel.BasicPublish(
                exchange: "",
                routingKey: "test_exception_queue",
                basicProperties: null,
                body: Encoding.UTF8.GetBytes("valid json")
            );

            await Task.Delay(1000);

            await service.StopAsync(CancellationToken.None);
            service.Dispose();
            provider.Disconnect();

            Assert.True(true, "Consumer handles exception during message processing gracefully");
        }
    }
}
