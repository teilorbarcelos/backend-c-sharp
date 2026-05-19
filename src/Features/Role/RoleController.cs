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

namespace MageBackend.Features.Role
{
    [ApiController]
    [Route("v1/role")]
    public class RoleController : BaseApiController
    {
        private readonly ApplicationDbContext _context;
        private static readonly string[] AllowedFields = { "name", "description" };

        public RoleController(ApplicationDbContext context)
        {
            _context = context;
        }

        public class RoleFeatureDto
        {
            [JsonPropertyName("id_feature")]
            public string IdFeature { get; set; } = string.Empty;
            public bool Create { get; set; }
            public bool View { get; set; }
            public bool Delete { get; set; }
            public bool Activate { get; set; }
        }

        public class RoleResponseDto
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string? Description { get; set; }
            public bool Active { get; set; }
            [JsonPropertyName("is_deleted")]
            public bool IsDeleted { get; set; }
            [JsonPropertyName("created_at")]
            public DateTime CreatedAt { get; set; }
            [JsonPropertyName("updated_at")]
            public DateTime UpdatedAt { get; set; }
            [JsonPropertyName("deleted_at")]
            [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
            public DateTime? DeletedAt { get; set; }
            [JsonPropertyName("RoleFeature")]
            public List<RoleFeatureDto> RoleFeature { get; set; } = new();
        }

        [HttpGet("features")]
        [CheckPermission("role", "view")]
        public async Task<IActionResult> ListFeatures()
        {
            var features = await _context.Feature.Where(f => f.Active).ToListAsync();
            return Ok(features);
        }

        [HttpGet]
        [CheckPermission("role", "view")]
        public async Task<IActionResult> List()
        {
            var req = SearchRequest.Parse(Request.Query, AllowedFields, out var err);
            if (err != null) return ErrorResponse(err);

            var queryable = _context.Role.Where(r => !r.IsDeleted);

            if (req.Active.HasValue)
            {
                queryable = queryable.Where(r => r.Active == req.Active.Value);
            }
            else
            {
                queryable = queryable.Where(r => r.Active == true);
            }

            return await ExecuteSearch(queryable, req);
        }

        [HttpGet("all")]
        [CheckPermission("role", "view")]
        public async Task<IActionResult> ListAll()
        {
            var req = SearchRequest.Parse(Request.Query, AllowedFields, out var err);
            if (err != null) return ErrorResponse(err);

            var queryable = _context.Role.Where(r => !r.IsDeleted);

            if (req.Active.HasValue)
            {
                queryable = queryable.Where(r => r.Active == req.Active.Value);
            }

            return await ExecuteSearch(queryable, req);
        }

        [HttpGet("{id}")]
        [CheckPermission("role", "view")]
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

        public class CreateRoleDto
        {
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public List<RoleFeatureDto>? Permissions { get; set; }
        }

        [HttpPost]
        [CheckPermission("role", "create")]
        public async Task<IActionResult> Create([FromBody] CreateRoleDto dto)
        {
            if (string.IsNullOrEmpty(dto.Name)) return BadRequest(new { message = "Name is required" });

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
        public async Task<IActionResult> Update(string id, [FromBody] CreateRoleDto dto)
        {
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

        public class ToggleStatusDto
        {
            public bool Active { get; set; }
        }

        [HttpPatch("{id}/status")]
        [CheckPermission("role", "activate")]
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

        private async Task<IActionResult> ExecuteSearch(IQueryable<Database.Role> query, SearchRequest req)
        {
            if (!string.IsNullOrEmpty(req.SearchWord))
            {
                query = query.Where(r => r.Name.Contains(req.SearchWord) || (r.Description != null && r.Description.Contains(req.SearchWord)));
            }

            if (req.CreatedAtStart.HasValue)
            {
                query = query.Where(r => r.CreatedAt >= req.CreatedAtStart.Value);
            }
            if (req.CreatedAtEnd.HasValue)
            {
                query = query.Where(r => r.CreatedAt <= req.CreatedAtEnd.Value);
            }

            var total = await query.CountAsync();

            if (!string.IsNullOrEmpty(req.OrderBy))
            {
                var isDesc = req.OrderDirection.Equals("desc", StringComparison.OrdinalIgnoreCase);
                query = req.OrderBy.ToLower() switch
                {
                    "name" => isDesc ? query.OrderByDescending(r => r.Name) : query.OrderBy(r => r.Name),
                    "description" => isDesc ? query.OrderByDescending(r => r.Description) : query.OrderBy(r => r.Description),
                    _ => isDesc ? query.OrderByDescending(r => r.CreatedAt) : query.OrderBy(r => r.CreatedAt)
                };
            }
            else
            {
                query = query.OrderByDescending(r => r.CreatedAt);
            }

            var items = await query.Skip(req.Page * req.Size).Take(req.Size).ToListAsync();

            var roleIds = items.Select(r => r.Id).ToList();
            var allRoleFeatures = await _context.RoleFeature
                .Where(rf => roleIds.Contains(rf.IdRole))
                .ToListAsync();

            var dtos = items.Select(r => MapToDto(r, allRoleFeatures
                .Where(rf => rf.IdRole == r.Id)
                .Select(rf => new RoleFeatureDto
                {
                    IdFeature = rf.IdFeature,
                    Create = rf.Create,
                    View = rf.View,
                    Delete = rf.Delete,
                    Activate = rf.Activate
                }).ToList())).ToList();

            return Ok(new SearchResult<RoleResponseDto>(dtos, total, req.Page, req.Size));
        }
    }
}
