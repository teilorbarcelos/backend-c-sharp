using System;
using System.Threading;
using System.Threading.Tasks;
using MageBackend.Infrastructure.Messaging;
using Xunit;

namespace MageBackend.Tests
{
    public class RabbitMQTests : IntegrationTestBase
    {
        public RabbitMQTests(IntegrationTestFixture fixture) : base(fixture) { }

        [Fact]
        public void GivenRabbitProvider_WhenPublishingMessage_ThenMessageIsQueuedAndSubscribed()
        {
            var provider = new RabbitMQProvider();
            provider.Connect();

            var receivedMessage = string.Empty;
            var resetEvent = new ManualResetEventSlim(false);

            provider.Subscribe<TestMessage>("test_queue", msg =>
            {
                receivedMessage = msg.Content;
                resetEvent.Set();
            });

            provider.Publish("test_queue", new TestMessage { Content = "Hello Rabbit!" });

            var hit = resetEvent.Wait(TimeSpan.FromSeconds(5));
            
            Assert.True(hit, "Did not receive message within 5 seconds");
            Assert.Equal("Hello Rabbit!", receivedMessage);

            provider.Disconnect();
            provider.Dispose();
        }

        [Fact]
        public void GivenInvalidUrl_WhenConnecting_ThenThrowsException()
        {
            var originalUrl = Environment.GetEnvironmentVariable("RABBIT_URL");
            Environment.SetEnvironmentVariable("RABBIT_URL", "amqp://invalid-host:5672");
            var provider = new RabbitMQProvider();
            
            Assert.ThrowsAny<Exception>(() => provider.Connect());

            Environment.SetEnvironmentVariable("RABBIT_URL", originalUrl);
        }

        public class TestMessage
        {
            public string Content { get; set; } = string.Empty;
        }
    }
}
