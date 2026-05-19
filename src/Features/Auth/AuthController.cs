using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using MageBackend.Database;
using MageBackend.Core;
using MageBackend.Core.Middleware;
using MageBackend.Core.Filters;
using MageBackend.Infrastructure.Auth;

namespace MageBackend.Features.Auth
{
    [ApiController]
    [Route("v1/auth")]
    public class AuthController : BaseApiController
    {
        private readonly ApplicationDbContext _context;
        private readonly JwtProvider _jwtProvider;

        public AuthController(ApplicationDbContext context, JwtProvider jwtProvider)
        {
            _context = context;
            _jwtProvider = jwtProvider;
        }

        public class LoginDto
        {
            public string Email { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
        }

        public class RefreshDto
        {
            public string RefreshToken { get; set; } = string.Empty;
        }

        public class ResetRequestDto
        {
            public string Email { get; set; } = string.Empty;
        }

        public class ResetValidateDto
        {
            public string Email { get; set; } = string.Empty;
            public string Token { get; set; } = string.Empty;
        }

        public class ChangePasswordDto
        {
            public string Email { get; set; } = string.Empty;
            public string Token { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
        }

        public class AuthUserRoleDto
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string? Description { get; set; }
            public List<PermissionClaim> Permissions { get; set; } = new();
        }

        public class AuthUserDto
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public AuthUserRoleDto? Role { get; set; }
        }

        public class AuthResponseDto
        {
            public string Token { get; set; } = string.Empty;
            public string RefreshToken { get; set; } = string.Empty;
            public AuthUserDto User { get; set; } = new();
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            if (string.IsNullOrEmpty(dto.Email) || string.IsNullOrEmpty(dto.Password))
            {
                return BadRequest(new { message = "Email and password are required." });
            }

            var user = await _context.User
                .Include(u => u.Auth)
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Email == dto.Email && !u.IsDeleted);

            if (user == null || user.Auth == null || user.Role == null || !user.Active || user.Auth.IsDeleted || !user.Auth.Active || !user.Role.Active)
            {
                return StatusCode(401, new { error = "UnauthorizedError", message = "User not found or account is disabled/removed" });
            }

            var passwordMatches = BCrypt.Net.BCrypt.Verify(dto.Password, user.Auth.Password);
            if (!passwordMatches)
            {
                return StatusCode(401, new { error = "UnauthorizedError", message = "Invalid email or password" });
            }

            return await GenerateAuthResponse(user);
        }

        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh([FromBody] RefreshDto dto)
        {
            if (string.IsNullOrEmpty(dto.RefreshToken))
            {
                return BadRequest(new { message = "RefreshToken is required." });
            }

            try
            {
                var payload = _jwtProvider.VerifyToken(dto.RefreshToken);

                using var sha256 = SHA256.Create();
                var refreshBytes = Encoding.UTF8.GetBytes(dto.RefreshToken);
                var refreshHashBytes = sha256.ComputeHash(refreshBytes);
                var refreshTokenHash = Convert.ToHexString(refreshHashBytes).ToLower();

                var refreshKey = $"session:user:{payload.Id}:refresh:{refreshTokenHash}";
                var redisDb = RedisProvider.Database;

                var isValid = await redisDb.KeyExistsAsync(refreshKey);
                if (!isValid)
                {
                    return StatusCode(401, new { error = "UnauthorizedError", message = "Sessão encerrada. Por favor, faça login novamente." });
                }

                var user = await _context.User
                    .Include(u => u.Auth)
                    .Include(u => u.Role)
                    .FirstOrDefaultAsync(u => u.Id == payload.Id && !u.IsDeleted);

                if (user == null || user.Auth == null || user.Role == null || !user.Active || user.Auth.IsDeleted || !user.Auth.Active || !user.Role.Active)
                {
                    return StatusCode(401, new { error = "UnauthorizedError", message = "User not found or account is disabled/removed" });
                }

                // Delete the old refresh token session
                await redisDb.KeyDeleteAsync(refreshKey);

                return await GenerateAuthResponse(user);
            }
            catch
            {
                return StatusCode(401, new { error = "UnauthorizedError", message = "Invalid or expired refresh token" });
            }
        }

        [HttpGet("me")]
        public async Task<IActionResult> GetMe()
        {
            var userId = User.FindFirst("id")?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return StatusCode(401, new { error = "UnauthorizedError", message = "Usuário não autenticado" });
            }

