using System;
using MageBackend.Infrastructure.Pdf;
using Microsoft.Extensions.Http.Resilience;
using Xunit;

namespace MageBackend.Tests
{
    public class PdfResilienceConfigTests : IDisposable
    {
        private const string EnabledEnvVar = PdfResilienceConfig.EnabledEnvVar;
        private const string MaxRetryAttemptsEnvVar = PdfResilienceConfig.MaxRetryAttemptsEnvVar;
        private const string TimeoutSecondsEnvVar = PdfResilienceConfig.TimeoutSecondsEnvVar;
        private const string AttemptTimeoutSecondsEnvVar = PdfResilienceConfig.AttemptTimeoutSecondsEnvVar;
        private const string CircuitBreakerRatioEnvVar = PdfResilienceConfig.CircuitBreakerRatioEnvVar;
        private const string CircuitBreakerSamplingSecondsEnvVar = PdfResilienceConfig.CircuitBreakerSamplingSecondsEnvVar;
        private const string CircuitBreakerMinAttemptsEnvVar = PdfResilienceConfig.CircuitBreakerMinAttemptsEnvVar;
        private const string CircuitBreakerBreakSecondsEnvVar = PdfResilienceConfig.CircuitBreakerBreakSecondsEnvVar;

        public PdfResilienceConfigTests()
        {
            ClearAllEnvVars();
        }

        public void Dispose()
        {
            ClearAllEnvVars();
            GC.SuppressFinalize(this);
        }

        [Fact]
        public void GivenNoEnvVars_WhenIsEnabled_ThenReturnsTrue()
        {
            Assert.True(PdfResilienceConfig.IsEnabled());
        }

        [Fact]
        public void GivenDisabledEnvVar_WhenIsEnabled_ThenReturnsFalse()
        {
            Environment.SetEnvironmentVariable(EnabledEnvVar, "false");
            Assert.False(PdfResilienceConfig.IsEnabled());
        }

        [Fact]
        public void GivenEnabledEnvVar_WhenIsEnabled_ThenReturnsTrue()
        {
            Environment.SetEnvironmentVariable(EnabledEnvVar, "true");
            Assert.True(PdfResilienceConfig.IsEnabled());
        }

        [Fact]
        public void GivenInvalidBoolEnvVar_WhenIsEnabled_ThenReturnsDefault()
        {
            Environment.SetEnvironmentVariable(EnabledEnvVar, "not-a-bool");
            Assert.True(PdfResilienceConfig.IsEnabled());
        }

        [Fact]
        public void GivenNoEnvVars_WhenConfigure_ThenUsesDefaults()
        {
            var opts = new HttpStandardResilienceOptions();

            PdfResilienceConfig.Configure(opts);

            Assert.Equal(PdfResilienceConfig.DefaultMaxRetryAttempts, opts.Retry.MaxRetryAttempts);
            Assert.Equal(TimeSpan.FromSeconds(PdfResilienceConfig.DefaultTimeoutSeconds), opts.TotalRequestTimeout.Timeout);
            Assert.Equal(TimeSpan.FromSeconds(PdfResilienceConfig.DefaultAttemptTimeoutSeconds), opts.AttemptTimeout.Timeout);
            Assert.Equal(PdfResilienceConfig.DefaultCircuitBreakerRatio, opts.CircuitBreaker.FailureRatio);
            Assert.Equal(TimeSpan.FromSeconds(PdfResilienceConfig.DefaultCircuitBreakerSamplingSeconds), opts.CircuitBreaker.SamplingDuration);
            Assert.Equal(PdfResilienceConfig.DefaultCircuitBreakerMinAttempts, opts.CircuitBreaker.MinimumThroughput);
            Assert.Equal(TimeSpan.FromSeconds(PdfResilienceConfig.DefaultCircuitBreakerBreakSeconds), opts.CircuitBreaker.BreakDuration);
        }

