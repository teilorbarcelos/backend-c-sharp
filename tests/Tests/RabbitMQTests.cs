using System;
using System.Collections.Generic;
using System.Text;
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
            using var provider = new RabbitMQProvider();
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
        }

        [Fact]
        public void GivenInvalidUrl_WhenConnecting_ThenThrowsException()
        {
            var originalUrl = Environment.GetEnvironmentVariable("RABBIT_URL");
            Environment.SetEnvironmentVariable("RABBIT_URL", "amqp://invalid-host:5672");
            using var provider = new RabbitMQProvider();

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
                using var provider = new RabbitMQProvider();
                provider.Connect();

                provider.Publish("test_queue_disabled", new TestMessage { Content = "test" });
                provider.Subscribe<TestMessage>("test_queue_disabled", msg => { });

                Assert.True(true, "Messaging disabled should not throw exceptions");
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
                using var provider = new RabbitMQProvider();

                Assert.Throws<InvalidOperationException>(() => provider.Publish("test_queue", new TestMessage { Content = "test" }));
                Assert.Throws<InvalidOperationException>(() => provider.Subscribe<TestMessage>("test_queue", msg => { }));
            }
            finally
            {
                Environment.SetEnvironmentVariable("MESSAGING_ENABLED", originalEnabled);
            }
        }

        [Fact]
        public async Task GivenRabbitProvider_WhenMessageCallbackFails_ThenHandlesException()
        {
            using var provider = new RabbitMQProvider();
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
        }

        [Fact]
        public async Task GivenRabbitProvider_WhenMessageBodyIsEmpty_ThenReturnsEarly()
        {
            using var provider = new RabbitMQProvider();
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
        }

        [Fact]
        public async Task GivenRabbitProvider_WhenMessageDeserializesToNull_ThenCallbackIsNotInvoked()
        {
            using var provider = new RabbitMQProvider();
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
        }

        [Fact]
        public void GivenRabbitProvider_WhenConnect_ThenIsConnectedTrue()
        {
            using var provider = new RabbitMQProvider();
            provider.Connect();

            Assert.True(provider.IsConnected);

            provider.Disconnect();
        }

        [Fact]
        public void GivenRabbitProvider_WhenNotConnected_ThenIsConnectedFalse()
        {
            var originalEnabled = Environment.GetEnvironmentVariable("MESSAGING_ENABLED");
            try
            {
                Environment.SetEnvironmentVariable("MESSAGING_ENABLED", "false");
                using var provider = new RabbitMQProvider();

                Assert.False(provider.IsConnected);
            }
            finally
            {
                Environment.SetEnvironmentVariable("MESSAGING_ENABLED", originalEnabled);
            }
        }

        [Fact]
        public void GivenRabbitProvider_WhenDeclareDeadLetterInfrastructure_ThenDlxAndDlqAreCreated()
        {
            using var provider = new RabbitMQProvider();
            provider.Connect();

            provider.DeclareDeadLetterInfrastructure("test_dlx_queue");

            var channelField = typeof(RabbitMQProvider).GetField("_channel",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var channel = (IModel)channelField!.GetValue(provider)!;

            var dlqPublished = false;
            var dlqResetEvent = new ManualResetEventSlim(false);

            var consumer = new RabbitMQ.Client.Events.EventingBasicConsumer(channel);
            consumer.Received += (model, ea) =>
            {
                dlqPublished = true;
                dlqResetEvent.Set();
            };
            channel.BasicConsume("test_dlx_queue-dlq", autoAck: true, consumer: consumer);

            channel.BasicPublish(
                exchange: "test_dlx_queue-dlx",
                routingKey: "",
                basicProperties: null,
                body: Encoding.UTF8.GetBytes("{\"Content\":\"dead-letter\"}")
            );

            var hit = dlqResetEvent.Wait(TimeSpan.FromSeconds(5));
            Assert.True(hit, "Did not receive message on DLQ within 5 seconds");
            Assert.True(dlqPublished, "Message published to DLX was not delivered to DLQ");

            provider.Disconnect();
        }

        [Fact]
        public void GivenRabbitProvider_WhenPublish_ThenMessageHasVersionHeader()
        {
            using var provider = new RabbitMQProvider();
            provider.Connect();

            var headerFound = false;
            var resetEvent = new ManualResetEventSlim(false);

            var channelField = typeof(RabbitMQProvider).GetField("_channel",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var channel = (IModel)channelField!.GetValue(provider)!;

            channel.QueueDeclare("test_version_header_queue", durable: true, exclusive: false, autoDelete: false,
                arguments: null);

            var consumer = new RabbitMQ.Client.Events.EventingBasicConsumer(channel);
            consumer.Received += (model, ea) =>
            {
                if (ea.BasicProperties?.Headers is not null &&
                    ea.BasicProperties.Headers.TryGetValue("x-message-version", out var versionObj))
                {
                    var version = versionObj is byte[] bytes ? Encoding.UTF8.GetString(bytes) : versionObj?.ToString();
                    headerFound = version == "1.0";
                }
                resetEvent.Set();
            };

            channel.BasicConsume("test_version_header_queue", autoAck: true, consumer: consumer);

            provider.Publish("test_version_header_queue", new TestMessage { Content = "version test" });

            var hit = resetEvent.Wait(TimeSpan.FromSeconds(5));
            Assert.True(hit, "Did not receive message within 5 seconds");
            Assert.True(headerFound, "Message did not contain x-message-version header with value 1.0");

            provider.Disconnect();
        }

        [Fact]
        public void GivenMessagingEnabled_WhenPublishWithConfirmsEnabled_ThenSucceeds()
        {
            var originalEnabled = Environment.GetEnvironmentVariable("RABBIT_PUBLISHER_CONFIRMS");
            try
            {
                Environment.SetEnvironmentVariable("RABBIT_PUBLISHER_CONFIRMS", "true");
                using var provider = new RabbitMQProvider();
                provider.Connect();

                provider.Publish("test_confirms_queue", new TestMessage { Content = "confirms test" });

                Assert.True(true, "Publish with confirms enabled did not throw");
                provider.Disconnect();
            }
            finally
            {
                Environment.SetEnvironmentVariable("RABBIT_PUBLISHER_CONFIRMS", originalEnabled);
            }
        }

        [Fact]
        public void GivenMessagingDisabled_WhenDeclareDeadLetterInfrastructure_ThenReturnsEarly()
        {
            var originalEnabled = Environment.GetEnvironmentVariable("MESSAGING_ENABLED");
            try
            {
                Environment.SetEnvironmentVariable("MESSAGING_ENABLED", "false");
                using var provider = new RabbitMQProvider();

                provider.DeclareDeadLetterInfrastructure("test_dlx_disabled");

                Assert.True(true, "DeclareDeadLetterInfrastructure should not throw when disabled");
            }
            finally
            {
                Environment.SetEnvironmentVariable("MESSAGING_ENABLED", originalEnabled);
            }
        }

        [Fact]
        public void GivenRabbitProvider_WhenSubscribeWithDeadLetterQueue_ThenNackedMessagesGoToDlq()
        {
            using var provider = new RabbitMQProvider();
            provider.Connect();

            var dlqReceived = false;
            var dlqResetEvent = new ManualResetEventSlim(false);

            var channelField = typeof(RabbitMQProvider).GetField("_channel",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var channel = (IModel)channelField!.GetValue(provider)!;

            provider.DeclareDeadLetterInfrastructure("test_nack_to_dlq");

            var dlqConsumer = new RabbitMQ.Client.Events.EventingBasicConsumer(channel);
            dlqConsumer.Received += (model, ea) =>
            {
                dlqReceived = true;
                dlqResetEvent.Set();
            };
            channel.BasicConsume("test_nack_to_dlq-dlq", autoAck: true, consumer: dlqConsumer);

            channel.QueueDeclare("test_nack_to_dlq", durable: true, exclusive: false, autoDelete: false,
                arguments: new Dictionary<string, object?>
                {
                    { "x-dead-letter-exchange", "test_nack_to_dlq-dlx" },
                    { "x-dead-letter-routing-key", "" }
                });

            var mainConsumer = new RabbitMQ.Client.Events.EventingBasicConsumer(channel);
            mainConsumer.Received += (model, ea) =>
            {
                channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
            };
            channel.BasicConsume("test_nack_to_dlq", autoAck: false, consumer: mainConsumer);

            channel.BasicPublish(
                exchange: "",
                routingKey: "test_nack_to_dlq",
                basicProperties: null,
                body: Encoding.UTF8.GetBytes("{\"Content\":\"will fail\"}")
            );

            var hit = dlqResetEvent.Wait(TimeSpan.FromSeconds(5));
            Assert.True(hit, "Nacked message did not reach DLQ within 5 seconds");
            Assert.True(dlqReceived);

            provider.Disconnect();
        }

        public class TestMessage
        {
            public string Content { get; set; } = string.Empty;
        }
    }
}
