using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using MageBackend.Database;
using MageBackend.Core;
using MageBackend.Core.Middleware;
using MageBackend.Core.Filters;
using MageBackend.Infrastructure.Auth;
using FluentValidation;

namespace MageBackend.Features.User
{
    [ApiController]
    [Route("v1/user")]
    public class UserController : BaseApiController
    {
        private readonly ApplicationDbContext _context;
        private readonly IValidator<CreateUserDto> _createUserValidator;
        private readonly IValidator<UpdateUserDto> _updateUserValidator;
        private static readonly string[] AllowedFields = { "name", "email", "active", "created_at", "updated_at", "Role.name" };

        public UserController(
            ApplicationDbContext context,
            IValidator<CreateUserDto> createUserValidator,
            IValidator<UpdateUserDto> updateUserValidator)
        {
            _context = context;
            _createUserValidator = createUserValidator;
            _updateUserValidator = updateUserValidator;
        }

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

        [HttpGet]
        [CheckPermission("user", "view")]
        [ProducesResponseType(typeof(SearchResult<UserResponseDto>), 200)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> List()
        {
            var req = SearchRequest.Parse(Request.Query, AllowedFields, out var err);
            if (err != null) return ErrorResponse(err);

            var result = await _context.User
                .Include(u => u.Role)
                .Where(u => !u.IsDeleted)
                .ApplyActiveFilter(req.Active, forceDefaultTrue: true)
                .ExecuteSearchAsync(req, MapToDto);

            return Ok(result);
        }

        [HttpGet("all")]
        [CheckPermission("user", "view")]
        [ProducesResponseType(typeof(SearchResult<UserResponseDto>), 200)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> ListAll()
        {
            var req = SearchRequest.Parse(Request.Query, AllowedFields, out var err);
            if (err != null) return ErrorResponse(err);

            var result = await _context.User
                .Include(u => u.Role)
                .Where(u => !u.IsDeleted)
                .ApplyActiveFilter(req.Active)
                .ExecuteSearchAsync(req, MapToDto);

            return Ok(result);
        }

        [HttpGet("{id}")]
        [CheckPermission("user", "view")]
        [ProducesResponseType(typeof(UserResponseDto), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetById(string id)
        {
            var user = await _context.User.FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted);
            if (user == null) return NotFound(new { message = "User not found" });

            return Ok(MapToDto(user));
        }

        public record CreateUserDto
        {
            public string Name { get; init; } = string.Empty;
            public string Email { get; init; } = string.Empty;
            public string Password { get; init; } = string.Empty;
            public string? Phone { get; init; }
            public string? Document { get; init; }
            public string? Avatar { get; init; }
            [JsonPropertyName("id_role")]
            public string IdRole { get; init; } = string.Empty;
        }

        [HttpPost]
        [CheckPermission("user", "create")]
        [ProducesResponseType(typeof(UserResponseDto), 201)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> Create([FromBody] CreateUserDto dto)
        {
            var validationResult = await _createUserValidator.ValidateAsync(dto);
            if (!validationResult.IsValid)
            {
                return BadRequest(new { message = validationResult.Errors.First().ErrorMessage });
            }

            var emailExists = await _context.User.AnyAsync(u => u.Email == dto.Email && !u.IsDeleted);
            if (emailExists)
            {
                return BadRequest(new { message = "Email already in use." });
            }

            var roleExists = await _context.Role.AnyAsync(r => r.Id == dto.IdRole && !r.IsDeleted);
            if (!roleExists)
            {
                return BadRequest(new { message = "Role not found." });
            }

            var hashedPassword = BCrypt.Net.BCrypt.HashPassword(dto.Password, 10);
            var auth = new Database.Auth
            {
                Id = Guid.NewGuid().ToString(),
                Password = hashedPassword,
                Active = true,
                FirstAccess = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var user = new Database.User
            {
                Id = Guid.NewGuid().ToString(),
                Name = dto.Name,
                Email = dto.Email,
                Phone = dto.Phone,
                Document = dto.Document,
                Avatar = dto.Avatar,
                Active = true,
                IdRole = dto.IdRole,
                IdAuth = auth.Id,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Auth.Add(auth);
            _context.User.Add(user);
            await _context.SaveChangesAsync();

            return StatusCode(201, MapToDto(user));
        }

        public record UpdateUserDto
        {
            public string? Name { get; init; }
            public string? Email { get; init; }
            public string? Password { get; init; }
            public string? Phone { get; init; }
            public string? Document { get; init; }
            public string? Avatar { get; init; }
            [JsonPropertyName("id_role")]
            public string? IdRole { get; init; }
            public bool? Active { get; init; }
        }

        [HttpPut("{id}")]
        [CheckPermission("user", "create")]
        [ProducesResponseType(typeof(UserResponseDto), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> Update(string id, [FromBody] UpdateUserDto dto)
        {
            var validationResult = await _updateUserValidator.ValidateAsync(dto);
            if (!validationResult.IsValid)
            {
                return BadRequest(new { message = validationResult.Errors.First().ErrorMessage });
            }

            var user = await _context.User.Include(u => u.Auth).FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted);
            if (user == null) return NotFound(new { message = "User not found" });

            var adminEmail = Environment.GetEnvironmentVariable("FIRST_USER") ?? "admin@email.com";

            // Root admin protection
            if (user.Email == adminEmail)
            {
                if (!string.IsNullOrEmpty(dto.Password))
                {
                    var hashedPassword = BCrypt.Net.BCrypt.HashPassword(dto.Password, 12);
                    if (user.Auth != null)
                    {
                        user.Auth.Password = hashedPassword;
                        user.Auth.UpdatedAt = DateTime.UtcNow;
                    }
                }
                user.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                await SessionManager.InvalidateUserSessionsAsync(id);
                return Ok(MapToDto(user));
            }

            if (dto.Name != null) user.Name = dto.Name;
            if (dto.Email != null)
            {
                var emailExists = await _context.User.AnyAsync(u => u.Email == dto.Email && u.Id != id && !u.IsDeleted);
                if (emailExists) return BadRequest(new { message = "Email already in use." });
                user.Email = dto.Email;
            }
            if (dto.Phone != null) user.Phone = dto.Phone;
            if (dto.Document != null) user.Document = dto.Document;
            if (dto.Avatar != null) user.Avatar = dto.Avatar;
            if (dto.Active.HasValue) user.Active = dto.Active.Value;

            if (!string.IsNullOrEmpty(dto.IdRole))
            {
                var roleExists = await _context.Role.AnyAsync(r => r.Id == dto.IdRole && !r.IsDeleted);
                if (!roleExists) return BadRequest(new { message = "Role not found." });
                user.IdRole = dto.IdRole;
            }

            if (!string.IsNullOrEmpty(dto.Password))
            {
                var hashedPassword = BCrypt.Net.BCrypt.HashPassword(dto.Password, 12);
                if (user.Auth != null)
                {
                    user.Auth.Password = hashedPassword;
                    user.Auth.UpdatedAt = DateTime.UtcNow;
                }
            }

            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            await SessionManager.InvalidateUserSessionsAsync(id);

            return Ok(MapToDto(user));
        }

        [HttpDelete("{id}")]
        [CheckPermission("user", "delete")]
        [ProducesResponseType(204)]
        [ProducesResponseType(400)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> Delete(string id)
        {
            var user = await _context.User.FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted);
            if (user == null) return NotFound(new { message = "User not found" });

            var adminEmail = Environment.GetEnvironmentVariable("FIRST_USER") ?? "admin@email.com";
            if (user.Email == adminEmail)
            {
                return BadRequest(new { message = "O usuário administrador inicial não pode ser excluído." });
            }

            // LGPD Anonymization
            user.Name = "Deleted User";
            user.Email = $"deleted-{id}@anonymized.local";
            user.Phone = null;
            user.Document = null;
            user.Avatar = null;
            user.IsDeleted = true;
            user.DeletedAt = DateTime.UtcNow;
            user.Active = false;
            user.UpdatedAt = DateTime.UtcNow;

            if (!string.IsNullOrEmpty(user.IdAuth))
            {
                var auth = await _context.Auth.FindAsync(user.IdAuth);
                if (auth != null)
                {
                    auth.IsDeleted = true;
                    auth.DeletedAt = DateTime.UtcNow;
                    auth.Active = false;
                    auth.UpdatedAt = DateTime.UtcNow;
                }
            }

            await _context.SaveChangesAsync();
            await SessionManager.InvalidateUserSessionsAsync(id);

            return NoContent();
        }

        public record ToggleStatusDto
        {
            public bool Active { get; init; }
        }

        [HttpPatch("{id}/status")]
        [CheckPermission("user", "activate")]
        [ProducesResponseType(typeof(UserResponseDto), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> ToggleStatus(string id, [FromBody] ToggleStatusDto dto)
        {
            var user = await _context.User.FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted);
            if (user == null) return NotFound(new { message = "User not found" });

            var adminEmail = Environment.GetEnvironmentVariable("FIRST_USER") ?? "admin@email.com";
            if (user.Email == adminEmail && !dto.Active)
            {
                return BadRequest(new { message = "O usuário administrador inicial não pode ser desativado." });
            }

            user.Active = dto.Active;
            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await SessionManager.InvalidateUserSessionsAsync(id);

            return Ok(MapToDto(user));
        }

        private static UserResponseDto MapToDto(Database.User user)
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

    public class CreateUserDtoValidator : AbstractValidator<UserController.CreateUserDto>
    {
        public CreateUserDtoValidator()
        {
            RuleFor(x => x.Name).NotEmpty().WithMessage("Name, email, password, and id_role are required.");
            RuleFor(x => x.Email).NotEmpty().WithMessage("Name, email, password, and id_role are required.");
            RuleFor(x => x.Password).NotEmpty().WithMessage("Name, email, password, and id_role are required.");
            RuleFor(x => x.IdRole).NotEmpty().WithMessage("Name, email, password, and id_role are required.");

            RuleFor(x => x.Password)
                .MinimumLength(6)
                .When(x => !string.IsNullOrEmpty(x.Password))
                .WithMessage("Password must be at least 6 characters long.");
        }
    }

    public class UpdateUserDtoValidator : AbstractValidator<UserController.UpdateUserDto>
    {
        public UpdateUserDtoValidator()
        {
            RuleFor(x => x.Password)
                .MinimumLength(6)
                .When(x => !string.IsNullOrEmpty(x.Password))
                .WithMessage("Password must be at least 6 characters long.");
        }
    }
}
