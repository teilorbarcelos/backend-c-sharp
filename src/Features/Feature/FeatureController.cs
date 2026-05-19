using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using MageBackend.Database;
using MageBackend.Core;
using MageBackend.Core.Middleware;
using MageBackend.Core.Filters;

namespace MageBackend.Features.Feature
{
    [ApiController]
    [Route("v1/feature")]
    public class FeatureController : BaseApiController
    {
        private readonly ApplicationDbContext _context;
        private static readonly string[] AllowedFields = { "name", "description" };

        public FeatureController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        [CheckPermission("feature", "view")]
        [ProducesResponseType(typeof(SearchResult<Database.Feature>), 200)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> List([FromQuery] string? active)
        {
            var req = SearchRequest.Parse(Request.Query, AllowedFields, out var err);
            if (err != null) return ErrorResponse(err);

            var result = await _context.Feature
                .ApplyActiveFilter(req.Active, forceDefaultTrue: true)
                .ExecuteSearchAsync(req, f => f);

            return Ok(result);
        }

        [HttpGet("all")]
        [CheckPermission("feature", "view")]
        [ProducesResponseType(typeof(SearchResult<Database.Feature>), 200)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> ListAll()
        {
            var req = SearchRequest.Parse(Request.Query, AllowedFields, out var err);
            if (err != null) return ErrorResponse(err);

            var result = await _context.Feature
                .ApplyActiveFilter(req.Active)
                .ExecuteSearchAsync(req, f => f);

            return Ok(result);
        }

        [HttpGet("{id}")]
        [CheckPermission("feature", "view")]
        [ProducesResponseType(typeof(Database.Feature), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetById(string id)
        {
            var feature = await _context.Feature.FindAsync(id);
            if (feature == null) return NotFound(new { message = "Feature not found" });
            return Ok(feature);
        }

        public class CreateFeatureDto
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string? Description { get; set; }
        }

        [HttpPost]
        [CheckPermission("feature", "create")]
        [ProducesResponseType(typeof(Database.Feature), 201)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> Create([FromBody] CreateFeatureDto dto)
        {
            if (string.IsNullOrEmpty(dto.Id) || string.IsNullOrEmpty(dto.Name))
            {
                return BadRequest(new { message = "Id and Name are required" });
            }

            var exists = await _context.Feature.AnyAsync(f => f.Id == dto.Id);
            if (exists) return BadRequest(new { message = "Feature already exists" });

            var feature = new Database.Feature
            {
                Id = dto.Id,
                Name = dto.Name,
                Description = dto.Description,
                Active = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Feature.Add(feature);
            await _context.SaveChangesAsync();

            return StatusCode(201, feature);
        }

        public class UpdateFeatureDto
        {
            public string Name { get; set; } = string.Empty;
            public string? Description { get; set; }
        }

        [HttpPut("{id}")]
        [CheckPermission("feature", "create")]
        [ProducesResponseType(typeof(Database.Feature), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> Update(string id, [FromBody] UpdateFeatureDto dto)
        {
            var feature = await _context.Feature.FindAsync(id);
            if (feature == null) return NotFound(new { message = "Feature not found" });

            feature.Name = dto.Name;
            feature.Description = dto.Description;
            feature.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return Ok(feature);
        }

        [HttpDelete("{id}")]
        [CheckPermission("feature", "delete")]
        [ProducesResponseType(204)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> Delete(string id)
        {
            var feature = await _context.Feature.FindAsync(id);
            if (feature == null) return NotFound(new { message = "Feature not found" });

            _context.Feature.Remove(feature);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        public class ToggleStatusDto
        {
            public bool Active { get; set; }
        }

        [HttpPatch("{id}/status")]
        [CheckPermission("feature", "activate")]
        [ProducesResponseType(typeof(Database.Feature), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> ToggleStatus(string id, [FromBody] ToggleStatusDto dto)
        {
            var feature = await _context.Feature.FindAsync(id);
            if (feature == null) return NotFound(new { message = "Feature not found" });

            feature.Active = dto.Active;
            feature.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return Ok(feature);
        }
    }
}
