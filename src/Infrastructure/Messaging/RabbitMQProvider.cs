using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Serilog;

namespace MageBackend.Infrastructure.Messaging
{
    public class RabbitMQProvider : IDisposable
    {
        private IConnection? _connection;
        private IModel? _channel;
        private readonly bool _enabled;
        private readonly string _rabbitUrl;
        private static readonly string DefaultRabbitUrl = new UriBuilder("amqp", "localhost").ToString();

        public bool IsConnected => _channel is not null && _channel.IsOpen;

        public RabbitMQProvider()
        {
            var enabledEnv = Environment.GetEnvironmentVariable("MESSAGING_ENABLED");
            _enabled = !string.IsNullOrEmpty(enabledEnv) && (enabledEnv.Equals("true", StringComparison.OrdinalIgnoreCase) || enabledEnv == "1");
            _rabbitUrl = Environment.GetEnvironmentVariable("RABBIT_URL") ?? DefaultRabbitUrl;
        }

        public void Connect()
        {
            if (!_enabled)
            {
                return;
            }

            try
            {
                var factory = new ConnectionFactory
                {
                    Uri = new Uri(_rabbitUrl)
                };

                _connection = factory.CreateConnection();
                _channel = _connection.CreateModel();

                var prefetchCount = RabbitMQConfig.GetPrefetchCount();
                _channel.BasicQos(prefetchSize: 0, prefetchCount: (ushort)prefetchCount, global: false);
                Log.Information("[RabbitMQ] Connected successfully (prefetchCount={Prefetch})", prefetchCount);

                if (RabbitMQConfig.IsPublisherConfirmsEnabled())
                {
                    _channel.ConfirmSelect();
                    Log.Information("[RabbitMQ] Publisher confirms enabled");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[RabbitMQ] Connection failed: {Message}", ex.Message);
                throw new InvalidOperationException("Falha na inicialização do RabbitMQ", ex);
            }
        }

        public void DeclareDeadLetterInfrastructure(string queueName)
        {
            if (_channel == null)
            {
                if (_enabled)
                    throw new InvalidOperationException("RabbitMQ channel not initialized");
                return;
            }

            var dlxName = queueName + RabbitMQConfig.DeadLetterExchangeSuffix;
            var dlqName = queueName + RabbitMQConfig.DeadLetterQueueSuffix;
            var retryExchangeName = queueName + RabbitMQConfig.RetryExchangeSuffix;
            var retryQueueName = queueName + RabbitMQConfig.RetryQueueSuffix;

            _channel.ExchangeDeclare(dlxName, ExchangeType.Fanout, durable: true);
            _channel.QueueDeclare(dlqName, durable: true, exclusive: false, autoDelete: false, arguments: null);
            _channel.QueueBind(dlqName, dlxName, routingKey: "");

            var retryArgs = new Dictionary<string, object?>
            {
                { "x-dead-letter-exchange", "" },
                { "x-dead-letter-routing-key", queueName },
                { "x-message-ttl", 5000 }
            };
            _channel.ExchangeDeclare(retryExchangeName, ExchangeType.Fanout, durable: true);
            _channel.QueueDeclare(retryQueueName, durable: true, exclusive: false, autoDelete: false, arguments: retryArgs);
            _channel.QueueBind(retryQueueName, retryExchangeName, routingKey: "");

            Log.Information("[RabbitMQ] DLX/DLQ infrastructure declared for queue '{Queue}'", queueName);
        }

        public void Publish<T>(string queue, T message, bool addVersionHeader = true)
        {
            if (_channel == null)
            {
                if (_enabled)
                {
                    throw new InvalidOperationException("RabbitMQ channel not initialized");
                }
                Log.Warning("[RabbitMQ] Publish ignored: messaging is disabled.");
                return;
            }

            DeclareQueue(queue);

            var json = JsonSerializer.Serialize(message);
            var body = Encoding.UTF8.GetBytes(json);

            var properties = _channel.CreateBasicProperties();
            properties.Persistent = true;

            if (addVersionHeader)
            {
                properties.Headers ??= new Dictionary<string, object?>();
                properties.Headers["x-message-version"] = RabbitMQConfig.DefaultMessageVersion;
            }

            _channel.BasicPublish(
                exchange: string.Empty,
                routingKey: queue,
                basicProperties: properties,
                body: body
            );

            if (RabbitMQConfig.IsPublisherConfirmsEnabled())
            {
                _channel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(5));
            }
        }

        public void Subscribe<T>(string queue, Action<T> callback) where T : class
        {
            if (_channel == null)
            {
                if (_enabled)
                {
                    throw new InvalidOperationException("RabbitMQ channel not initialized");
                }
                Log.Warning("[RabbitMQ] Subscribe ignored: messaging is disabled.");
                return;
            }

            DeclareQueue(queue);

            var consumer = new EventingBasicConsumer(_channel);
            consumer.Received += (model, ea) => HandleMessageReceived(ea, callback);

            _channel.BasicConsume(
                queue: queue,
                autoAck: false,
                consumer: consumer
            );
        }

        private void DeclareQueue(string queue)
        {
            if (_channel == null) return;

            _channel.QueueDeclare(
                queue: queue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null
            );
        }

        private void HandleMessageReceived<T>(BasicDeliverEventArgs ea, Action<T> callback) where T : class
        {
            var body = ea.Body.ToArray();
            if (body == null || body.Length == 0)
            {
                _channel?.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
                return;
            }

            try
            {
                var json = Encoding.UTF8.GetString(body);
                var message = JsonSerializer.Deserialize<T>(json);

                if (message != null)
                {
                    callback(message);
                    _channel?.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
                }
                else
                {
                    _channel?.BasicNack(deliveryTag: ea.DeliveryTag, multiple: false, requeue: false);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[RabbitMQ] Error handling message: {Message}", ex.Message);
                _channel?.BasicNack(deliveryTag: ea.DeliveryTag, multiple: false, requeue: false);
            }
        }

        public void Disconnect()
        {
            try
            {
                _channel?.Close();
                _connection?.Close();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[RabbitMQ] Error disconnecting: {Message}", ex.Message);
            }
        }

        private bool _disposed;

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    Disconnect();
                    _channel?.Dispose();
                    _connection?.Dispose();
                }
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
