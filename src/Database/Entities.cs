using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MageBackend.Database
{
    public class User
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string Email { get; set; } = string.Empty;
        public string? CognitoId { get; set; }
        public bool Active { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public string? Document { get; set; }
        public bool IsDeleted { get; set; } = false;
        public DateTime? DeletedAt { get; set; }
        public string? Avatar { get; set; }
        
        public string? IdAuth { get; set; }
        [ForeignKey("IdAuth")]
        public virtual Auth? Auth { get; set; }
        
        public string IdRole { get; set; } = string.Empty;
        [ForeignKey("IdRole")]
        public virtual Role? Role { get; set; }
    }

    public class Auth
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Password { get; set; } = string.Empty;
        public string? RequestPasswordToken { get; set; }
        public DateTime? RequestPasswordExpiration { get; set; }
        public int Retries { get; set; } = 0;
        public bool FirstAccess { get; set; } = true;
        public bool Active { get; set; } = true;
        public bool IsDeleted { get; set; } = false;
        public DateTime? DeletedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class Role
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool Active { get; set; } = true;
        public bool IsDeleted { get; set; } = false;
        public DateTime? DeletedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class Feature
    {
        [Key]
        public string Id { get; set; } = string.Empty; // e.g. "product", "user", "role", "feature"
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool Active { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class RoleFeature
    {
        public string IdRole { get; set; } = string.Empty;
        [ForeignKey("IdRole")]
        public virtual Role? Role { get; set; }

        public string IdFeature { get; set; } = string.Empty;
        [ForeignKey("IdFeature")]
        public virtual Feature? Feature { get; set; }

        public bool Create { get; set; } = false;
        public bool View { get; set; } = false;
        public bool Activate { get; set; } = false;
        public bool Delete { get; set; } = false;
    }

    public class Product
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string Sku { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int Stock { get; set; }
        public string? Description { get; set; }
        public bool Active { get; set; } = true;
        public bool IsDeleted { get; set; } = false;
        public DateTime? DeletedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class Audit
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string? IdUser { get; set; }
        public string? UserName { get; set; }
        public string? ActionType { get; set; }
        public string? ExecuteType { get; set; }
        public string? Class { get; set; }
        public string? Function { get; set; }
        public string? Params { get; set; }
        public string? Raw { get; set; }
        public string? TableName { get; set; }
        public string? DiffValue { get; set; }
        public string? Error { get; set; }
        public string? Host { get; set; }
        public string? Ip { get; set; }
        public string? BaseUrl { get; set; }
        public string? Method { get; set; }
        public string? Hostname { get; set; }
        public string? OriginalUrl { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class ErrorLog
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string? IdUser { get; set; }
        public string? Source { get; set; }
        public string? ErrorMessage { get; set; }
        public string? ErrorData { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
