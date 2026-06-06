using System;
using System.Text.Json.Serialization;

namespace MageBackend.Features.User
{
    public record UserResponseDto
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public string? Phone { get; init; }
        public string? Document { get; init; }
        public string? Avatar { get; init; }
        [JsonPropertyName("id_role")]
        public string IdRole { get; init; } = string.Empty;
        public bool Active { get; init; }
        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; init; }
        [JsonPropertyName("updated_at")]
        public DateTime UpdatedAt { get; init; }
    }

    public static class UserMapper
    {
        public static UserResponseDto MapToDto(Database.User user)
        {
            return new UserResponseDto
            {
                Id = user.Id,
                Name = user.Name,
                Email = user.Email,
                Phone = user.Phone,
                Document = user.Document,
                Avatar = user.Avatar,
                IdRole = user.IdRole,
                Active = user.Active,
                CreatedAt = user.CreatedAt,
                UpdatedAt = user.UpdatedAt
            };
        }
    }

    public class UserEntityMapper : MageBackend.Domain.IEntityMapper<Database.User, UserResponseDto>
    {
        public UserResponseDto MapToDto(Database.User entity)
        {
            return UserMapper.MapToDto(entity);
        }
    }
}
