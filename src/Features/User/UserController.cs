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

namespace MageBackend.Features.User
{
    [ApiController]
    [Route("v1/user")]
    public class UserController : BaseApiController
    {
        private readonly ApplicationDbContext _context;
        private static readonly string[] AllowedFields = { "name", "email", "active", "created_at", "updated_at", "Role.name" };

        public UserController(ApplicationDbContext context)
        {
            _context = context;
        }

        public class UserResponseDto
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string? Phone { get; set; }
            public string? Document { get; set; }
            public string? Avatar { get; set; }
            [JsonPropertyName("id_role")]
            public string IdRole { get; set; } = string.Empty;
            public bool Active { get; set; }
            [JsonPropertyName("created_at")]
            public DateTime CreatedAt { get; set; }
            [JsonPropertyName("updated_at")]
            public DateTime UpdatedAt { get; set; }
        }

        [HttpGet]
        [CheckPermission("user", "view")]
        [ProducesResponseType(typeof(SearchResult<UserResponseDto>), 200)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> List()
        {
            var req = SearchRequest.Parse(Request.Query, AllowedFields, out var err);
            if (err != null) return ErrorResponse(err);

            var query = _context.User.Include(u => u.Role).Where(u => !u.IsDeleted);

            if (req.Active.HasValue)
            {
                query = query.Where(u => u.Active == req.Active.Value);
            }
            else
            {
                query = query.Where(u => u.Active == true);
            }

            return await ExecuteSearch(query, req);
        }

        [HttpGet("all")]
        [CheckPermission("user", "view")]
        [ProducesResponseType(typeof(SearchResult<UserResponseDto>), 200)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> ListAll()
        {
            var req = SearchRequest.Parse(Request.Query, AllowedFields, out var err);
            if (err != null) return ErrorResponse(err);

            var query = _context.User.Include(u => u.Role).Where(u => !u.IsDeleted);

            if (req.Active.HasValue)
            {
                query = query.Where(u => u.Active == req.Active.Value);
            }

            return await ExecuteSearch(query, req);
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

        public class CreateUserDto
        {
            public string Name { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
            public string? Phone { get; set; }
            public string? Document { get; set; }
            public string? Avatar { get; set; }
            [JsonPropertyName("id_role")]
            public string IdRole { get; set; } = string.Empty;
        }

        [HttpPost]
        [CheckPermission("user", "create")]
        [ProducesResponseType(typeof(UserResponseDto), 201)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> Create([FromBody] CreateUserDto dto)
        {
            if (string.IsNullOrEmpty(dto.Name) || string.IsNullOrEmpty(dto.Email) || string.IsNullOrEmpty(dto.Password) || string.IsNullOrEmpty(dto.IdRole))
            {
                return BadRequest(new { message = "Name, email, password, and id_role are required." });
            }

            if (dto.Password.Length < 6)
            {
                return BadRequest(new { message = "Password must be at least 6 characters long." });
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

        public class UpdateUserDto
        {
            public string? Name { get; set; }
            public string? Email { get; set; }
            public string? Password { get; set; }
            public string? Phone { get; set; }
            public string? Document { get; set; }
            public string? Avatar { get; set; }
            [JsonPropertyName("id_role")]
            public string? IdRole { get; set; }
            public bool? Active { get; set; }
        }

        [HttpPut("{id}")]
        [CheckPermission("user", "create")]
        [ProducesResponseType(typeof(UserResponseDto), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> Update(string id, [FromBody] UpdateUserDto dto)
        {
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
                if (dto.Password.Length < 6) return BadRequest(new { message = "Password must be at least 6 characters long." });
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

        public class ToggleStatusDto
        {
            public bool Active { get; set; }
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

        private async Task<IActionResult> ExecuteSearch(IQueryable<Database.User> query, SearchRequest req)
        {
            // Apply SearchWord
            if (!string.IsNullOrEmpty(req.SearchWord) && !string.IsNullOrEmpty(req.SearchFields))
            {
                var fields = req.SearchFields.Split(',').Select(f => f.Trim().ToLower()).ToList();
                query = query.Where(u =>
                    (fields.Contains("name") && u.Name.Contains(req.SearchWord)) ||
                    (fields.Contains("email") && u.Email.Contains(req.SearchWord)) ||
                    (fields.Contains("role.name") && u.Role != null && u.Role.Name.Contains(req.SearchWord))
                );
            }

            // Apply Date Filters
            if (req.CreatedAtStart.HasValue)
            {
                query = query.Where(u => u.CreatedAt >= req.CreatedAtStart.Value);
            }
            if (req.CreatedAtEnd.HasValue)
            {
                query = query.Where(u => u.CreatedAt <= req.CreatedAtEnd.Value);
            }

            var total = await query.CountAsync();

            // Apply Sorting
            if (!string.IsNullOrEmpty(req.OrderBy))
            {
                var isDesc = req.OrderDirection.Equals("desc", StringComparison.OrdinalIgnoreCase);
                query = req.OrderBy.ToLower() switch
                {
                    "name" => isDesc ? query.OrderByDescending(u => u.Name) : query.OrderBy(u => u.Name),
                    "email" => isDesc ? query.OrderByDescending(u => u.Email) : query.OrderBy(u => u.Email),
                    "role.name" => isDesc ? query.OrderByDescending(u => u.Role != null ? u.Role.Name : "") : query.OrderBy(u => u.Role != null ? u.Role.Name : ""),
                    _ => isDesc ? query.OrderByDescending(u => u.CreatedAt) : query.OrderBy(u => u.CreatedAt)
                };
            }
            else
            {
                query = query.OrderByDescending(u => u.CreatedAt);
            }

            // Apply Pagination
            var items = await query.Skip(req.Page * req.Size).Take(req.Size).ToListAsync();

            var dtos = items.Select(MapToDto).ToList();
            return Ok(new SearchResult<UserResponseDto>(dtos, total, req.Page, req.Size));
        }
    }
}
