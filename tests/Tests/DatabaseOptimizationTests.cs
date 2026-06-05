using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using MageBackend.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MageBackend.Tests
{
    /*
     * Regressões críticas para o boilerplate:
     *   1. Índices hot-path estão presentes nas entities e na migration
     *   2. NoTracking é o default (para preservar o ganho de perf)
     *   3. Writes ainda funcionam (AsTracking aplicado onde necessário)
     *
     * Se algum desses quebrar, novo projeto que herdar o boilerplate
     * terá regressão de performance ou bug silencioso de write.
     */
    public class DatabaseOptimizationTests : IntegrationTestBase
    {
        public DatabaseOptimizationTests(IntegrationTestFixture fixture) : base(fixture) { }

        [Fact]
        public void GivenUserEntity_WhenInspected_ThenHasHotPathIndexes()
        {
            using var scope = _fixture.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userEntity = dbContext.Model.FindEntityType(typeof(User));

            Assert.NotNull(userEntity);

            var indexedProps = userEntity!.GetIndexes()
                .Select(i => string.Join(",", i.Properties.Select(p => p.Name)))
                .ToList();

            Assert.Contains(indexedProps, p => p.Contains("Email"));
            Assert.Contains(indexedProps, p => p.Contains("CognitoId"));
            Assert.Contains(indexedProps, p => p.Contains("Document"));
            Assert.Contains(indexedProps, p => p.Contains("IdRole"));
        }

        [Fact]
        public void GivenUserEmailIndex_WhenInspected_ThenIsUnique()
        {
            using var scope = _fixture.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userEntity = dbContext.Model.FindEntityType(typeof(User));

            var emailIndex = userEntity!.GetIndexes()
                .FirstOrDefault(i => i.Properties.Any(p => p.Name == "Email"));

            Assert.NotNull(emailIndex);
            Assert.True(emailIndex!.IsUnique);
        }

        [Fact]
        public void GivenProductEntity_WhenInspected_ThenHasHotPathIndexes()
        {
            using var scope = _fixture.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var productEntity = dbContext.Model.FindEntityType(typeof(Product));

            Assert.NotNull(productEntity);

            var indexedProps = productEntity!.GetIndexes()
                .Select(i => string.Join(",", i.Properties.Select(p => p.Name)))
                .ToList();

            Assert.Contains(indexedProps, p => p.Contains("Sku"));
            Assert.Contains(indexedProps, p => p.Contains("Category"));
        }

        [Fact]
        public void GivenProductSkuIndex_WhenInspected_ThenIsUnique()
        {
            using var scope = _fixture.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var productEntity = dbContext.Model.FindEntityType(typeof(Product));

            var skuIndex = productEntity!.GetIndexes()
                .FirstOrDefault(i => i.Properties.Any(p => p.Name == "Sku"));

            Assert.NotNull(skuIndex);
            Assert.True(skuIndex!.IsUnique);
        }

        [Fact]
        public void GivenAuditEntity_WhenInspected_ThenHasCompositeIndexOnIdUserAndCreatedAt()
        {
            using var scope = _fixture.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var auditEntity = dbContext.Model.FindEntityType(typeof(Audit));

            Assert.NotNull(auditEntity);

            var compositeIndex = auditEntity!.GetIndexes()
                .FirstOrDefault(i => i.Properties.Count == 2
                    && i.Properties.Any(p => p.Name == "IdUser")
                    && i.Properties.Any(p => p.Name == "CreatedAt"));

            Assert.NotNull(compositeIndex);
        }

        [Fact]
        public void GivenDbContext_WhenInspected_ThenDefaultTrackingBehaviorIsNoTracking()
        {
            using var scope = _fixture.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            Assert.Equal(QueryTrackingBehavior.NoTracking, dbContext.ChangeTracker.QueryTrackingBehavior);
        }

        [Fact]
        public async Task GivenReadOnlyQuery_WhenExecuted_ThenEntitiesAreNotTracked()
        {
            using var scope = _fixture.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var loginData = await LoginAsync("admin@email.com", "admin@123");
            var userId = loginData.User.Id;

            var user = await dbContext.User.FirstOrDefaultAsync(u => u.Id == userId);

            Assert.NotNull(user);
            Assert.Equal(EntityState.Detached, dbContext.Entry(user!).State);
        }

        [Fact]
        public async Task GivenExplicitAsTracking_WhenExecuted_ThenEntitiesAreTracked()
        {
            using var scope = _fixture.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var loginData = await LoginAsync("admin@email.com", "admin@123");
            var userId = loginData.User.Id;

            var user = await dbContext.User.AsTracking().FirstOrDefaultAsync(u => u.Id == userId);

            Assert.NotNull(user);
            Assert.Equal(EntityState.Unchanged, dbContext.Entry(user!).State);
        }

        /*
         * Regressão: garantir que writes ainda funcionam após NoTracking default.
         * Se algum lugar precisar AsTracking() e o desenvolvedor esquecer, esse
         * teste pega — porque cobre a operação de update via API, que é o
         * caminho que os handlers usam.
         */
        [Fact]
        public async Task GivenUserUpdateViaApi_WhenExecuted_ThenChangesArePersisted()
        {
            var loginData = await LoginAsync("admin@email.com", "admin@123");
            SetAuthHeader(loginData.Token);

            var uniqueSuffix = Guid.NewGuid().ToString().Substring(0, 8);
            var createEmail = $"dbopt_{uniqueSuffix}@email.com";

            var createResp = await _client.PostAsJsonAsync("/v1/user", new
            {
                name = "DBOpt User",
                email = createEmail,
                password = "Password123!",
                id_role = "operator",
                active = true
            });
            Assert.Contains(createResp.StatusCode, new[] { System.Net.HttpStatusCode.Created, System.Net.HttpStatusCode.OK });
            var userData = await createResp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            var userId = userData.GetProperty("id").GetString()!;

            var newName = $"Updated {uniqueSuffix}";
            var updateResp = await _client.PutAsJsonAsync($"/v1/user/{userId}", new
            {
                name = newName
            });
            Assert.Equal(System.Net.HttpStatusCode.OK, updateResp.StatusCode);

            ClearAuthHeader();
            using var scope = _fixture.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await dbContext.User.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
            Assert.NotNull(user);
            Assert.Equal(newName, user!.Name);
        }
    }
}
