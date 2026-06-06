using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Serilog;
using Microsoft.EntityFrameworkCore;
using MageBackend.Database;

namespace MageBackend.Infrastructure.Auth
{
    public static class RedisProvider
    {
        private static Lazy<ConnectionMultiplexer>? _lazyConnection;

        public static void Initialize(string connectionString)
        {
            if (connectionString.StartsWith("redis://"))
            {
                connectionString = connectionString.Substring(8);
            }

            _lazyConnection = new Lazy<ConnectionMultiplexer>(() =>
            {
                var config = ConfigurationOptions.Parse(connectionString);
                config.AbortOnConnectFail = false;
                return ConnectionMultiplexer.Connect(config);
            });
        }

        public static ConnectionMultiplexer Connection
        {
            get
            {
                if (_lazyConnection == null)
                    throw new InvalidOperationException("RedisProvider is not initialized.");
                return _lazyConnection.Value;
            }
        }

        public static IDatabase Database => Connection.GetDatabase();
    }

    public static class SessionManager
    {
        private static readonly TimeSpan SessionVersionTtl = TimeSpan.FromDays(7);

        /*
         * Lê a SessionVersion atual de um user com cache-aside.
         *
         * É a fonte única de verdade para o versionamento de sessão, usada
         * tanto pelo TokenSessionValidationMiddleware (checagem do JWT) quanto
         * pelo RefreshTokenHandler (defesa em profundidade no /v1/auth/refresh).
         *
         * Fluxo:
         *   1. Lê session:user:{id}:version do Redis (O(1))
         *   2. Cache hit → retorna versão cacheada
         *   3. Cache miss → hidrata do DB (Auth.SessionVersion) e reescreve no
         *      Redis com TTL de 7d. Isso permite que o sistema sobreviva a um
         *      wipe do Redis (fail-secure + auto-recovery).
         *
         * Retorna null se o user não tem Auth (sessão inválida).
         */
        public static async Task<int?> GetCurrentVersionAsync(
            string userId,
            ApplicationDbContext dbContext,
            IDatabase? redisDb = null)
        {
            redisDb ??= RedisProvider.Database;
            var sessionKey = $"session:user:{userId}:version";

            var redisValue = await redisDb.StringGetAsync(sessionKey);
            if (redisValue.HasValue && int.TryParse(redisValue.ToString(), out var cached))
            {
                return cached;
            }

            return await HydrateFromDatabaseAsync(userId, dbContext, redisDb, sessionKey);
        }

        private static async Task<int?> HydrateFromDatabaseAsync(
            string userId,
            ApplicationDbContext dbContext,
            IDatabase redisDb,
            string sessionKey)
        {
            var user = await dbContext.User
                .AsNoTracking()
                .Include(u => u.Auth)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user?.Auth == null) return null;

            var version = user.Auth.SessionVersion;
            await redisDb.StringSetAsync(sessionKey, version.ToString(), SessionVersionTtl);
            return version;
        }

