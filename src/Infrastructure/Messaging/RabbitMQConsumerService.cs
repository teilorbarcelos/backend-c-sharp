using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Serilog;

namespace MageBackend.Infrastructure.Messaging
{
    public sealed class RabbitMQConsumerService : BackgroundService
    {
        private readonly RabbitMQProvider _provider;
        private readonly string _queueName;
        private IModel? _channel;

        public RabbitMQConsumerService(RabbitMQProvider provider, string queueName)
        {
            _provider = provider;
            _queueName = queueName;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (string.IsNullOrEmpty(_queueName))
            {
                Log.Warning("[RabbitMQ Consumer] No queue configured, skipping consumer");
                return;
            }

            if (!_provider.IsConnected)
            {
                Log.Warning("[RabbitMQ Consumer] Provider not connected, skipping consumer for '{Queue}'", _queueName);
                return;
            }

            Log.Information("[RabbitMQ Consumer] Starting consumer for queue '{Queue}'", _queueName);

            try
            {
                /*
                 * Reflection to access _channel is intentional:
                 * the consumer needs the same channel as the provider
                 * to share the consumer tag and lifecycle.
                 */
#pragma warning disable S3011
                var channelField = typeof(RabbitMQProvider).GetField("_channel",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
#pragma warning restore S3011

                if (channelField?.GetValue(_provider) is IModel providerChannel)
                {
                    _channel = providerChannel;

                    providerChannel.QueueDeclare(
                        queue: _queueName,
                        durable: true,
                        exclusive: false,
                        autoDelete: false,
                        arguments: null
                    );

                    var consumer = new EventingBasicConsumer(providerChannel);
                    consumer.Received += OnMessageReceived;

                    providerChannel.BasicConsume(
                        queue: _queueName,
                        autoAck: false,
                        consumer: consumer
                    );

                    Log.Information("[RabbitMQ Consumer] Subscribed to queue '{Queue}'", _queueName);
                }

                await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex)
            {
                Log.Information(ex, "[RabbitMQ Consumer] Stopped for queue '{Queue}'", _queueName);
            }
        }

        private void OnMessageReceived(object? sender, BasicDeliverEventArgs ea)
        {
            var body = ea.Body.ToArray();
            if (body is null || body.Length == 0)
            {
                _channel?.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
                return;
            }

            try
            {
                var json = Encoding.UTF8.GetString(body);

                if (ea.BasicProperties?.Headers is not null &&
                    ea.BasicProperties.Headers.TryGetValue("x-message-version", out var versionObj))
                {
                    var version = versionObj is byte[] bytes ? Encoding.UTF8.GetString(bytes) : versionObj?.ToString();
                    Log.Debug("[RabbitMQ Consumer] Received message version={Version} on queue '{Queue}'", version, _queueName);
                }

                Log.Information("[RabbitMQ Consumer] Processing message on queue '{Queue}': {Body}",
                    _queueName, json.Length > 200 ? json[..200] + "..." : json);

                _channel?.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[RabbitMQ Consumer] Error processing message on queue '{Queue}'", _queueName);
                _channel?.BasicNack(deliveryTag: ea.DeliveryTag, multiple: false, requeue: false);
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            Log.Information("[RabbitMQ Consumer] Stopping consumer for queue '{Queue}'", _queueName);
            await base.StopAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
