using System;
using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace MageBackend.Infrastructure.Messaging
{
    public class RabbitMQProvider : IDisposable
    {
        private IConnection? _connection;
        private IModel? _channel;
        private readonly bool _enabled;
        private readonly string _rabbitUrl;

        public RabbitMQProvider()
        {
            var enabledEnv = Environment.GetEnvironmentVariable("MESSAGING_ENABLED");
            _enabled = !string.IsNullOrEmpty(enabledEnv) && (enabledEnv.Equals("true", StringComparison.OrdinalIgnoreCase) || enabledEnv == "1");
            _rabbitUrl = Environment.GetEnvironmentVariable("RABBIT_URL") ?? "amqp://localhost";
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
                Console.WriteLine("[RabbitMQ] Connected successfully");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[RabbitMQ] Connection failed: {ex.Message}");
                throw;
            }
        }

        public void Publish<T>(string queue, T message)
        {
            if (_channel == null)
            {
                if (_enabled)
                {
                    throw new InvalidOperationException("RabbitMQ channel not initialized");
                }
                return;
            }

            _channel.QueueDeclare(
                queue: queue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null
            );

            var json = JsonSerializer.Serialize(message);
            var body = Encoding.UTF8.GetBytes(json);

            var properties = _channel.CreateBasicProperties();
            properties.Persistent = true;

            _channel.BasicPublish(
                exchange: string.Empty,
                routingKey: queue,
                basicProperties: properties,
                body: body
            );
        }

        public void Subscribe<T>(string queue, Action<T> callback)
        {
            if (_channel == null)
            {
                if (_enabled)
                {
                    throw new InvalidOperationException("RabbitMQ channel not initialized");
                }
                return;
            }

            _channel.QueueDeclare(
                queue: queue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null
            );

            var consumer = new EventingBasicConsumer(_channel);
            consumer.Received += (model, ea) =>
            {
                var body = ea.Body.ToArray();
                if (body == null || body.Length == 0)
                {
                    return;
                }

                try
                {
                    var json = Encoding.UTF8.GetString(body);
                    var message = JsonSerializer.Deserialize<T>(json);

                    if (message != null)
                    {
                        callback(message);
                        _channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[RabbitMQ] Error handling message: {ex.Message}");
                }
            };

            _channel.BasicConsume(
                queue: queue,
                autoAck: false,
                consumer: consumer
            );
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
                Console.WriteLine($"[RabbitMQ] Error disconnecting: {ex.Message}");
            }
        }

        public void Dispose()
        {
            Disconnect();
            _channel?.Dispose();
            _connection?.Dispose();
        }
    }
}
