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
using FluentValidation;
using Serilog;

namespace MageBackend.Features.Auth
{
    [ApiController]
    [Route("v1/auth")]
    public class AuthController : BaseApiController
    {
        private readonly ApplicationDbContext _context;
        private readonly JwtProvider _jwtProvider;
        private readonly IValidator<LoginDto> _loginValidator;
        private readonly IValidator<RefreshDto> _refreshValidator;
        private readonly IValidator<ResetRequestDto> _resetRequestValidator;
        private readonly IValidator<ResetValidateDto> _resetValidateValidator;
        private readonly IValidator<ChangePasswordDto> _changePasswordValidator;

        public AuthController(
            ApplicationDbContext context,
            JwtProvider jwtProvider,
            IValidator<LoginDto> loginValidator,
            IValidator<RefreshDto> refreshValidator,
            IValidator<ResetRequestDto> resetRequestValidator,
            IValidator<ResetValidateDto> resetValidateValidator,
            IValidator<ChangePasswordDto> changePasswordValidator)
        {
            _context = context;
            _jwtProvider = jwtProvider;
            _loginValidator = loginValidator;
            _refreshValidator = refreshValidator;
            _resetRequestValidator = resetRequestValidator;
            _resetValidateValidator = resetValidateValidator;
            _changePasswordValidator = changePasswordValidator;
        }

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

        [HttpPost("login")]
        [ProducesResponseType(typeof(AuthResponseDto), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            var validationResult = await _loginValidator.ValidateAsync(dto);
            if (!validationResult.IsValid)
            {
                throw new FluentValidation.ValidationException(validationResult.Errors);
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
        [ProducesResponseType(typeof(AuthResponseDto), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> Refresh([FromBody] RefreshDto dto)
        {
            var validationResult = await _refreshValidator.ValidateAsync(dto);
            if (!validationResult.IsValid)
            {
                throw new FluentValidation.ValidationException(validationResult.Errors);
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
        [ProducesResponseType(typeof(AuthResponseDto), 200)]
        [ProducesResponseType(401)]
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
        [ProducesResponseType(200)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> RequestPasswordReset([FromBody] ResetRequestDto dto)
        {
            var validationResult = await _resetRequestValidator.ValidateAsync(dto);
            if (!validationResult.IsValid)
            {
                throw new FluentValidation.ValidationException(validationResult.Errors);
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
                Log.Information("[PasswordReset] Reset code for {Email}: {ResetToken}", dto.Email, resetToken);
            }

            return Ok(new { message = "E-mail de recuperação enviado com sucesso!" });
        }

        [HttpPost("password/validate")]
        [ProducesResponseType(200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> ValidateResetToken([FromBody] ResetValidateDto dto)
        {
            var validationResult = await _resetValidateValidator.ValidateAsync(dto);
            if (!validationResult.IsValid)
            {
                throw new FluentValidation.ValidationException(validationResult.Errors);
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
        [ProducesResponseType(200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
        {
            var validationResult = await _changePasswordValidator.ValidateAsync(dto);
            if (!validationResult.IsValid)
            {
                throw new FluentValidation.ValidationException(validationResult.Errors);
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
        [ProducesResponseType(200)]
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

    public class LoginDtoValidator : AbstractValidator<AuthController.LoginDto>
    {
        public LoginDtoValidator()
        {
            RuleFor(x => x.Email).NotEmpty().WithMessage("Email and password are required.");
            RuleFor(x => x.Password).NotEmpty().WithMessage("Email and password are required.");
        }
    }

    public class RefreshDtoValidator : AbstractValidator<AuthController.RefreshDto>
    {
        public RefreshDtoValidator()
        {
            RuleFor(x => x.RefreshToken).NotEmpty().WithMessage("RefreshToken is required.");
        }
    }

    public class ResetRequestDtoValidator : AbstractValidator<AuthController.ResetRequestDto>
    {
        public ResetRequestDtoValidator()
        {
            RuleFor(x => x.Email).NotEmpty().WithMessage("Email is required.");
        }
    }

    public class ResetValidateDtoValidator : AbstractValidator<AuthController.ResetValidateDto>
    {
        public ResetValidateDtoValidator()
        {
            RuleFor(x => x.Email).NotEmpty().WithMessage("Email and token are required.");
            RuleFor(x => x.Token).NotEmpty().WithMessage("Email and token are required.");
        }
    }

    public class ChangePasswordDtoValidator : AbstractValidator<AuthController.ChangePasswordDto>
    {
        public ChangePasswordDtoValidator()
        {
            RuleFor(x => x.Email).NotEmpty().WithMessage("Email, token, and password are required.");
            RuleFor(x => x.Token).NotEmpty().WithMessage("Email, token, and password are required.");
            RuleFor(x => x.Password).NotEmpty().WithMessage("Email, token, and password are required.");
        }
    }
}
