using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MageBackend.Database;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MageBackend.Infrastructure.Auditing
{
    /*
     * BackgroundService que consome AuditLogEntry da fila e grava em batch
     * no SQL. Substitui o Task.Run detached do middleware antigo, ganhando:
     *
     *  - Concorrência única e controlada (1 writer por instância) — sem flood
     *    no ThreadPool quando o DB lenteia.
     *  - Batching natural: drena até AUDIT_BATCH_SIZE (default 50) itens em
     *    uma única transação por flush, reduzindo round-trips.
     *  - Graceful shutdown: ao receber stoppingToken, completa o writer e
     *    drena os itens restantes antes de sair.
     *  - Resiliência: falha de escrita não derruba o loop; itens problemáticos
     *    são logados e descartados (best-effort, padrão herdado do middleware).
     */
    public sealed class AuditLogBackgroundService : BackgroundService
    {
        private const int DefaultBatchSize = 50;
        private const string BatchSizeEnvVar = "AUDIT_BATCH_SIZE";

        private readonly IAuditLogQueue _queue;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly int _batchSize;

        public AuditLogBackgroundService(IAuditLogQueue queue, IServiceScopeFactory scopeFactory)
        {
            _queue = queue;
            _scopeFactory = scopeFactory;
            _batchSize = ReadEnvInt(BatchSizeEnvVar, DefaultBatchSize);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Log.Information("[Audit] BackgroundService started (batchSize={Size})", _batchSize);

            var batch = new List<AuditLogEntry>(_batchSize);

            try
            {
                while (await _queue.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
                {
                    DrainAvailable(batch);
                    await FlushAsync(batch).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                /* Shutdown solicitado — drena o que sobrou e sai limpo. */
            }

            DrainAvailable(batch);
            await FlushAsync(batch).ConfigureAwait(false);

            Log.Information("[Audit] BackgroundService stopped");
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            /* Completa o writer para destravar o WaitToReadAsync e permitir drain final. */
            _queue.Complete();
            await base.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        internal void DrainAvailable(List<AuditLogEntry> batch)
        {
            while (batch.Count < _batchSize && _queue.TryDequeue(out var entry) && entry is not null)
            {
                batch.Add(entry);
            }
        }

        internal async Task FlushAsync(List<AuditLogEntry> batch)
        {
            if (batch.Count == 0)
            {
                return;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                foreach (var entry in batch)
                {
                    dbContext.Audit.Add(ToAudit(entry));
                }

                await dbContext.SaveChangesAsync().ConfigureAwait(false);
            }
#pragma warning disable S2221
            /*
             * Catch genérico justificado: o pipeline de auditoria é best-effort.
             * Qualquer falha (DB indisponível, deadlock, payload inesperado) deve
             * ser logada mas não pode derrubar o BackgroundService, sob pena de
             * derrubar toda a auditoria do processo até o próximo restart.
             */
            catch (Exception ex)
            {
                Log.Error(ex, "[Audit] Failed to flush batch of {Count} entries", batch.Count);
            }
#pragma warning restore S2221
            finally
            {
                batch.Clear();
            }
        }

        internal static Audit ToAudit(AuditLogEntry entry)
        {
            var diffValue = !string.IsNullOrEmpty(entry.ResponseBody)
                ? entry.ResponseBody
                : JsonSerializer.Serialize(new { statusCode = entry.StatusCode });

            return new Audit
            {
                IdUser = entry.IdUser,
                UserName = entry.UserName,
                ActionType = "HTTP_REQUEST",
                ExecuteType = entry.Method,
                Method = entry.Method,
                Class = entry.TableName,
                Function = entry.Path,
                Params = entry.Params,
                Raw = entry.Params,
                TableName = entry.TableName,
                DiffValue = diffValue,
                Host = entry.Host,
                Ip = entry.Ip,
                BaseUrl = entry.Path,
                Hostname = entry.Hostname,
                OriginalUrl = entry.Path,
                CreatedAt = entry.CreatedAt
            };
        }

        private static int ReadEnvInt(string name, int defaultValue)
        {
            var raw = Environment.GetEnvironmentVariable(name);
            return int.TryParse(raw, out var parsed) && parsed > 0 ? parsed : defaultValue;
        }
    }
}
