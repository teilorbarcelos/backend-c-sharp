using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using MageBackend.Database;
using MageBackend.Core;
using MageBackend.Core.Middleware;
using MageBackend.Core.Filters;
using MageBackend.Infrastructure.Auth;
using FluentValidation;

namespace MageBackend.Features.Role
{
    [ApiController]
    [Route("v1/role")]
    public class RoleController : BaseApiController
    {
        private readonly ApplicationDbContext _context;
        private readonly IValidator<CreateRoleDto> _roleValidator;
        private static readonly string[] AllowedFields = { "name", "description" };

        public RoleController(ApplicationDbContext context, IValidator<CreateRoleDto> roleValidator)
        {
            _context = context;
            _roleValidator = roleValidator;
        }

        public record RoleFeatureDto
        {
            [JsonPropertyName("id_feature")]
            public string IdFeature { get; init; } = string.Empty;
            public bool Create { get; init; }
            public bool View { get; init; }
            public bool Delete { get; init; }
            public bool Activate { get; init; }
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

        [HttpGet("features")]
        [CheckPermission("role", "view")]
        [ProducesResponseType(typeof(List<Database.Feature>), 200)]
        public async Task<IActionResult> ListFeatures()
        {
            var features = await _context.Feature.Where(f => f.Active).ToListAsync();
            return Ok(features);
        }

