using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MageBackend.Infrastructure.Auth
{
    public class RedisProvider
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

    public class SessionManager
    {
        public static async Task InvalidateUserSessionsAsync(string userId)
        {
            var pattern = $"session:user:{userId}:*";
            Console.WriteLine($"[SessionManager] Invalidating sessions for user {userId} with pattern {pattern}");

            var endpoints = RedisProvider.Connection.GetEndPoints();
            var database = RedisProvider.Database;

            foreach (var endpoint in endpoints)
            {
                var server = RedisProvider.Connection.GetServer(endpoint);
                var keys = new List<RedisKey>();

                await foreach (var key in server.KeysAsync(pattern: pattern, pageSize: 100))
                {
                    keys.Add(key);
                }

                if (keys.Count > 0)
                {
                    await database.KeyDeleteAsync(keys.ToArray());
                    Console.WriteLine($"[SessionManager] Deleted {keys.Count} session keys for user {userId}");
                }
            }
        }

        public static async Task InvalidateManyUsersSessionsAsync(IEnumerable<string> userIds)
        {
            Console.WriteLine("[SessionManager] Invalidating sessions for multiple users");
            foreach (var userId in userIds)
            {
                await InvalidateUserSessionsAsync(userId);
            }
        }
    }
}
