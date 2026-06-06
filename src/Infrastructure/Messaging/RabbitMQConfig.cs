using System;
using System.Collections.Generic;

namespace MageBackend.Infrastructure.Messaging
{
    public static class RabbitMQConfig
    {
        public const string DeadLetterExchangeSuffix = "-dlx";
        public const string DeadLetterQueueSuffix = "-dlq";
        public const string RetryExchangeSuffix = "-retry";
        public const string RetryQueueSuffix = "-retry";
        public const string DefaultMessageVersion = "1.0";
        public const int DefaultPrefetchCount = 16;
        public const string PrefetchCountEnvVar = "RABBIT_PREFETCH_COUNT";
        public const string PublisherConfirmsEnvVar = "RABBIT_PUBLISHER_CONFIRMS";

        public static int GetPrefetchCount()
        {
            var raw = Environment.GetEnvironmentVariable(PrefetchCountEnvVar);
            return int.TryParse(raw, out var parsed) && parsed > 0 ? parsed : DefaultPrefetchCount;
        }

        public static bool IsPublisherConfirmsEnabled()
        {
            var raw = Environment.GetEnvironmentVariable(PublisherConfirmsEnvVar);
            return raw is not null && (raw.Equals("true", StringComparison.OrdinalIgnoreCase) || raw == "1");
        }

        public static Dictionary<string, object?> GetDeadLetterArgs(string exchangeName)
        {
            return new Dictionary<string, object?>
            {
                { "x-dead-letter-exchange", exchangeName + DeadLetterExchangeSuffix },
                { "x-dead-letter-routing-key", "" }
            };
        }
    }
}
