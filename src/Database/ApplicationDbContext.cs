using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace MageBackend.Database
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        public DbSet<User> User { get; set; } = null!;
        public DbSet<Auth> Auth { get; set; } = null!;
        public DbSet<Role> Role { get; set; } = null!;
        public DbSet<Feature> Feature { get; set; } = null!;
        public DbSet<RoleFeature> RoleFeature { get; set; } = null!;
        public DbSet<Product> Product { get; set; } = null!;
        public DbSet<Audit> Audit { get; set; } = null!;
        public DbSet<ErrorLog> ErrorLog { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            /* Configure table names and schemas */
            modelBuilder.Entity<User>().ToTable("User");
            modelBuilder.Entity<Auth>().ToTable("Auth");
            modelBuilder.Entity<Role>().ToTable("Role");
            modelBuilder.Entity<Feature>().ToTable("Feature");
            modelBuilder.Entity<RoleFeature>().ToTable("RoleFeature");
            modelBuilder.Entity<Product>().ToTable("Product");

            /* Audit tables map to "audit" schema */
            modelBuilder.Entity<Audit>().ToTable("tb_audit", "audit");
            modelBuilder.Entity<ErrorLog>().ToTable("tb_error_log", "audit");

            /* Configure composite key for RoleFeature */
            modelBuilder.Entity<RoleFeature>()
                .HasKey(rf => new { rf.IdRole, rf.IdFeature });

            /* Configure decimal precision */
            modelBuilder.Entity<Product>()
                .Property(p => p.Price)
                .HasColumnType("decimal(18,2)");

            /* Apply snake_case naming convention to all column names */
            foreach (var entity in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entity.GetProperties())
                {
                    property.SetColumnName(ConvertToSnakeCase(property.Name));
                }
            }
        }

        private static string ConvertToSnakeCase(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            var result = Regex.Replace(input, "([a-z0-9])([A-Z])", "$1_$2").ToLower();
            return result;
        }
    }
}
