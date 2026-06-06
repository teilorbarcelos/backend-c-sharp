using System.Threading;
using System.Threading.Tasks;

namespace MageBackend.Infrastructure.Messaging
{
    public interface IRabbitMQMessageHandler<in T>
    {
        Task HandleAsync(T message, CancellationToken cancellationToken);
    }
}
