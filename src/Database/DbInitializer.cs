using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace MageBackend.Database
{
    public static class DbInitializer
    {
        public static async Task InitializeAsync(ApplicationDbContext context)
        {
            // Apply migrations automatically
            await context.Database.EnsureCreatedAsync();

            // 1. Seed Features
            if (!await context.Feature.AnyAsync())
            {
                var features = new[]
                {
                    new Feature { Id = "dashboard", Name = "Dashboard", Description = "Visualizar indicadores e métricas do sistema" },
                    new Feature { Id = "user", Name = "Usuários", Description = "Gerenciar usuários e acessos" },
                    new Feature { Id = "role", Name = "Perfis de Acesso", Description = "Gerenciar cargos e permissões" },
                    new Feature { Id = "product", Name = "Produtos", Description = "Gerenciar catálogo de produtos" }
                };

                await context.Feature.AddRangeAsync(features);
                await context.SaveChangesAsync();
            }

            // 2. Seed Roles & RoleFeatures
            if (!await context.Role.AnyAsync())
            {
                var admin = new Role { Id = "administrator", Name = "Administrador", Description = "Acesso total ao sistema" };
                var manager = new Role { Id = "manager", Name = "Gerente", Description = "Gerente operacional" };
                var operatorRole = new Role { Id = "operator", Name = "Operador", Description = "Operador de sistema" };

                await context.Role.AddRangeAsync(admin, manager, operatorRole);
                await context.SaveChangesAsync();

                // Seed RoleFeatures for Admin
                var adminFeatures = new[] { "dashboard", "user", "role", "product" }
                    .Select(f => new RoleFeature { IdRole = "administrator", IdFeature = f, Create = true, View = true, Activate = true, Delete = true });

                // Seed RoleFeatures for Manager
                var managerFeatures = new[]
                {
                    new RoleFeature { IdRole = "manager", IdFeature = "dashboard", Create = true, View = true, Activate = true, Delete = true },
                    new RoleFeature { IdRole = "manager", IdFeature = "user", Create = true, View = true, Activate = false, Delete = false },
                    new RoleFeature { IdRole = "manager", IdFeature = "role", Create = false, View = true, Activate = false, Delete = false },
                    new RoleFeature { IdRole = "manager", IdFeature = "product", Create = true, View = true, Activate = true, Delete = true }
                };

                // Seed RoleFeatures for Operator
                var operatorFeatures = new[]
                {
                    new RoleFeature { IdRole = "operator", IdFeature = "dashboard", Create = true, View = true, Activate = true, Delete = true },
                    new RoleFeature { IdRole = "operator", IdFeature = "user", Create = false, View = false, Activate = false, Delete = false },
                    new RoleFeature { IdRole = "operator", IdFeature = "role", Create = false, View = false, Activate = false, Delete = false },
                    new RoleFeature { IdRole = "operator", IdFeature = "product", Create = false, View = true, Activate = false, Delete = false }
                };

                await context.RoleFeature.AddRangeAsync(adminFeatures);
                await context.RoleFeature.AddRangeAsync(managerFeatures);
                await context.RoleFeature.AddRangeAsync(operatorFeatures);
                await context.SaveChangesAsync();
            }

            // 3. Seed First User (Admin)
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
                
                Console.WriteLine($"[DbInitializer] Seeded initial admin account: {adminEmail}");
            }
        }
    }
}
