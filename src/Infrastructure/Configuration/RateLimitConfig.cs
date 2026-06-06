using System;
using System.Collections.Generic;

namespace MageBackend.Infrastructure.Configuration
{
    /*
     * Configuração centralizada de rate limiting por endpoint.
     *
     * Single source of truth: todos os limites estão aqui. Para ajustar
     * qualquer limite, edite este arquivo e recompile — não há magic
     * numbers espalhados pelo middleware.
     *
     * O default global (RATE_LIMIT_MAX/RATE_LIMIT_WINDOW_SECONDS) ainda é
     * configurável via env var para ops ajustarem em runtime, mas overrides
     * por endpoint são hard-coded por design — limite de 5 req/min no login
     * é decisão de segurança, não decisão de ops.
     *
     * Endpoints não listados caem no Default. Endpoints na lista têm bucket
     * próprio no Redis (chave ratelimit:{Key}:{ip}), ou seja, esgotar o
     * limite de login não afeta o limite de listagem de user.
     */
    public sealed record EndpointRateLimit(int Max, int WindowSeconds, string Key);

    public static class RateLimitConfig
    {
        public const int DefaultMax = 100;
        public const int DefaultWindowSeconds = 60;
        public const string DefaultKey = "ip";

        /*
         * Paths que nunca devem ser limitados (health check, metrics, etc).
         * Match exato. Vazio por default — o limite default (100/min) é
         * suficiente para monitoring tools. Adicione aqui se um endpoint
         * específico precisar ser isento (ex.: healthcheck de Kubernetes
         * com loop agressivo).
         */
        public static readonly IReadOnlySet<string> ExemptPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /*
         * Limites por endpoint. Match exato no path.
         *
         * Justificativa dos valores (alinhado com OWASP API Security Top 10):
         *   - /v1/auth/login:           5/min  → credential stuffing (NIST 800-63B)
         *   - /v1/auth/refresh:         30/min → clients legítimos renovam a cada ~50min,
         *                                       mas 30 cobre mobile/web reabrir várias vezes
         *   - /v1/auth/password/request: 3/min → email bombing / DoS de inbox
         *   - /v1/auth/password/validate: 10/min → UX do front (validação ao digitar)
         *   - /v1/auth/password/change:  5/min → alinhado com login
         *   - /v1/user/export/pdf:      10/min → abuse de recurso (CPU/RAM no PDF service)
         */
        public static readonly IReadOnlyDictionary<string, EndpointRateLimit> Endpoints =
            new Dictionary<string, EndpointRateLimit>(StringComparer.OrdinalIgnoreCase)
            {
                ["/v1/auth/login"] = new(Max: 5, WindowSeconds: 60, Key: "login"),
                ["/v1/auth/refresh"] = new(Max: 30, WindowSeconds: 60, Key: "refresh"),
                ["/v1/auth/password/request"] = new(Max: 3, WindowSeconds: 60, Key: "password_request"),
                ["/v1/auth/password/validate"] = new(Max: 10, WindowSeconds: 60, Key: "password_validate"),
                ["/v1/auth/password/change"] = new(Max: 5, WindowSeconds: 60, Key: "password_change"),
                ["/v1/user/export/pdf"] = new(Max: 10, WindowSeconds: 60, Key: "pdf_export")
            };

        public static EndpointRateLimit GetFor(string? path)
        {
            if (string.IsNullOrEmpty(path)) return Default();
            if (Endpoints.TryGetValue(path, out var specific)) return specific;
            return Default();
        }

        public static bool IsExempt(string? path)
        {
            return !string.IsNullOrEmpty(path) && ExemptPaths.Contains(path);
        }

        private static EndpointRateLimit Default() =>
            new(Max: DefaultMax, WindowSeconds: DefaultWindowSeconds, Key: DefaultKey);
    }
}
