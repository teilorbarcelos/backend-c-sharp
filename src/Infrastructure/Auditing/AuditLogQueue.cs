using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Serilog;

namespace MageBackend.Infrastructure.Auditing
{
    /*
     * Implementação singleton de IAuditLogQueue baseada em System.Threading.Channels.
     *
     * Capacidade configurável via env AUDIT_QUEUE_CAPACITY (default 10000).
     * Modo de overflow: DropOldest — em pico de tráfego, descarta o item
     * mais antigo da fila ao invés de bloquear ou matar a request. O writer
     * é não-bloqueante (TryWrite) para que o middleware nunca segure a thread
     * que está respondendo ao cliente HTTP.
     */
    public sealed class AuditLogQueue : IAuditLogQueue
    {
        private const int DefaultCapacity = 10000;
        private const string CapacityEnvVar = "AUDIT_QUEUE_CAPACITY";

        private readonly Channel<AuditLogEntry> _channel;
        private long _dropped;

        public AuditLogQueue()
        {
            var capacity = ReadCapacity();
            _channel = Channel.CreateBounded<AuditLogEntry>(new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
        }

        public int Count => _channel.Reader.Count;

        public long DroppedCount => Interlocked.Read(ref _dropped);

        public bool TryEnqueue(AuditLogEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);

            if (!_channel.Writer.TryWrite(entry))
            {
                Interlocked.Increment(ref _dropped);
                Log.Warning("[Audit] Queue closed; dropping entry for {Method} {Path}", entry.Method, entry.Path);
                return false;
            }

            return true;
        }

        public ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken)
        {
            return _channel.Reader.WaitToReadAsync(cancellationToken);
        }

        public bool TryDequeue(out AuditLogEntry? entry)
        {
            return _channel.Reader.TryRead(out entry);
        }

        public void Complete()
        {
            _channel.Writer.TryComplete();
        }

        private static int ReadCapacity()
        {
            var raw = Environment.GetEnvironmentVariable(CapacityEnvVar);
            return int.TryParse(raw, out var parsed) && parsed > 0 ? parsed : DefaultCapacity;
        }
    }
}
