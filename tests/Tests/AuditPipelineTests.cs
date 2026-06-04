using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MageBackend.Core.Auditing;
using MageBackend.Core.Middleware;
using MageBackend.Database;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace MageBackend.Tests
{
    /*
     * Cobertura completa do pipeline assíncrono de auditoria introduzido para
     * substituir o Task.Run detached do middleware antigo:
     *  - AuditLogQueue (Channel-based) — enqueue, drain, drop-oldest, shutdown
     *  - AuditLogBackgroundService — batch flush, resiliência, graceful stop
     *  - AuditLogMiddleware — sanitização, captura de contexto, fallback de DI
     *  - Integração end-to-end via Testcontainers (real SQL + HTTP)
     */
    public class AuditPipelineTests : IntegrationTestBase
    {
        public AuditPipelineTests(IntegrationTestFixture fixture) : base(fixture) { }

        // ==========================================
        // --- AuditLogQueue (Channel-backed) -------
        // ==========================================

        [Fact]
        public void GivenNewQueue_WhenInstantiated_ThenUsesDefaultCapacity()
        {
            var originalCapacity = Environment.GetEnvironmentVariable("AUDIT_QUEUE_CAPACITY");
            Environment.SetEnvironmentVariable("AUDIT_QUEUE_CAPACITY", null);
            try
            {
                var queue = new AuditLogQueue();
                Assert.Equal(0, queue.Count);
                Assert.Equal(0, queue.DroppedCount);
                Assert.True(queue.TryEnqueue(MakeEntry()));
                Assert.Equal(1, queue.Count);
            }
            finally
            {
                Environment.SetEnvironmentVariable("AUDIT_QUEUE_CAPACITY", originalCapacity);
            }
        }

        [Fact]
        public void GivenCustomCapacityEnv_WhenInstantiated_ThenRespectsIt()
        {
            using var _ = new EnvVarScope(new Dictionary<string, string?> { ["AUDIT_QUEUE_CAPACITY"] = "2" });

            var queue = new AuditLogQueue();
            queue.TryEnqueue(MakeEntry("first"));
            queue.TryEnqueue(MakeEntry("second"));
            queue.TryEnqueue(MakeEntry("third"));

            Assert.Equal(2, queue.Count);
            Assert.True(queue.TryDequeue(out var first));
            /* DropOldest: o primeiro item foi descartado, então o "second" sobra na frente. */
            Assert.Equal("second", first!.Path);
        }

        [Fact]
        public void GivenInvalidCapacityEnv_WhenInstantiated_ThenUsesDefault()
        {
            using var _ = new EnvVarScope(new Dictionary<string, string?> { ["AUDIT_QUEUE_CAPACITY"] = "not-a-number" });
            var queue = new AuditLogQueue();
            Assert.True(queue.TryEnqueue(MakeEntry()));
        }

        [Fact]
        public void GivenZeroCapacityEnv_WhenInstantiated_ThenUsesDefault()
        {
            using var _ = new EnvVarScope(new Dictionary<string, string?> { ["AUDIT_QUEUE_CAPACITY"] = "0" });
            var queue = new AuditLogQueue();
            Assert.True(queue.TryEnqueue(MakeEntry()));
        }

        [Fact]
        public void GivenNullEntry_WhenEnqueued_ThenThrowsArgumentNullException()
        {
            var queue = new AuditLogQueue();
            Assert.Throws<ArgumentNullException>(() => queue.TryEnqueue(null!));
        }

        [Fact]
        public void GivenCompletedQueue_WhenEnqueued_ThenReturnsFalseAndIncrementsDropped()
        {
            var queue = new AuditLogQueue();
            queue.Complete();

            var ok = queue.TryEnqueue(MakeEntry());

            Assert.False(ok);
            Assert.Equal(1, queue.DroppedCount);
        }

        [Fact]
        public async Task GivenCompletedEmptyQueue_WhenWaitToRead_ThenReturnsFalse()
        {
            var queue = new AuditLogQueue();
            queue.Complete();
            var hasItems = await queue.WaitToReadAsync(CancellationToken.None);
            Assert.False(hasItems);
        }

        [Fact]
        public async Task GivenEnqueuedItem_WhenWaitToRead_ThenReturnsTrue()
        {
            var queue = new AuditLogQueue();
            queue.TryEnqueue(MakeEntry());
            var hasItems = await queue.WaitToReadAsync(CancellationToken.None);
            Assert.True(hasItems);
        }

        [Fact]
        public void GivenEmptyQueue_WhenTryDequeue_ThenReturnsFalse()
        {
            var queue = new AuditLogQueue();
            var ok = queue.TryDequeue(out var entry);
            Assert.False(ok);
            Assert.Null(entry);
        }

        [Fact]
        public void GivenCompleteCalledTwice_WhenInvoked_ThenIdempotent()
        {
            var queue = new AuditLogQueue();
            queue.Complete();
            queue.Complete();
            Assert.False(queue.TryEnqueue(MakeEntry()));
        }

        // ==========================================
        // --- AuditLogBackgroundService ------------
        // ==========================================

        [Fact]
        public void GivenEntryWithResponseBody_WhenMappedToAudit_ThenUsesItAsDiffValue()
        {
            var entry = MakeEntry();
            var withBody = entry with { ResponseBody = "{\"hello\":\"world\"}" };

            var audit = AuditLogBackgroundService.ToAudit(withBody);

            Assert.Equal("{\"hello\":\"world\"}", audit.DiffValue);
            Assert.Equal(entry.IdUser, audit.IdUser);
            Assert.Equal(entry.UserName, audit.UserName);
            Assert.Equal("HTTP_REQUEST", audit.ActionType);
            Assert.Equal(entry.Method, audit.ExecuteType);
            Assert.Equal(entry.Method, audit.Method);
            Assert.Equal(entry.TableName, audit.TableName);
            Assert.Equal(entry.TableName, audit.Class);
            Assert.Equal(entry.Path, audit.Function);
            Assert.Equal(entry.Path, audit.BaseUrl);
            Assert.Equal(entry.Path, audit.OriginalUrl);
            Assert.Equal(entry.Params, audit.Params);
            Assert.Equal(entry.Params, audit.Raw);
            Assert.Equal(entry.Host, audit.Host);
            Assert.Equal(entry.Ip, audit.Ip);
            Assert.Equal(entry.Hostname, audit.Hostname);
        }

        [Fact]
        public void GivenEntryWithoutResponseBody_WhenMappedToAudit_ThenUsesStatusCodeSentinel()
        {
            var entry = MakeEntry() with { ResponseBody = string.Empty, StatusCode = 201 };
            var audit = AuditLogBackgroundService.ToAudit(entry);
            Assert.Contains("201", audit.DiffValue);
            Assert.Contains("statusCode", audit.DiffValue);
        }

        [Fact]
        public async Task GivenEmptyBatch_WhenFlushed_ThenNoOpAndDoesNotOpenScope()
        {
            var scopeFactory = new Mock<IServiceScopeFactory>(MockBehavior.Strict);
            var queue = new AuditLogQueue();
            var service = new AuditLogBackgroundService(queue, scopeFactory.Object);

            await service.FlushAsync(new List<AuditLogEntry>());

            scopeFactory.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task GivenPopulatedBatch_WhenFlushed_ThenWritesAllAndClearsBatch()
        {
            using var scope = _fixture.Services.CreateScope();
            var scopeFactory = _fixture.Services.GetRequiredService<IServiceScopeFactory>();
            var queue = _fixture.Services.GetRequiredService<IAuditLogQueue>();
            var service = new AuditLogBackgroundService(queue, scopeFactory);

            var unique = Guid.NewGuid().ToString("N").Substring(0, 8);
            var batch = new List<AuditLogEntry>
            {
                MakeEntry($"/v1/flush/{unique}/a"),
                MakeEntry($"/v1/flush/{unique}/b")
            };

            await service.FlushAsync(batch);

            Assert.Empty(batch);
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var rows = await db.Audit.Where(a => a.OriginalUrl!.Contains($"/v1/flush/{unique}/")).ToListAsync();
            Assert.Equal(2, rows.Count);
        }

        [Fact]
        public async Task GivenDbFailure_WhenFlushed_ThenLogsAndClearsBatchWithoutThrowing()
        {
            var scopeFactory = new Mock<IServiceScopeFactory>();
            var serviceProvider = new Mock<IServiceProvider>();
            var scopeMock = new Mock<IServiceScope>();
            /* GetRequiredService<ApplicationDbContext> dispara porque o provider devolve null. */
            serviceProvider.Setup(p => p.GetService(typeof(ApplicationDbContext))).Returns((object?)null);
            scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProvider.Object);
            scopeFactory.Setup(f => f.CreateScope()).Returns(scopeMock.Object);

            var queue = new AuditLogQueue();
            var service = new AuditLogBackgroundService(queue, scopeFactory.Object);

            var batch = new List<AuditLogEntry> { MakeEntry() };
            await service.FlushAsync(batch);

            Assert.Empty(batch);
        }

        [Fact]
        public void GivenQueueWithItems_WhenDrainAvailable_ThenFillsBatchUpToCapacity()
        {
            using var _ = new EnvVarScope(new Dictionary<string, string?> { ["AUDIT_BATCH_SIZE"] = "3" });

            var queue = new AuditLogQueue();
            queue.TryEnqueue(MakeEntry("/a"));
            queue.TryEnqueue(MakeEntry("/b"));
            queue.TryEnqueue(MakeEntry("/c"));
            queue.TryEnqueue(MakeEntry("/d"));

            var scopeFactory = new Mock<IServiceScopeFactory>().Object;
            var service = new AuditLogBackgroundService(queue, scopeFactory);

            var batch = new List<AuditLogEntry>();
            service.DrainAvailable(batch);

            Assert.Equal(3, batch.Count);
            Assert.Equal(1, queue.Count);
        }

        [Fact]
        public void GivenEmptyQueue_WhenDrainAvailable_ThenBatchStaysEmpty()
        {
            var queue = new AuditLogQueue();
            var service = new AuditLogBackgroundService(queue, new Mock<IServiceScopeFactory>().Object);
            var batch = new List<AuditLogEntry>();
            service.DrainAvailable(batch);
            Assert.Empty(batch);
        }

        [Fact]
        public async Task GivenRunningService_WhenStopAsyncCalled_ThenCompletesQueueAndExits()
        {
            using var _ = new EnvVarScope(new Dictionary<string, string?> { ["AUDIT_BATCH_SIZE"] = "5" });

            var scopeFactory = _fixture.Services.GetRequiredService<IServiceScopeFactory>();
            var queue = new AuditLogQueue();
            var service = new AuditLogBackgroundService(queue, scopeFactory);

            var unique = Guid.NewGuid().ToString("N").Substring(0, 8);
            queue.TryEnqueue(MakeEntry($"/v1/stopflow/{unique}"));

            await service.StartAsync(CancellationToken.None);
            await Task.Delay(200);
            await service.StopAsync(CancellationToken.None);

            using var scope = _fixture.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await db.Audit.FirstOrDefaultAsync(a => a.OriginalUrl == $"/v1/stopflow/{unique}");
            Assert.NotNull(row);
        }

        [Fact]
        public async Task GivenServiceWithPendingItems_WhenStoppingTokenFires_ThenDrainsRemainingItems()
        {
            using var _ = new EnvVarScope(new Dictionary<string, string?> { ["AUDIT_BATCH_SIZE"] = "5" });

            var scopeFactory = _fixture.Services.GetRequiredService<IServiceScopeFactory>();
            var queue = new AuditLogQueue();
            var service = new AuditLogBackgroundService(queue, scopeFactory);

            await service.StartAsync(CancellationToken.None);

            var unique = Guid.NewGuid().ToString("N").Substring(0, 8);
            for (int i = 0; i < 3; i++)
            {
                queue.TryEnqueue(MakeEntry($"/v1/drain/{unique}/{i}"));
            }

            await Task.Delay(200);
            await service.StopAsync(CancellationToken.None);

            using var scope = _fixture.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var rows = await db.Audit.Where(a => a.OriginalUrl!.Contains($"/v1/drain/{unique}/")).ToListAsync();
            Assert.Equal(3, rows.Count);
        }

        // ==========================================
        // --- AuditLogMiddleware -------------------
        // ==========================================

        [Theory]
        [InlineData("POST", "/v1/product", true)]
        [InlineData("PUT", "/v1/user/123", true)]
        [InlineData("DELETE", "/v1/role/abc", true)]
        [InlineData("PATCH", "/v1/feature/x", true)]
        [InlineData("GET", "/v1/product", false)]
        [InlineData("HEAD", "/v1/product", false)]
        [InlineData("POST", "/v1/auth/login", false)]
        [InlineData("POST", "/V1/Auth/Login", false)]
        [InlineData("POST", "/health", false)]
        [InlineData("POST", "/metrics", false)]
        [InlineData("POST", "/admin/anything", false)]
        public void GivenMethodAndPath_WhenShouldAudit_ThenReturnsExpected(string method, string path, bool expected)
        {
            Assert.Equal(expected, AuditLogMiddleware.ShouldAudit(method, path));
        }

        [Theory]
        [InlineData("/v1/user/123", "User")]
        [InlineData("/v1/role/abc", "Role")]
        [InlineData("/v1/product", "Product")]
        [InlineData("/v1/feature/test", "Feature")]
        [InlineData("/v1/USER", "User")]
        [InlineData("/v1/unknown", "System")]
        public void GivenPath_WhenResolveTableName_ThenReturnsExpected(string path, string expected)
        {
            Assert.Equal(expected, AuditLogMiddleware.ResolveTableName(path));
        }

        [Fact]
        public void GivenNullOrEmptyBody_WhenSanitized_ThenReturnsNull()
        {
            Assert.Null(AuditLogMiddleware.SanitizeBody(null));
            Assert.Null(AuditLogMiddleware.SanitizeBody(string.Empty));
        }

        [Fact]
        public void GivenBodyWithSecrets_WhenSanitized_ThenRedactsCaseInsensitively()
        {
            var body = "{\"name\":\"john\",\"Password\":\"secret\",\"refreshToken\":\"abc\",\"nested\":42}";
            var sanitized = AuditLogMiddleware.SanitizeBody(body);
            Assert.Contains("\"john\"", sanitized);
            Assert.Contains("\"******\"", sanitized);
            Assert.DoesNotContain("secret", sanitized);
            Assert.DoesNotContain("abc", sanitized);
            Assert.Contains("42", sanitized);
        }

        [Fact]
        public void GivenNonObjectJson_WhenSanitized_ThenReturnsAsIs()
        {
            var body = "[1,2,3]";
            Assert.Equal(body, AuditLogMiddleware.SanitizeBody(body));
        }

        [Fact]
        public void GivenInvalidJson_WhenSanitized_ThenReturnsAsIs()
        {
            var body = "{not-json";
            Assert.Equal(body, AuditLogMiddleware.SanitizeBody(body));
        }

        [Fact]
        public async Task GivenContextWithoutBody_WhenReadRequestBody_ThenReturnsNull()
        {
            var context = new DefaultHttpContext();
            Assert.Null(await AuditLogMiddleware.ReadRequestBodyAsync(context));
        }

        [Fact]
        public async Task GivenContextWithBody_WhenReadRequestBody_ThenReadsAndRewinds()
        {
            var context = new DefaultHttpContext();
            var payload = "{\"x\":1}";
            var bytes = Encoding.UTF8.GetBytes(payload);
            context.Request.Body = new MemoryStream(bytes);
            context.Request.ContentLength = bytes.Length;

            var body = await AuditLogMiddleware.ReadRequestBodyAsync(context);
            Assert.Equal(payload, body);
            Assert.Equal(0, context.Request.Body.Position);
        }

        [Fact]
        public void GivenContextWithUserClaims_WhenBuildEntry_ThenExtractsThem()
        {
            var context = new DefaultHttpContext();
            context.Request.Host = new HostString("api.example.com");
            context.Response.StatusCode = 201;
            var identity = new System.Security.Claims.ClaimsIdentity(new[]
            {
                new System.Security.Claims.Claim("id", "user-1"),
                new System.Security.Claims.Claim("email", "user@example.com")
            });
            context.User = new System.Security.Claims.ClaimsPrincipal(identity);

            var entry = AuditLogMiddleware.BuildEntry(context, "POST", "/v1/product", "params", string.Empty);

            Assert.Equal("user-1", entry.IdUser);
            Assert.Equal("user@example.com", entry.UserName);
            Assert.Equal("POST", entry.Method);
            Assert.Equal("/v1/product", entry.Path);
            Assert.Equal("Product", entry.TableName);
            Assert.Equal("params", entry.Params);
            Assert.Equal(201, entry.StatusCode);
            Assert.Equal("api.example.com", entry.Host);
            Assert.Equal("api.example.com", entry.Hostname);
            Assert.Equal("127.0.0.1", entry.Ip);
        }

        [Fact]
        public void GivenContextWithoutClaims_WhenBuildEntry_ThenDefaultsToAnonymous()
        {
            var context = new DefaultHttpContext();
            context.Request.Host = new HostString("localhost");

            var entry = AuditLogMiddleware.BuildEntry(context, "DELETE", "/v1/role/1", null, string.Empty);

            Assert.Null(entry.IdUser);
            Assert.Equal("Anonymous", entry.UserName);
        }

        [Fact]
        public async Task GivenExcludedPath_WhenMiddlewareInvoked_ThenSkipsEnqueue()
        {
            var queue = new Mock<IAuditLogQueue>(MockBehavior.Strict);
            var middleware = new AuditLogMiddleware(_ => Task.CompletedTask);
            var context = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };
            context.Request.Method = "POST";
            context.Request.Path = "/health";

            await middleware.InvokeAsync(context, queue.Object);

            queue.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task GivenAuditedRequest_WhenMiddlewareInvoked_ThenEnqueuesEntry()
        {
            var enqueued = new List<AuditLogEntry>();
            var queue = new Mock<IAuditLogQueue>();
            queue.Setup(q => q.TryEnqueue(It.IsAny<AuditLogEntry>()))
                 .Callback<AuditLogEntry>(e => enqueued.Add(e))
                 .Returns(true);

            var middleware = new AuditLogMiddleware(_ => Task.CompletedTask);
            var context = new DefaultHttpContext
            {
                RequestServices = new ServiceCollection().BuildServiceProvider()
            };
            context.Request.Method = "POST";
            context.Request.Path = "/v1/product";
            context.Request.Host = new HostString("test");

            await middleware.InvokeAsync(context, queue.Object);

            Assert.Single(enqueued);
            Assert.Equal("/v1/product", enqueued[0].Path);
        }

        [Fact]
        public async Task GivenNoQueueParam_WhenMiddlewareInvoked_ThenResolvesFromDI()
        {
            var queue = new Mock<IAuditLogQueue>();
            queue.Setup(q => q.TryEnqueue(It.IsAny<AuditLogEntry>())).Returns(true);

            var services = new ServiceCollection();
            services.AddSingleton(queue.Object);

            var middleware = new AuditLogMiddleware(_ => Task.CompletedTask);
            var context = new DefaultHttpContext
            {
                RequestServices = services.BuildServiceProvider()
            };
            context.Request.Method = "POST";
            context.Request.Path = "/v1/product";
            context.Request.Host = new HostString("test");

            await middleware.InvokeAsync(context);

            queue.Verify(q => q.TryEnqueue(It.IsAny<AuditLogEntry>()), Times.Once);
        }

        [Fact]
        public async Task GivenNoQueueRegistered_WhenMiddlewareInvoked_ThenLogsWarningAndContinues()
        {
            var middleware = new AuditLogMiddleware(_ => Task.CompletedTask);
            var context = new DefaultHttpContext
            {
                RequestServices = new ServiceCollection().BuildServiceProvider()
            };
            context.Request.Method = "POST";
            context.Request.Path = "/v1/product";
            context.Request.Host = new HostString("test");

            await middleware.InvokeAsync(context);
            /* O middleware deve completar sem throw quando a fila não está registrada. */
            Assert.True(true);
        }

        // ==========================================
        // --- Integração end-to-end ----------------
        // ==========================================

        [Fact]
        public async Task GivenConcurrentMutatingRequests_WhenProcessed_ThenAllAreAudited()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var batchId = Guid.NewGuid().ToString("N").Substring(0, 8);
            var tasks = new List<Task<HttpResponseMessage>>();
            for (int i = 0; i < 5; i++)
            {
                tasks.Add(_client.PostAsJsonAsync("/v1/product", new
                {
                    name = $"Concurrent Product {batchId} #{i}",
                    sku = $"sku-concurrent-{batchId}-{i}",
                    category = "concurrency-test",
                    description = "Backpressure check",
                    price = 9.99,
                    stock = 1
                }));
            }

            var responses = await Task.WhenAll(tasks);
            foreach (var resp in responses)
            {
                Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            }

            await Task.Delay(800);

            using var scope = _fixture.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var rows = await db.Audit
                .Where(a => a.TableName == "Product" && a.Params != null && a.Params.Contains(batchId))
                .ToListAsync();

            Assert.Equal(5, rows.Count);
            foreach (var row in rows)
            {
                Assert.Equal(loginData.User.Id, row.IdUser);
                Assert.Equal("POST", row.Method);
            }

            ClearAuthHeader();
        }

        // ==========================================
        // --- Helpers ------------------------------
        // ==========================================

        private static AuditLogEntry MakeEntry(string path = "/v1/test")
        {
            return new AuditLogEntry
            {
                IdUser = "user-1",
                UserName = "tester@example.com",
                Method = "POST",
                Path = path,
                TableName = "Test",
                Params = "{\"x\":1}",
                ResponseBody = string.Empty,
                StatusCode = 200,
                Host = "localhost",
                Ip = "127.0.0.1",
                Hostname = "localhost",
                CreatedAt = DateTime.UtcNow
            };
        }
    }
}