        public static async Task<int> InvalidateUserSessionsAsync(string userId, ApplicationDbContext context)
        {
            Log.Information("[SessionManager] Invalidating sessions for user {UserId}", userId);

            var idAuth = await context.User
                .Where(u => u.Id == userId)
                .Select(u => u.IdAuth)
                .FirstOrDefaultAsync();

            if (string.IsNullOrEmpty(idAuth))
            {
                Log.Warning("[SessionManager] No auth record found for user {UserId}", userId);
                return 0;
            }

            var rows = await context.Auth
                .Where(a => a.Id == idAuth)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.SessionVersion, a => a.SessionVersion + 1));

            if (rows == 0)
            {
                Log.Warning("[SessionManager] Auth update affected 0 rows for user {UserId}", userId);
                return 0;
            }

            var currentVersion = await context.Auth
                .Where(a => a.Id == idAuth)
                .Select(a => a.SessionVersion)
                .FirstAsync();

            var redisDb = RedisProvider.Database;
            await redisDb.StringSetAsync($"session:user:{userId}:version", currentVersion.ToString(), SessionVersionTtl);

            /*
             * Bug fix: bump só do SessionVersion não basta. O /v1/auth/refresh
             * está nos public paths do TokenSessionValidationMiddleware
             * (bypass do sv check), e o RefreshTokenHandler valida
             * apenas KeyExistsAsync(session:user:{id}:refresh:{hash}).
             * Se a chave continuar viva, o user consegue refresh imediatamente
             * após a invalidação. Apaga TODAS as chaves de refresh do user.
             */
            var deletedRefreshKeys = await DeleteRefreshTokensAsync(userId, redisDb);

            Log.Information(
                "[SessionManager] Incremented session version for user {UserId} to {Version} (deleted {RefreshCount} refresh keys)",
                userId, currentVersion, deletedRefreshKeys);
            return currentVersion;
        }

        public static async Task InvalidateManyUsersSessionsAsync(IEnumerable<string> userIds, ApplicationDbContext context)
        {
            Log.Information("[SessionManager] Invalidating sessions for multiple users");

            var idList = new List<string>(userIds);
            if (idList.Count == 0) return;

            var userAuths = await context.User
                .Where(u => idList.Contains(u.Id) && u.IdAuth != null)
                .Select(u => new { u.Id, IdAuth = u.IdAuth! })
                .ToListAsync();

            if (userAuths.Count == 0) return;

            var authIds = userAuths.Select(ua => ua.IdAuth).Distinct().ToList();

            await context.Auth
                .Where(a => authIds.Contains(a.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.SessionVersion, a => a.SessionVersion + 1));

            var updatedAuths = await context.Auth
                .Where(a => authIds.Contains(a.Id))
                .Select(a => new { a.Id, a.SessionVersion })
                .ToListAsync();

            var userIdByAuth = userAuths.ToDictionary(ua => ua.IdAuth, ua => ua.Id);
            var redisDb = RedisProvider.Database;
            var batch = redisDb.CreateBatch();
            var tasks = new List<Task>(updatedAuths.Count);
            var totalDeletedRefreshKeys = 0L;

            foreach (var auth in updatedAuths)
            {
                if (userIdByAuth.TryGetValue(auth.Id, out var userId))
                {
                    tasks.Add(batch.StringSetAsync($"session:user:{userId}:version", auth.SessionVersion.ToString(), SessionVersionTtl));
                }
            }
            batch.Execute();
            await Task.WhenAll(tasks);

            /*
             * Mesmo bug do InvalidateUserSessionsAsync: precisa invalidar
             * também as refresh keys de cada user do batch. Roda após o
             * batch de versões pra minimizar o gap em que a versão está
             * bumpada mas os refresh tokens ainda existem.
             */
            foreach (var userId in userAuths.Select(ua => ua.Id))
            {
                totalDeletedRefreshKeys += await DeleteRefreshTokensAsync(userId, redisDb);
            }

            Log.Information(
                "[SessionManager] Invalidated sessions for {Count} users (deleted {RefreshCount} refresh keys)",
                userAuths.Count, totalDeletedRefreshKeys);
        }

        /*
         * Apaga todas as chaves session:user:{userId}:refresh:* no Redis.
         *
         * Implementação multi-shard safe: Itera todos os endpoints do
         * ConnectionMultiplexer (em cluster, cada shard tem seu próprio
         * endpoint). Em single-node, só itera um endpoint.
         *
         * Usa IServer.Keys(pattern) que internamente usa SCAN (não KEYS,
         * que é O(N) bloqueante). Filtra replicas para não tentar scan
         * em nós read-only.
         */
        private static async Task<long> DeleteRefreshTokensAsync(string userId, IDatabase redisDb)
        {
            var multiplexer = redisDb.Multiplexer;
            var pattern = $"session:user:{userId}:refresh:*";
            var database = redisDb.Database;
            var keysToDelete = new List<RedisKey>();

            foreach (var endpoint in multiplexer.GetEndPoints())
            {
                var server = multiplexer.GetServer(endpoint);
                if (!server.IsConnected || server.IsReplica) continue;

                var materialized = server.Keys(database: database, pattern: pattern, pageSize: 1000).ToList();
                keysToDelete.AddRange(materialized);
            }

            if (keysToDelete.Count == 0) return 0;

            return await redisDb.KeyDeleteAsync(keysToDelete.ToArray());
        }
    }
}
