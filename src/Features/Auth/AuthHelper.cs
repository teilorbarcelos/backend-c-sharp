using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MageBackend.Database;
using MageBackend.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;

namespace MageBackend.Features.Auth
{
    public static class AuthHelper
    {
        public static async Task<AuthResponseDto> GenerateAuthResponse(Database.User user, ApplicationDbContext context, JwtProvider jwtProvider)
        {
            var permissions = await context.RoleFeature
                .Where(rf => rf.IdRole == user.IdRole)
                .Select(rf => new PermissionClaim
                {
                    Feature = rf.IdFeature,
                    Create = rf.Create,
                    View = rf.View,
                    Delete = rf.Delete,
                    Activate = rf.Activate
                }).ToListAsync();

            var payload = new AuthPayload
            {
                Id = user.Id,
                Email = user.Email,
                RoleId = user.IdRole,
                Permissions = permissions
            };

            var tokens = jwtProvider.GenerateTokenPair(payload);

            var tokenBytes = Encoding.UTF8.GetBytes(tokens.Token);
            var tokenHashBytes = SHA256.HashData(tokenBytes);
            var tokenHash = Convert.ToHexString(tokenHashBytes).ToLower();

            var refreshBytes = Encoding.UTF8.GetBytes(tokens.RefreshToken);
            var refreshHashBytes = SHA256.HashData(refreshBytes);
            var refreshTokenHash = Convert.ToHexString(refreshHashBytes).ToLower();

            var redisDb = RedisProvider.Database;

            /* session:user:{id}:access:{tokenHash} -> payload (24h) */
            /* session:user:{id}:refresh:{refreshTokenHash} -> "1" (7d) */
            await redisDb.StringSetAsync($"session:user:{user.Id}:access:{tokenHash}", JsonSerializer.Serialize(payload), TimeSpan.FromDays(1));
            await redisDb.StringSetAsync($"session:user:{user.Id}:refresh:{refreshTokenHash}", "1", TimeSpan.FromDays(7));

            return new AuthResponseDto
            {
                Token = tokens.Token,
                RefreshToken = tokens.RefreshToken,
                User = new AuthUserDto
                {
                    Id = user.Id,
                    Name = user.Name,
                    Email = user.Email,
                    Role = new AuthUserRoleDto
                    {
                        Id = user.IdRole,
                        Name = user.Role?.Name ?? "",
                        Description = user.Role?.Description,
                        Permissions = permissions
                    }
                }
            };
        }
    }
}
