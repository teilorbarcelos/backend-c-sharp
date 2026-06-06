using System;
using System.Collections.Generic;
using MageBackend.Infrastructure.Messaging;
using Xunit;

namespace MageBackend.Tests
{
    public class RabbitMQConfigTests : IDisposable
    {
        private readonly Dictionary<string, string?> _originalEnv = new();

        public RabbitMQConfigTests()
        {
            Capture("RABBIT_PREFETCH_COUNT");
            Capture("RABBIT_PUBLISHER_CONFIRMS");
        }

        public void Dispose()
        {
            foreach (var kv in _originalEnv)
            {
                Environment.SetEnvironmentVariable(kv.Key, kv.Value);
            }
            GC.SuppressFinalize(this);
        }

        private void Capture(string name)
        {
            _originalEnv[name] = Environment.GetEnvironmentVariable(name);
        }

        [Fact]
        public void GivenNoEnvVar_WhenGetPrefetchCount_ThenReturnsDefault()
        {
            Environment.SetEnvironmentVariable("RABBIT_PREFETCH_COUNT", null);
            Assert.Equal(16, RabbitMQConfig.GetPrefetchCount());
        }

        [Fact]
        public void GivenCustomEnvVar_WhenGetPrefetchCount_ThenReturnsCustomValue()
        {
            Environment.SetEnvironmentVariable("RABBIT_PREFETCH_COUNT", "32");
            Assert.Equal(32, RabbitMQConfig.GetPrefetchCount());
        }

        [Fact]
        public void GivenInvalidEnvVar_WhenGetPrefetchCount_ThenReturnsDefault()
        {
            Environment.SetEnvironmentVariable("RABBIT_PREFETCH_COUNT", "not-a-number");
            Assert.Equal(16, RabbitMQConfig.GetPrefetchCount());
        }

        [Fact]
        public void GivenNegativeEnvVar_WhenGetPrefetchCount_ThenReturnsDefault()
        {
            Environment.SetEnvironmentVariable("RABBIT_PREFETCH_COUNT", "-5");
            Assert.Equal(16, RabbitMQConfig.GetPrefetchCount());
        }

        [Fact]
        public void GivenNoEnvVar_WhenIsPublisherConfirmsEnabled_ThenReturnsFalse()
        {
            Environment.SetEnvironmentVariable("RABBIT_PUBLISHER_CONFIRMS", null);
            Assert.False(RabbitMQConfig.IsPublisherConfirmsEnabled());
        }

        [Fact]
        public void GivenEnabledEnvVar_WhenIsPublisherConfirmsEnabled_ThenReturnsTrue()
        {
            Environment.SetEnvironmentVariable("RABBIT_PUBLISHER_CONFIRMS", "true");
            Assert.True(RabbitMQConfig.IsPublisherConfirmsEnabled());
        }

        [Fact]
        public void GivenDisabledEnvVar_WhenIsPublisherConfirmsEnabled_ThenReturnsFalse()
        {
            Environment.SetEnvironmentVariable("RABBIT_PUBLISHER_CONFIRMS", "false");
            Assert.False(RabbitMQConfig.IsPublisherConfirmsEnabled());
        }

        [Fact]
        public void GivenQueueName_WhenGetDeadLetterArgs_ThenContainsDlxExchange()
        {
            var args = RabbitMQConfig.GetDeadLetterArgs("my-queue");

            Assert.NotNull(args);
            Assert.True(args.ContainsKey("x-dead-letter-exchange"));
            Assert.Equal("my-queue-dlx", args["x-dead-letter-exchange"]);
            Assert.Equal("", args["x-dead-letter-routing-key"]);
        }

        [Fact]
        public void GivenConstants_WhenUsed_ThenValuesAreCorrect()
        {
            Assert.Equal("-dlx", RabbitMQConfig.DeadLetterExchangeSuffix);
            Assert.Equal("-dlq", RabbitMQConfig.DeadLetterQueueSuffix);
            Assert.Equal("-retry", RabbitMQConfig.RetryExchangeSuffix);
            Assert.Equal("-retry", RabbitMQConfig.RetryQueueSuffix);
            Assert.Equal("1.0", RabbitMQConfig.DefaultMessageVersion);
            Assert.Equal(16, RabbitMQConfig.DefaultPrefetchCount);
        }
    }
}