            var user = await _context.User
                .Include(u => u.Auth)
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted);

            if (user == null || user.Auth == null || !user.Active || user.Auth.IsDeleted || !user.Auth.Active)
            {
                return Unauthorized(new { message = "User not found or account is disabled/removed" });
            }

            var permissions = await GetUserPermissions(user.IdRole);
            var response = new AuthResponseDto
            {
                Token = Request.Headers["Authorization"].ToString().Replace("Bearer ", ""),
                RefreshToken = "", // No refresh token returned on getMe
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

            return Ok(response);
        }

        [HttpPost("password/request")]
        public async Task<IActionResult> RequestPasswordReset([FromBody] ResetRequestDto dto)
        {
            if (string.IsNullOrEmpty(dto.Email))
            {
                return BadRequest(new { message = "Email is required." });
            }

            var user = await _context.User
                .Include(u => u.Auth)
                .FirstOrDefaultAsync(u => u.Email == dto.Email && !u.IsDeleted);

            if (user != null && user.Auth != null)
            {
                var random = new Random();
                var resetToken = random.Next(100000, 999999).ToString();
                var expiration = DateTime.UtcNow.AddMinutes(15);

                user.Auth.RequestPasswordToken = resetToken;
                user.Auth.RequestPasswordExpiration = expiration;
                user.Auth.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                Console.WriteLine($"[PasswordReset] Reset code for {dto.Email}: {resetToken}");
            }

            return Ok(new { message = "E-mail de recuperação enviado com sucesso!" });
        }

        [HttpPost("password/validate")]
        public async Task<IActionResult> ValidateResetToken([FromBody] ResetValidateDto dto)
        {
            if (string.IsNullOrEmpty(dto.Email) || string.IsNullOrEmpty(dto.Token))
            {
                return BadRequest(new { message = "Email and token are required." });
            }

            var user = await _context.User
                .Include(u => u.Auth)
                .FirstOrDefaultAsync(u => u.Email == dto.Email && !u.IsDeleted);

            if (user == null || user.Auth == null)
            {
                return NotFound(new { message = "User not found" });
            }

            if (user.Auth.RequestPasswordToken != dto.Token)
            {
                return Unauthorized(new { message = "Invalid reset token" });
            }

            if (user.Auth.RequestPasswordExpiration.HasValue && user.Auth.RequestPasswordExpiration < DateTime.UtcNow)
            {
                return Unauthorized(new { message = "Reset token has expired" });
            }

            return Ok(new { valid = true });
        }

        [HttpPost("password/change")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
        {
            if (string.IsNullOrEmpty(dto.Email) || string.IsNullOrEmpty(dto.Token) || string.IsNullOrEmpty(dto.Password))
            {
                return BadRequest(new { message = "Email, token, and password are required." });
            }

            var user = await _context.User
                .Include(u => u.Auth)
                .FirstOrDefaultAsync(u => u.Email == dto.Email && !u.IsDeleted);

            if (user == null || user.Auth == null)
            {
                return NotFound(new { message = "User not found" });
            }

            if (user.Auth.RequestPasswordToken != dto.Token)
            {
                return Unauthorized(new { message = "Invalid reset token" });
            }

            if (user.Auth.RequestPasswordExpiration.HasValue && user.Auth.RequestPasswordExpiration < DateTime.UtcNow)
            {
                return Unauthorized(new { message = "Reset token has expired" });
            }

            var hashedPassword = BCrypt.Net.BCrypt.HashPassword(dto.Password, 12);
            user.Auth.Password = hashedPassword;
            user.Auth.RequestPasswordToken = null;
            user.Auth.RequestPasswordExpiration = null;
            user.Auth.Retries = 0;
            user.Auth.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return Ok(new { message = "Senha alterada com sucesso!" });
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            var userId = User.FindFirst("id")?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                await SessionManager.InvalidateUserSessionsAsync(userId);
            }

            return Ok(new { message = "Logout realizado com sucesso!" });
        }

        private async Task<List<PermissionClaim>> GetUserPermissions(string roleId)
        {
            return await _context.RoleFeature
                .Where(rf => rf.IdRole == roleId)
                .Select(rf => new PermissionClaim
                {
                    Feature = rf.IdFeature,
                    Create = rf.Create,
                    View = rf.View,
                    Delete = rf.Delete,
                    Activate = rf.Activate
                }).ToListAsync();
        }

        private async Task<IActionResult> GenerateAuthResponse(Database.User user)
        {
            var permissions = await GetUserPermissions(user.IdRole);

            var payload = new AuthPayload
            {
                Id = user.Id,
                Email = user.Email,
                RoleId = user.IdRole,
                Permissions = permissions
            };

            var tokens = _jwtProvider.GenerateTokenPair(payload);

            using var sha256 = SHA256.Create();

            var tokenBytes = Encoding.UTF8.GetBytes(tokens.Token);
            var tokenHashBytes = sha256.ComputeHash(tokenBytes);
            var tokenHash = Convert.ToHexString(tokenHashBytes).ToLower();

            var refreshBytes = Encoding.UTF8.GetBytes(tokens.RefreshToken);
            var refreshHashBytes = sha256.ComputeHash(refreshBytes);
            var refreshTokenHash = Convert.ToHexString(refreshHashBytes).ToLower();

            var redisDb = RedisProvider.Database;

            // session:user:{id}:access:{tokenHash} -> payload (24h)
            // session:user:{id}:refresh:{refreshTokenHash} -> "1" (7d)
            await redisDb.StringSetAsync($"session:user:{user.Id}:access:{tokenHash}", JsonSerializer.Serialize(payload), TimeSpan.FromDays(1));
            await redisDb.StringSetAsync($"session:user:{user.Id}:refresh:{refreshTokenHash}", "1", TimeSpan.FromDays(7));

            var response = new AuthResponseDto
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

            return Ok(response);
        }
    }
}
