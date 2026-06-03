using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MageBackend.Features.Role
{
    public record RoleFeatureDto
    {
        [JsonPropertyName("id_feature")]
        public string IdFeature { get; init; } = string.Empty;
        public required bool Create { get; init; }
        public required bool View { get; init; }
        public required bool Delete { get; init; }
        public required bool Activate { get; init; }
    }

    public record RoleResponseDto
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string? Description { get; init; }
        public bool Active { get; init; }
        [JsonPropertyName("is_deleted")]
        public bool IsDeleted { get; init; }
        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; init; }
        [JsonPropertyName("updated_at")]
        public DateTime UpdatedAt { get; init; }
        [JsonPropertyName("deleted_at")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public DateTime? DeletedAt { get; init; }
        [JsonPropertyName("RoleFeature")]
        public List<RoleFeatureDto> RoleFeature { get; init; } = new();
    }

    public record CreateRoleDto
    {
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public List<RoleFeatureDto>? Permissions { get; init; }
    }

    public static class RoleMapper
    {
        public static RoleResponseDto MapToDto(Database.Role role, List<RoleFeatureDto> features)
        {
            return new RoleResponseDto
            {
                Id = role.Id,
                Name = role.Name,
                Description = role.Description,
                Active = role.Active,
                IsDeleted = role.IsDeleted,
                CreatedAt = role.CreatedAt,
                UpdatedAt = role.UpdatedAt,
                DeletedAt = role.DeletedAt,
                RoleFeature = features
            };
        }
    }
}
