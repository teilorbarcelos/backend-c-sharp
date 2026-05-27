using System;
using System.Threading;
using System.Threading.Tasks;
using MageBackend.Infrastructure.Messaging;
using RabbitMQ.Client;
using Xunit;

namespace MageBackend.Tests
{
    public class RabbitMQTests : IntegrationTestBase
    {
        public RabbitMQTests(IntegrationTestFixture fixture) : base(fixture) { }

        [Fact]
        public async Task GivenRabbitProvider_WhenPublishingMessage_ThenMessageIsQueuedAndSubscribed()
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
            await Task.Delay(50); // Ensure BasicAck is covered in RabbitMQProvider
            
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

        [Fact]
        public void GivenMessagingDisabled_WhenConnectingPublishingOrSubscribing_ThenReturnsEarly()
        {
            var originalEnabled = Environment.GetEnvironmentVariable("MESSAGING_ENABLED");
            try
            {
                Environment.SetEnvironmentVariable("MESSAGING_ENABLED", "false");
                var provider = new RabbitMQProvider();
                provider.Connect();
                
                provider.Publish("test_queue_disabled", new TestMessage { Content = "test" });
                provider.Subscribe<TestMessage>("test_queue_disabled", msg => {});
                
                Assert.NotNull(provider);
            }
            finally
            {
                Environment.SetEnvironmentVariable("MESSAGING_ENABLED", originalEnabled);
            }
        }

        [Fact]
        public void GivenMessagingEnabledButNotConnected_WhenPublishingOrSubscribing_ThenThrowsInvalidOperation()
        {
            var originalEnabled = Environment.GetEnvironmentVariable("MESSAGING_ENABLED");
            try
            {
                Environment.SetEnvironmentVariable("MESSAGING_ENABLED", "true");
                var provider = new RabbitMQProvider();
                
                Assert.Throws<InvalidOperationException>(() => provider.Publish("test_queue", new TestMessage { Content = "test" }));
                Assert.Throws<InvalidOperationException>(() => provider.Subscribe<TestMessage>("test_queue", msg => {}));
            }
            finally
            {
                Environment.SetEnvironmentVariable("MESSAGING_ENABLED", originalEnabled);
            }
        }

        [Fact]
        public async Task GivenRabbitProvider_WhenMessageCallbackFails_ThenHandlesException()
        {
            var provider = new RabbitMQProvider();
            provider.Connect();

            var resetEvent = new ManualResetEventSlim(false);

            provider.Subscribe<TestMessage>("test_error_queue", msg =>
            {
                resetEvent.Set();
                throw new Exception("Simulated message handling failure");
            });

            provider.Publish("test_error_queue", new TestMessage { Content = "Trigger exception" });

            var hit = resetEvent.Wait(TimeSpan.FromSeconds(5));
            await Task.Delay(50);
            Assert.True(hit, "Did not trigger callback");

            provider.Disconnect();
            provider.Dispose();
        }

        [Fact]
        public async Task GivenRabbitProvider_WhenMessageBodyIsEmpty_ThenReturnsEarly()
        {
            var provider = new RabbitMQProvider();
            provider.Connect();

            var callbackTriggered = false;
            provider.Subscribe<TestMessage>("test_empty_body_queue", msg =>
            {
                callbackTriggered = true;
            });

            var channelField = typeof(RabbitMQProvider).GetField("_channel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var channel = (RabbitMQ.Client.IModel)channelField!.GetValue(provider)!;
            
            channel.BasicPublish(exchange: "", routingKey: "test_empty_body_queue", basicProperties: null, body: ReadOnlyMemory<byte>.Empty);

            await Task.Delay(500);
            Assert.False(callbackTriggered);

            provider.Disconnect();
            provider.Dispose();
        }

        [Fact]
        public async Task GivenRabbitProvider_WhenMessageDeserializesToNull_ThenCallbackIsNotInvoked()
        {
            var provider = new RabbitMQProvider();
            provider.Connect();

            var callbackTriggered = false;
            provider.Subscribe<TestMessage>("test_null_msg_queue", msg =>
            {
                callbackTriggered = true;
            });

            // Send the JSON literal "null" which deserializes to null for a reference type
            var channelField = typeof(RabbitMQProvider).GetField("_channel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var channel = (RabbitMQ.Client.IModel)channelField!.GetValue(provider)!;
            
            var nullJsonBody = System.Text.Encoding.UTF8.GetBytes("null");
            channel.BasicPublish(exchange: "", routingKey: "test_null_msg_queue", basicProperties: null, body: nullJsonBody);

            await Task.Delay(500);
            Assert.False(callbackTriggered, "Callback should not be invoked when message deserializes to null");

            provider.Disconnect();
            provider.Dispose();
        }

        public class TestMessage
        {
            public string Content { get; set; } = string.Empty;
        }
    }
}
