using System;
using System.Collections.Generic;
using System.Linq;

namespace MageBackend.Infrastructure.Configuration
{
    /*
     * Configuração parametrizável da política CORS.
     *
     * Substitui o "AllowAnyOrigin + AllowCredentials" do boilerplate original,
     * vetor de CSRF cross-origin: hoje os browsers bloqueiam a combinação, mas
     * basta alguém relaxar a config (ou o cliente ser não-browser) para virar
     * breach — qualquer site malicioso pode disparar requests autenticados.
     *
     * Variável de ambiente:
     *   CORS_ALLOWED_ORIGINS — lista separada por vírgula (case-sensitive, sem
     *                          wildcard). Quando ausente em Produção, lança
     *                          InvalidOperationException (fail-fast, sem
     *                          default silencioso em prod). Em outros ambientes
     *                          (Development/Testing/Staging/...), cai em
     *                          DevDefaultOrigins.
     */
    public static class CorsConfig
    {
        public const string AllowedOriginsEnvVar = "CORS_ALLOWED_ORIGINS";
        public const string ProductionEnvironment = "Production";

        public static readonly IReadOnlyList<string> DevDefaultOrigins = new[]
        {
            "http://localhost:3000",
            "http://localhost:4200",
            "http://localhost:5173",
            "http://localhost:8080",
            "http://127.0.0.1:3000",
            "http://127.0.0.1:4200",
            "http://127.0.0.1:5173",
            "http://127.0.0.1:8080"
        };

        public static bool IsProduction(string environmentName)
        {
            return string.Equals(environmentName, ProductionEnvironment, StringComparison.OrdinalIgnoreCase);
        }

        public static IReadOnlyList<string> GetAllowedOrigins(string environmentName)
        {
            var raw = Environment.GetEnvironmentVariable(AllowedOriginsEnvVar);

            if (!string.IsNullOrWhiteSpace(raw))
            {
                return raw
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToArray();
            }

            if (IsProduction(environmentName))
            {
                throw new InvalidOperationException(
                    $"{AllowedOriginsEnvVar} environment variable must be set in Production. " +
                    "Provide a comma-separated list of allowed origins (e.g. https://app.example.com).");
            }

            return DevDefaultOrigins;
        }
    }
}
