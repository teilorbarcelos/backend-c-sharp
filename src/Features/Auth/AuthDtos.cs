using System.Text.Json.Serialization;
using MageBackend.Infrastructure.Auth;

namespace MageBackend.Features.Auth
{
    public record LoginDto
    {
        public string Email { get; init; } = string.Empty;
        public string Password { get; init; } = string.Empty;
    }

    public record RefreshDto
    {
        public string RefreshToken { get; init; } = string.Empty;
    }

    public record ResetRequestDto
    {
        public string Email { get; init; } = string.Empty;
    }

    public record ResetValidateDto
    {
        public string Email { get; init; } = string.Empty;
        public string Token { get; init; } = string.Empty;
    }

    public record ChangePasswordDto
    {
        public string Email { get; init; } = string.Empty;
        public string Token { get; init; } = string.Empty;
        public string Password { get; init; } = string.Empty;
    }

    public record AuthUserRoleDto
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string? Description { get; init; }
        public List<PermissionClaim> Permissions { get; init; } = new();
    }

    public record AuthUserDto
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public AuthUserRoleDto Role { get; init; } = new();
    }

    public record AuthResponseDto
    {
        public string Token { get; init; } = string.Empty;
        public string RefreshToken { get; init; } = string.Empty;
        public AuthUserDto User { get; init; } = new();
    }
}