        [Fact]
        public void GivenCustomEnvVars_WhenConfigure_ThenAppliesValues()
        {
            Environment.SetEnvironmentVariable(MaxRetryAttemptsEnvVar, "7");
            Environment.SetEnvironmentVariable(TimeoutSecondsEnvVar, "45");
            Environment.SetEnvironmentVariable(AttemptTimeoutSecondsEnvVar, "12");
            Environment.SetEnvironmentVariable(CircuitBreakerRatioEnvVar, "0.5");
            Environment.SetEnvironmentVariable(CircuitBreakerSamplingSecondsEnvVar, "60");
            Environment.SetEnvironmentVariable(CircuitBreakerMinAttemptsEnvVar, "20");
            Environment.SetEnvironmentVariable(CircuitBreakerBreakSecondsEnvVar, "15.5");

            var opts = new HttpStandardResilienceOptions();

            PdfResilienceConfig.Configure(opts);

            Assert.Equal(7, opts.Retry.MaxRetryAttempts);
            Assert.Equal(TimeSpan.FromSeconds(45), opts.TotalRequestTimeout.Timeout);
            Assert.Equal(TimeSpan.FromSeconds(12), opts.AttemptTimeout.Timeout);
            Assert.Equal(0.5, opts.CircuitBreaker.FailureRatio);
            Assert.Equal(TimeSpan.FromSeconds(60), opts.CircuitBreaker.SamplingDuration);
            Assert.Equal(20, opts.CircuitBreaker.MinimumThroughput);
            Assert.Equal(TimeSpan.FromSeconds(15.5), opts.CircuitBreaker.BreakDuration);
        }

        [Fact]
        public void GivenInvalidIntEnvVars_WhenConfigure_ThenUsesDefaults()
        {
            Environment.SetEnvironmentVariable(MaxRetryAttemptsEnvVar, "not-a-number");
            Environment.SetEnvironmentVariable(TimeoutSecondsEnvVar, "abc");
            Environment.SetEnvironmentVariable(AttemptTimeoutSecondsEnvVar, "12.5");
            Environment.SetEnvironmentVariable(CircuitBreakerSamplingSecondsEnvVar, "-10");
            Environment.SetEnvironmentVariable(CircuitBreakerMinAttemptsEnvVar, " ");

            var opts = new HttpStandardResilienceOptions();

            PdfResilienceConfig.Configure(opts);

            Assert.Equal(PdfResilienceConfig.DefaultMaxRetryAttempts, opts.Retry.MaxRetryAttempts);
            Assert.Equal(TimeSpan.FromSeconds(PdfResilienceConfig.DefaultTimeoutSeconds), opts.TotalRequestTimeout.Timeout);
            Assert.Equal(TimeSpan.FromSeconds(PdfResilienceConfig.DefaultAttemptTimeoutSeconds), opts.AttemptTimeout.Timeout);
            Assert.Equal(TimeSpan.FromSeconds(PdfResilienceConfig.DefaultCircuitBreakerSamplingSeconds), opts.CircuitBreaker.SamplingDuration);
            Assert.Equal(PdfResilienceConfig.DefaultCircuitBreakerMinAttempts, opts.CircuitBreaker.MinimumThroughput);
        }

        [Fact]
        public void GivenInvalidDoubleEnvVars_WhenConfigure_ThenUsesDefaults()
        {
            Environment.SetEnvironmentVariable(CircuitBreakerRatioEnvVar, "NaN");
            Environment.SetEnvironmentVariable(CircuitBreakerBreakSecondsEnvVar, "-1.5");

            var opts = new HttpStandardResilienceOptions();

            PdfResilienceConfig.Configure(opts);

            Assert.Equal(PdfResilienceConfig.DefaultCircuitBreakerRatio, opts.CircuitBreaker.FailureRatio);
            Assert.Equal(TimeSpan.FromSeconds(PdfResilienceConfig.DefaultCircuitBreakerBreakSeconds), opts.CircuitBreaker.BreakDuration);
        }