        [HttpGet]
        [CheckPermission("role", "view")]
        [ProducesResponseType(typeof(SearchResult<RoleResponseDto>), 200)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> List()
        {
            var req = SearchRequest.Parse(Request.Query, AllowedFields, out var err);
            if (err != null) return ErrorResponse(err);

            var searchResult = await _context.Role
                .Where(r => !r.IsDeleted)
                .ApplyActiveFilter(req.Active, forceDefaultTrue: true)
                .ExecuteSearchAsync(req, r => r);

            var roleIds = searchResult.Items.Select(r => r.Id).ToList();
            var allRoleFeatures = await _context.RoleFeature
                .Where(rf => roleIds.Contains(rf.IdRole))
                .ToListAsync();

            var dtos = searchResult.Items.Select(r => MapToDto(r, allRoleFeatures
                .Where(rf => rf.IdRole == r.Id)
                .Select(rf => new RoleFeatureDto
                {
                    IdFeature = rf.IdFeature,
                    Create = rf.Create,
                    View = rf.View,
                    Delete = rf.Delete,
                    Activate = rf.Activate
                }).ToList())).ToList();

            return Ok(new SearchResult<RoleResponseDto>(dtos, searchResult.Total, searchResult.Page, searchResult.Size));
        }

        [HttpGet("all")]
        [CheckPermission("role", "view")]
        [ProducesResponseType(typeof(SearchResult<RoleResponseDto>), 200)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> ListAll()
        {
            var req = SearchRequest.Parse(Request.Query, AllowedFields, out var err);
            if (err != null) return ErrorResponse(err);

            var searchResult = await _context.Role
                .Where(r => !r.IsDeleted)
                .ApplyActiveFilter(req.Active)
                .ExecuteSearchAsync(req, r => r);

            var roleIds = searchResult.Items.Select(r => r.Id).ToList();
            var allRoleFeatures = await _context.RoleFeature
                .Where(rf => roleIds.Contains(rf.IdRole))
                .ToListAsync();

            var dtos = searchResult.Items.Select(r => MapToDto(r, allRoleFeatures
                .Where(rf => rf.IdRole == r.Id)
                .Select(rf => new RoleFeatureDto
                {
                    IdFeature = rf.IdFeature,
                    Create = rf.Create,
                    View = rf.View,
                    Delete = rf.Delete,
                    Activate = rf.Activate
                }).ToList())).ToList();

            return Ok(new SearchResult<RoleResponseDto>(dtos, searchResult.Total, searchResult.Page, searchResult.Size));
        }

        [HttpGet("{id}")]
        [CheckPermission("role", "view")]
        [ProducesResponseType(typeof(RoleResponseDto), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetById(string id)
        {
            var role = await _context.Role.Where(r => r.Id == id && !r.IsDeleted).FirstOrDefaultAsync();
            if (role == null) return NotFound(new { message = "Role not found" });

            var roleFeatures = await _context.RoleFeature
                .Where(rf => rf.IdRole == id)
                .Select(rf => new RoleFeatureDto
                {
                    IdFeature = rf.IdFeature,
                    Create = rf.Create,
                    View = rf.View,
                    Delete = rf.Delete,
                    Activate = rf.Activate
                }).ToListAsync();

            return Ok(MapToDto(role, roleFeatures));
        }

        public record CreateRoleDto
        {
            public string Name { get; init; } = string.Empty;
            public string Description { get; init; } = string.Empty;
            public List<RoleFeatureDto>? Permissions { get; init; }
        }

        [HttpPost]
        [CheckPermission("role", "create")]
        [ProducesResponseType(typeof(RoleResponseDto), 201)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> Create([FromBody] CreateRoleDto dto)
        {
            var validationResult = await _roleValidator.ValidateAsync(dto);
            if (!validationResult.IsValid)
            {
                return BadRequest(new { message = validationResult.Errors.First().ErrorMessage });
            }

            var id = Slugify(dto.Name);
            var exists = await _context.Role.AnyAsync(r => r.Id == id);
            if (exists) return BadRequest(new { message = "Role already exists" });

            var role = new Database.Role
            {
                Id = id,
                Name = dto.Name,
                Description = dto.Description,
                Active = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var permissionsList = dto.Permissions ?? new();
            var roleFeatures = permissionsList.Select(p => new RoleFeature
            {
                IdRole = id,
                IdFeature = p.IdFeature,
                Create = p.Create,
                View = p.View,
                Delete = p.Delete,
                Activate = p.Activate
            }).ToList();

            _context.Role.Add(role);
            await _context.RoleFeature.AddRangeAsync(roleFeatures);
            await _context.SaveChangesAsync();

            return StatusCode(201, MapToDto(role, permissionsList));
        }

        [HttpPut("{id}")]
        [CheckPermission("role", "create")]
        [ProducesResponseType(typeof(RoleResponseDto), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> Update(string id, [FromBody] CreateRoleDto dto)
        {
            var validationResult = await _roleValidator.ValidateAsync(dto);
            if (!validationResult.IsValid)
            {
                return BadRequest(new { message = validationResult.Errors.First().ErrorMessage });
            }

            var role = await _context.Role.Where(r => r.Id == id && !r.IsDeleted).FirstOrDefaultAsync();
            if (role == null) return NotFound(new { message = "Role not found" });

            role.Name = dto.Name;
            role.Description = dto.Description;
            role.UpdatedAt = DateTime.UtcNow;

            List<RoleFeatureDto> returnedPermissions;

            if (dto.Permissions != null)
            {
                // Remove existing permissions
                var existingFeatures = await _context.RoleFeature.Where(rf => rf.IdRole == id).ToListAsync();
                _context.RoleFeature.RemoveRange(existingFeatures);

                // Add new permissions
                var roleFeatures = dto.Permissions.Select(p => new RoleFeature
                {
                    IdRole = id,
                    IdFeature = p.IdFeature,
                    Create = p.Create,
                    View = p.View,
                    Delete = p.Delete,
                    Activate = p.Activate
                }).ToList();

                await _context.RoleFeature.AddRangeAsync(roleFeatures);
                returnedPermissions = dto.Permissions;
            }
            else
            {
                // Retain existing permissions
                var existingFeatures = await _context.RoleFeature
                    .Where(rf => rf.IdRole == id)
                    .Select(rf => new RoleFeatureDto
                    {
                        IdFeature = rf.IdFeature,
                        Create = rf.Create,
                        View = rf.View,
                        Delete = rf.Delete,
                        Activate = rf.Activate
                    }).ToListAsync();
                returnedPermissions = existingFeatures;
            }

            await _context.SaveChangesAsync();
            await InvalidateUserSessions(id);

            return Ok(MapToDto(role, returnedPermissions));
        }

        [HttpDelete("{id}")]
        [CheckPermission("role", "delete")]
        [ProducesResponseType(204)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> Delete(string id)
        {
            var role = await _context.Role.Where(r => r.Id == id && !r.IsDeleted).FirstOrDefaultAsync();
            if (role == null) return NotFound(new { message = "Role not found" });

            role.IsDeleted = true;
            role.DeletedAt = DateTime.UtcNow;
            role.Active = false;
            role.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await InvalidateUserSessions(id);

            return NoContent();
        }

        public record ToggleStatusDto
        {
            public bool Active { get; init; }
        }

        [HttpPatch("{id}/status")]
        [CheckPermission("role", "activate")]
        [ProducesResponseType(typeof(RoleResponseDto), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> ToggleStatus(string id, [FromBody] ToggleStatusDto dto)
        {
            var role = await _context.Role.Where(r => r.Id == id && !r.IsDeleted).FirstOrDefaultAsync();
            if (role == null) return NotFound(new { message = "Role not found" });

            role.Active = dto.Active;
            role.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await InvalidateUserSessions(id);

            var roleFeatures = await _context.RoleFeature
                .Where(rf => rf.IdRole == id)
                .Select(rf => new RoleFeatureDto
                {
                    IdFeature = rf.IdFeature,
                    Create = rf.Create,
                    View = rf.View,
                    Delete = rf.Delete,
                    Activate = rf.Activate
                }).ToListAsync();

            return Ok(MapToDto(role, roleFeatures));
        }

        private async Task InvalidateUserSessions(string roleId)
        {
            var userIds = await _context.User
                .Where(u => u.IdRole == roleId)
                .Select(u => u.Id)
                .ToListAsync();

            await SessionManager.InvalidateManyUsersSessionsAsync(userIds);
        }

        private static string Slugify(string name)
        {
            if (string.IsNullOrEmpty(name)) return Guid.NewGuid().ToString();
            var result = name.ToLower().Replace(" ", "-");
            var sb = new StringBuilder();
            foreach (var c in result)
            {
                if (char.IsLetterOrDigit(c) || c == '-') sb.Append(c);
            }
            return sb.ToString();
        }

        private static RoleResponseDto MapToDto(Database.Role role, List<RoleFeatureDto> features)
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

    public class CreateRoleDtoValidator : AbstractValidator<RoleController.CreateRoleDto>
    {
        public CreateRoleDtoValidator()
        {
            RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required");
        }
    }
}
