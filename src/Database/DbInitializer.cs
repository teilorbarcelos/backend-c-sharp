using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using Serilog;

namespace MageBackend.Database
{
    public static class DbInitializer
    {
        public static async Task InitializeAsync(ApplicationDbContext context)
        {
            const string ManagerRoleId = "manager";
            const string OperatorRoleId = "operator";

            /* Apply migrations automatically */
            await context.Database.MigrateAsync();

            /* 1. Seed Features */
            if (!await context.Feature.AnyAsync())
            {
                var features = new[]
                {
                    new Feature { Id = "dashboard", Name = "Dashboard", Description = "Visualizar indicadores e métricas do sistema" },
                    new Feature { Id = "user", Name = "Usuários", Description = "Gerenciar usuários e acessos" },
                    new Feature { Id = "role", Name = "Perfis de Acesso", Description = "Gerenciar cargos e permissões" },
                    new Feature { Id = "product", Name = "Produtos", Description = "Gerenciar catálogo de produtos" },
                    new Feature { Id = "storage", Name = "Arquivos", Description = "Gerenciar upload de arquivos e mídias" },
                };

                await context.Feature.AddRangeAsync(features);
                await context.SaveChangesAsync();
            }

            /* 2. Seed Roles & RoleFeatures */
            if (!await context.Role.AnyAsync())
            {
                var admin = new Role { Id = "administrator", Name = "Administrador", Description = "Acesso total ao sistema" };
                var manager = new Role { Id = ManagerRoleId, Name = "Gerente", Description = "Gerente operacional" };
                var operatorRole = new Role { Id = OperatorRoleId, Name = "Operador", Description = "Operador de sistema" };

                await context.Role.AddRangeAsync(admin, manager, operatorRole);
                await context.SaveChangesAsync();

                /* Seed RoleFeatures for Admin */
                var excludedFeatures = new[] { "feature", "audit", "errorlog", "error" };
                var allFeatures = await context.Feature
                    .Where(f => !excludedFeatures.Contains(f.Id))
                    .Select(f => f.Id)
                    .ToListAsync();
                var adminFeatures = allFeatures
                    .Select(f => new RoleFeature { IdRole = "administrator", IdFeature = f, Create = true, View = true, Activate = true, Delete = true });

                /* Seed RoleFeatures for Manager */
                var managerFeatures = new[]
                {
                    new RoleFeature { IdRole = ManagerRoleId, IdFeature = "dashboard", Create = true, View = true, Activate = true, Delete = true },
                    new RoleFeature { IdRole = ManagerRoleId, IdFeature = "user", Create = true, View = true, Activate = false, Delete = false },
                    new RoleFeature { IdRole = ManagerRoleId, IdFeature = "role", Create = false, View = true, Activate = false, Delete = false },
                    new RoleFeature { IdRole = ManagerRoleId, IdFeature = "product", Create = true, View = true, Activate = true, Delete = true }
                };

                /* Seed RoleFeatures for Operator */
                var operatorFeatures = new[]
                {
                    new RoleFeature { IdRole = OperatorRoleId, IdFeature = "dashboard", Create = true, View = true, Activate = true, Delete = true },
                    new RoleFeature { IdRole = OperatorRoleId, IdFeature = "user", Create = false, View = false, Activate = false, Delete = false },
                    new RoleFeature { IdRole = OperatorRoleId, IdFeature = "role", Create = false, View = false, Activate = false, Delete = false },
                    new RoleFeature { IdRole = OperatorRoleId, IdFeature = "product", Create = false, View = true, Activate = false, Delete = false }
                };

                await context.RoleFeature.AddRangeAsync(adminFeatures);
                await context.RoleFeature.AddRangeAsync(managerFeatures);
                await context.RoleFeature.AddRangeAsync(operatorFeatures);
                await context.SaveChangesAsync();
            }

            /* 3. Seed First User (Admin) */
            var adminEmail = Environment.GetEnvironmentVariable("FIRST_USER") ?? "admin@email.com";
            var adminPassword = Environment.GetEnvironmentVariable("FIRST_PASSWORD") ?? "admin@123";

            var existingUser = await context.User.FirstOrDefaultAsync(u => u.Email == adminEmail);
            if (existingUser == null)
            {
                var hashedPassword = BCrypt.Net.BCrypt.HashPassword(adminPassword, 12);

                var auth = new Auth
                {
                    Id = Guid.NewGuid().ToString(),
                    Password = hashedPassword,
                    FirstAccess = false,
                    Active = true
                };

                var user = new User
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "Administrator",
                    Email = adminEmail,
                    Active = true,
                    IdRole = "administrator",
                    IdAuth = auth.Id
                };

                await context.Auth.AddAsync(auth);
                await context.User.AddAsync(user);
                await context.SaveChangesAsync();

                Log.Information("[DbInitializer] Seeded initial admin account: {Email}", adminEmail);
            }
        }
    }
}