        [Fact]
        public void GivenNegativeIntEnvVar_WhenReadInt_ThenReturnsDefault()
        {
            Environment.SetEnvironmentVariable(MaxRetryAttemptsEnvVar, "-3");

            var result = PdfResilienceConfig.ReadInt(MaxRetryAttemptsEnvVar, 99);

            Assert.Equal(99, result);
        }

        [Fact]
        public void GivenZeroIntEnvVar_WhenReadInt_ThenReturnsZero()
        {
            Environment.SetEnvironmentVariable(MaxRetryAttemptsEnvVar, "0");

            var result = PdfResilienceConfig.ReadInt(MaxRetryAttemptsEnvVar, 99);

            Assert.Equal(0, result);
        }

        [Fact]
        public void GivenValidIntEnvVar_WhenReadInt_ThenReturnsParsedValue()
        {
            Environment.SetEnvironmentVariable(MaxRetryAttemptsEnvVar, "42");

            var result = PdfResilienceConfig.ReadInt(MaxRetryAttemptsEnvVar, 99);

            Assert.Equal(42, result);
        }

        [Fact]
        public void GivenUnsetIntEnvVar_WhenReadInt_ThenReturnsDefault()
        {
            var result = PdfResilienceConfig.ReadInt("PDF_UNSET_VAR_42", 99);

            Assert.Equal(99, result);
        }

        [Fact]
        public void GivenValidDoubleEnvVar_WhenReadDouble_ThenReturnsParsedValue()
        {
            Environment.SetEnvironmentVariable(CircuitBreakerRatioEnvVar, "0.75");

            var result = PdfResilienceConfig.ReadDouble(CircuitBreakerRatioEnvVar, 0.1);

            Assert.Equal(0.75, result);
        }

        [Fact]
        public void GivenNegativeDoubleEnvVar_WhenReadDouble_ThenReturnsDefault()
        {
            Environment.SetEnvironmentVariable(CircuitBreakerRatioEnvVar, "-0.5");

            var result = PdfResilienceConfig.ReadDouble(CircuitBreakerRatioEnvVar, 0.1);

            Assert.Equal(0.1, result);
        }

        [Fact]
        public void GivenUnsetDoubleEnvVar_WhenReadDouble_ThenReturnsDefault()
        {
            var result = PdfResilienceConfig.ReadDouble("PDF_UNSET_VAR_99", 0.42);

            Assert.Equal(0.42, result);
        }

        [Fact]
        public void GivenValidBoolEnvVar_WhenReadBool_ThenReturnsParsedValue()
        {
            Environment.SetEnvironmentVariable(EnabledEnvVar, "false");

            var result = PdfResilienceConfig.ReadBool(EnabledEnvVar, true);

            Assert.False(result);
        }

        [Fact]
        public void GivenInvalidBoolEnvVar_WhenReadBool_ThenReturnsDefault()
        {
            Environment.SetEnvironmentVariable(EnabledEnvVar, "garbage");

            var result = PdfResilienceConfig.ReadBool(EnabledEnvVar, true);

            Assert.True(result);
        }

        [Fact]
        public void GivenUnsetBoolEnvVar_WhenReadBool_ThenReturnsDefault()
        {
            var result = PdfResilienceConfig.ReadBool("PDF_UNSET_VAR_TRUE", false);

            Assert.False(result);
        }

        private static void ClearAllEnvVars()
        {
            Environment.SetEnvironmentVariable(EnabledEnvVar, null);
            Environment.SetEnvironmentVariable(MaxRetryAttemptsEnvVar, null);
            Environment.SetEnvironmentVariable(TimeoutSecondsEnvVar, null);
            Environment.SetEnvironmentVariable(AttemptTimeoutSecondsEnvVar, null);
            Environment.SetEnvironmentVariable(CircuitBreakerRatioEnvVar, null);
            Environment.SetEnvironmentVariable(CircuitBreakerSamplingSecondsEnvVar, null);
            Environment.SetEnvironmentVariable(CircuitBreakerMinAttemptsEnvVar, null);
            Environment.SetEnvironmentVariable(CircuitBreakerBreakSecondsEnvVar, null);
        }
    }
}
