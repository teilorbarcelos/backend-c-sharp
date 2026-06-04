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
            Log.Information("[SessionManager] Incremented session version for user {UserId} to {Version}", userId, currentVersion);
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
            foreach (var auth in updatedAuths)
            {
                if (userIdByAuth.TryGetValue(auth.Id, out var userId))
                {
                    tasks.Add(batch.StringSetAsync($"session:user:{userId}:version", auth.SessionVersion.ToString(), SessionVersionTtl));
                }
            }
            batch.Execute();
            await Task.WhenAll(tasks);

            Log.Information("[SessionManager] Invalidated sessions for {Count} users", userAuths.Count);
        }
    }
}
