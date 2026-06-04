using System.Threading;
using System.Threading.Tasks;

namespace MageBackend.Core.Auditing
{
    /*
     * Fila singleton de logs de auditoria com backpressure.
     *
     * Substitui o padrão "Task.Run detached" do AuditLogMiddleware antigo:
     *  - Não estoura o ThreadPool quando o DB ficar lento (cada request virava
     *    uma Task.Run desconhecida pelo runtime).
     *  - Permite drain em batch + graceful shutdown via BackgroundService.
     *  - Aplica DropOldest quando a fila estoura, evitando OOM em pico de tráfego.
     */
    public interface IAuditLogQueue
    {
        /*
         * Tenta enfileirar de forma síncrona (não bloqueia a request HTTP).
         * Retorna false quando o writer já foi completado (durante shutdown).
         * Em modo DropOldest, retorna true mesmo se a fila estiver cheia
         * (descarta silenciosamente o item mais antigo).
         */
        bool TryEnqueue(AuditLogEntry entry);

        /*
         * Aguarda até que haja ao menos 1 item disponível, ou até o writer ser
         * completado (retorna false). Usado pelo BackgroundService para evitar
         * busy-loop.
         */
        ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken);

        /*
         * Tenta retirar um item sem bloquear. Retorna false quando a fila
         * está vazia.
         */
        bool TryDequeue(out AuditLogEntry? entry);

        /*
         * Encerra o writer; permite ao consumer drenar itens restantes e sair
         * limpo. Idempotente.
         */
        void Complete();

        /*
         * Tamanho atual da fila (somente para observabilidade/teste).
         */
        int Count { get; }
    }
}
