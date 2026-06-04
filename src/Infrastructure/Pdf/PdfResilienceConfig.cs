using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Extensions.Http.Resilience;

namespace MageBackend.Infrastructure.Pdf
{
    /*
     * Configuração parametrizável do pipeline de resiliência do PdfProvider.
     * Lê env vars (PDF_RESILIENCE_*) com defaults seguros; valores inválidos
     * ou negativos caem no default para garantir boot.
     *
     * O pipeline encadeia 5 estratégias (de fora para dentro):
     *   RateLimiter -> TotalRequestTimeout -> Retry -> CircuitBreaker -> AttemptTimeout
     *
     * Defaults seguem AddStandardResilienceHandler() do .NET 10. Variáveis:
     *   PDF_RESILIENCE_ENABLED              (bool,   default true)
     *   PDF_RESILIENCE_MAX_RETRY_ATTEMPTS   (int,    default 3)
     *   PDF_RESILIENCE_TIMEOUT_SECONDS      (int,    default 30)
     *   PDF_RESILIENCE_ATTEMPT_TIMEOUT_SECONDS (int, default 10)
     *   PDF_RESILIENCE_CB_RATIO             (double, default 0.1)
     *   PDF_RESILIENCE_CB_SAMPLING_SECONDS  (int,    default 30)
     *   PDF_RESILIENCE_CB_MIN_ATTEMPTS      (int,    default 5)
     *   PDF_RESILIENCE_CB_BREAK_SECONDS     (double, default 5)
     */
    public static class PdfResilienceConfig
    {
        public const string EnabledEnvVar = "PDF_RESILIENCE_ENABLED";
        public const string MaxRetryAttemptsEnvVar = "PDF_RESILIENCE_MAX_RETRY_ATTEMPTS";
        public const string TimeoutSecondsEnvVar = "PDF_RESILIENCE_TIMEOUT_SECONDS";
        public const string AttemptTimeoutSecondsEnvVar = "PDF_RESILIENCE_ATTEMPT_TIMEOUT_SECONDS";
        public const string CircuitBreakerRatioEnvVar = "PDF_RESILIENCE_CB_RATIO";
        public const string CircuitBreakerSamplingSecondsEnvVar = "PDF_RESILIENCE_CB_SAMPLING_SECONDS";
        public const string CircuitBreakerMinAttemptsEnvVar = "PDF_RESILIENCE_CB_MIN_ATTEMPTS";
        public const string CircuitBreakerBreakSecondsEnvVar = "PDF_RESILIENCE_CB_BREAK_SECONDS";

        public const bool DefaultEnabled = true;
        public const int DefaultMaxRetryAttempts = 3;
        public const int DefaultTimeoutSeconds = 30;
        public const int DefaultAttemptTimeoutSeconds = 10;
        public const double DefaultCircuitBreakerRatio = 0.1;
        public const int DefaultCircuitBreakerSamplingSeconds = 30;
        public const int DefaultCircuitBreakerMinAttempts = 5;
        public const double DefaultCircuitBreakerBreakSeconds = 5;

        public static bool IsEnabled()
        {
            return ReadBool(EnabledEnvVar, DefaultEnabled);
        }

        public static void Configure(HttpStandardResilienceOptions options)
        {
            options.Retry.MaxRetryAttempts = ReadInt(MaxRetryAttemptsEnvVar, DefaultMaxRetryAttempts);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(ReadInt(TimeoutSecondsEnvVar, DefaultTimeoutSeconds));
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(ReadInt(AttemptTimeoutSecondsEnvVar, DefaultAttemptTimeoutSeconds));
            options.CircuitBreaker.FailureRatio = ReadDouble(CircuitBreakerRatioEnvVar, DefaultCircuitBreakerRatio);
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(ReadInt(CircuitBreakerSamplingSecondsEnvVar, DefaultCircuitBreakerSamplingSeconds));
            options.CircuitBreaker.MinimumThroughput = ReadInt(CircuitBreakerMinAttemptsEnvVar, DefaultCircuitBreakerMinAttempts);
            options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(ReadDouble(CircuitBreakerBreakSecondsEnvVar, DefaultCircuitBreakerBreakSeconds));
        }

        internal static int ReadInt(string name, int defaultValue)
        {
            var raw = Environment.GetEnvironmentVariable(name);
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0
                ? parsed
                : defaultValue;
        }

        internal static double ReadDouble(string name, double defaultValue)
        {
            var raw = Environment.GetEnvironmentVariable(name);
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0
                ? parsed
                : defaultValue;
        }

        internal static bool ReadBool(string name, bool defaultValue)
        {
            var raw = Environment.GetEnvironmentVariable(name);
            return bool.TryParse(raw, out var parsed) ? parsed : defaultValue;
        }
    }
}
