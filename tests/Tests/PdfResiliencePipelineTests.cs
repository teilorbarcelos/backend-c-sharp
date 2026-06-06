using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MageBackend.Infrastructure.Pdf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Xunit;

namespace MageBackend.Tests
{
    /*
     * Testes de integração do pipeline de resiliência do PdfProvider.
     * Usa IHttpClientFactory + AddStandardResilienceHandler + um mock handler
     * primário para validar retry, circuit breaker e timeout em produção.
     */
    public class PdfResiliencePipelineTests
    {
        private const string TestUrl = "http://pdf-test-service/v1/pdf/generate";

        private class MockHttpMessageHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _sendAsync;
            public int CallCount { get; private set; }

            public MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
            {
                _sendAsync = sendAsync;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                CallCount++;
                return _sendAsync(request, cancellationToken);
            }
        }

        private static (IHttpClientFactory factory, MockHttpMessageHandler handler) BuildFactory(
            int maxRetries = 3,
            int attemptTimeoutSeconds = 2,
            int totalTimeoutSeconds = 10,
            double failureRatio = 0.5,
            int minimumThroughput = 5,
            int breakSeconds = 2)
        {
            var handler = new MockHttpMessageHandler((req, ct) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(new byte[] { 0x25, 0x50, 0x44, 0x46 })
                }));

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddHttpClient("pdf-test", c => { c.Timeout = TimeSpan.FromSeconds(30); })
                .ConfigurePrimaryHttpMessageHandler(() => handler)
                .AddStandardResilienceHandler(opts =>
                {
                    opts.Retry.MaxRetryAttempts = maxRetries;
                    opts.Retry.Delay = TimeSpan.FromMilliseconds(10);
                    opts.Retry.BackoffType = Polly.DelayBackoffType.Constant;
                    opts.AttemptTimeout.Timeout = TimeSpan.FromSeconds(attemptTimeoutSeconds);
                    opts.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(totalTimeoutSeconds);
                    opts.CircuitBreaker.FailureRatio = failureRatio;
                    opts.CircuitBreaker.MinimumThroughput = minimumThroughput;
                    opts.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
                    opts.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(breakSeconds);
                });

            var sp = services.BuildServiceProvider();
            return (sp.GetRequiredService<IHttpClientFactory>(), handler);
        }

        [Fact]
        public async Task GivenTransientFailuresFollowedBySuccess_WhenRequesting_ThenRetriesAndSucceeds()
        {
            int callCount = 0;
            var handler = new MockHttpMessageHandler((req, ct) =>
            {
                callCount++;
                if (callCount < 3)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(new byte[] { 0x25, 0x50, 0x44, 0x46 })
                });
            });

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddHttpClient("pdf-test")
                .ConfigurePrimaryHttpMessageHandler(() => handler)
                .AddStandardResilienceHandler(opts =>
                {
                    opts.Retry.MaxRetryAttempts = 5;
                    opts.Retry.Delay = TimeSpan.FromMilliseconds(5);
                    opts.Retry.BackoffType = Polly.DelayBackoffType.Constant;
                    opts.AttemptTimeout.Timeout = TimeSpan.FromSeconds(2);
                    opts.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);
                    opts.CircuitBreaker.MinimumThroughput = 100;
                });
            var sp = services.BuildServiceProvider();
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var client = factory.CreateClient("pdf-test");

            var response = await client.GetAsync(TestUrl);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(3, callCount);
        }

        [Fact]
        public async Task GivenAllAttemptsFail_WhenRequesting_ThenGivesUpAfterMaxRetries()
        {
            int callCount = 0;
            var handler = new MockHttpMessageHandler((req, ct) =>
            {
                callCount++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            });

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddHttpClient("pdf-test")
                .ConfigurePrimaryHttpMessageHandler(() => handler)
                .AddStandardResilienceHandler(opts =>
                {
                    opts.Retry.MaxRetryAttempts = 2;
                    opts.Retry.Delay = TimeSpan.FromMilliseconds(5);
                    opts.Retry.BackoffType = Polly.DelayBackoffType.Constant;
                    opts.AttemptTimeout.Timeout = TimeSpan.FromSeconds(2);
                    opts.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);
                    opts.CircuitBreaker.MinimumThroughput = 100;
                });
            var sp = services.BuildServiceProvider();
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var client = factory.CreateClient("pdf-test");

            var response = await client.GetAsync(TestUrl);

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal(3, callCount);
        }

        [Fact]
        public async Task GivenAllRequestsFailWith5xxAboveThreshold_WhenRequesting_ThenCircuitBreakerOpens()
        {
            int callCount = 0;
            var handler = new MockHttpMessageHandler((req, ct) =>
            {
                callCount++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            });

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddHttpClient("pdf-test")
                .ConfigurePrimaryHttpMessageHandler(() => handler)
                .AddStandardResilienceHandler(opts =>
                {
                    opts.Retry.MaxRetryAttempts = 1;
                    opts.Retry.Delay = TimeSpan.FromMilliseconds(5);
                    opts.Retry.BackoffType = Polly.DelayBackoffType.Constant;
                    opts.AttemptTimeout.Timeout = TimeSpan.FromSeconds(2);
                    opts.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);
                    opts.CircuitBreaker.FailureRatio = 0.5;
                    opts.CircuitBreaker.MinimumThroughput = 4;
                    opts.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
                    opts.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
                });
            var sp = services.BuildServiceProvider();
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var client = factory.CreateClient("pdf-test");

            var first = await client.GetAsync(TestUrl);
            var second = await client.GetAsync(TestUrl);

            Assert.Equal(HttpStatusCode.InternalServerError, first.StatusCode);
            Assert.Equal(HttpStatusCode.InternalServerError, second.StatusCode);

            var ex = await Assert.ThrowsAsync<BrokenCircuitException>(() => client.GetAsync(TestUrl));

            Assert.NotNull(ex);
        }

        [Fact]
        public async Task GivenSlowHandlerExceedingAttemptTimeout_WhenRequesting_ThenTimeoutRejectedException()
        {
            var handler = new MockHttpMessageHandler(async (req, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddHttpClient("pdf-test")
                .ConfigurePrimaryHttpMessageHandler(() => handler)
                .AddStandardResilienceHandler(opts =>
                {
                    opts.Retry.MaxRetryAttempts = 1;
                    opts.Retry.Delay = TimeSpan.FromMilliseconds(5);
                    opts.Retry.BackoffType = Polly.DelayBackoffType.Constant;
                    opts.AttemptTimeout.Timeout = TimeSpan.FromMilliseconds(100);
                    opts.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
                    opts.CircuitBreaker.MinimumThroughput = 100;
                });
            var sp = services.BuildServiceProvider();
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var client = factory.CreateClient("pdf-test");

            await Assert.ThrowsAsync<TimeoutRejectedException>(() => client.GetAsync(TestUrl));
        }

        [Fact]
        public async Task GivenSuccessfulRequest_WhenResilienceEnabled_ThenDelegatesToPrimaryHandlerOnce()
        {
            var (factory, handler) = BuildFactory(maxRetries: 3);
            var client = factory.CreateClient("pdf-test");

            var response = await client.GetAsync(TestUrl);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(1, handler.CallCount);
        }
    }
}
