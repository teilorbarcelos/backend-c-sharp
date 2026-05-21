using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using MageBackend.Database;
using MageBackend.Core;
using MageBackend.Core.Middleware;
using MageBackend.Core.Filters;
using FluentValidation;

namespace MageBackend.Features.{{EntityName}}
{
    [ApiController]
    [Route("v1/{{EntityNameLower}}")]
    public class {{EntityName}}Controller : BaseApiController
    {
        private readonly ApplicationDbContext _context;
        private readonly IValidator<Create{{EntityName}}Dto> _createValidator;
        private static readonly string[] AllowedFields = { {{AllowedFields}} };

        public {{EntityName}}Controller(ApplicationDbContext context, IValidator<Create{{EntityName}}Dto> createValidator)
        {
            _context = context;
            _createValidator = createValidator;
        }

        public record {{EntityName}}ResponseDto
        {
{{ResponseDtoProperties}}
        }

        [HttpGet]
        [CheckPermission("{{FeatureId}}", "view")]
        [ProducesResponseType(typeof(SearchResult<{{EntityName}}ResponseDto>), 200)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> List()
        {
            var req = SearchRequest.Parse(Request.Query, AllowedFields, out var err);
            if (err != null) return ErrorResponse(err);

            var result = await _context.{{EntityName}}
                .Where(p => !p.IsDeleted)
                .ApplyActiveFilter(req.Active, forceDefaultTrue: true)
                .ExecuteSearchAsync(req, MapToDto);

            return Ok(result);
        }

        [HttpGet("all")]
        [CheckPermission("{{FeatureId}}", "view")]
        [ProducesResponseType(typeof(SearchResult<{{EntityName}}ResponseDto>), 200)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> ListAll()
        {
            var req = SearchRequest.Parse(Request.Query, AllowedFields, out var err);
            if (err != null) return ErrorResponse(err);

            var result = await _context.{{EntityName}}
                .Where(p => !p.IsDeleted)
                .ApplyActiveFilter(req.Active)
                .ExecuteSearchAsync(req, MapToDto);

            return Ok(result);
        }

        [HttpGet("{id}")]
        [CheckPermission("{{FeatureId}}", "view")]
        [ProducesResponseType(typeof({{EntityName}}ResponseDto), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetById(string id)
        {
            var entity = await _context.{{EntityName}}.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);
            if (entity == null) return NotFound(new { message = "{{EntityName}} not found" });

            return Ok(MapToDto(entity));
        }

        public record Create{{EntityName}}Dto
        {
{{CreateDtoProperties}}
        }

        [HttpPost]
        [CheckPermission("{{FeatureId}}", "create")]
        [ProducesResponseType(typeof({{EntityName}}ResponseDto), 201)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> Create([FromBody] Create{{EntityName}}Dto dto)
        {
            var validationResult = await _createValidator.ValidateAsync(dto);
            if (!validationResult.IsValid)
            {
                throw new FluentValidation.ValidationException(validationResult.Errors);
            }

            var entity = new Database.{{EntityName}}
            {
                Id = Guid.NewGuid().ToString(),
{{CreateMappings}},
                Active = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.{{EntityName}}.Add(entity);
            await _context.SaveChangesAsync();

            return StatusCode(201, MapToDto(entity));
        }

        [HttpPut("{id}")]
        [CheckPermission("{{FeatureId}}", "create")]
        [ProducesResponseType(typeof({{EntityName}}ResponseDto), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> Update(string id, [FromBody] Create{{EntityName}}Dto dto)
        {
            var entity = await _context.{{EntityName}}.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);
            if (entity == null) return NotFound(new { message = "{{EntityName}} not found" });

{{UpdateMappings}}
            entity.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return Ok(MapToDto(entity));
        }

        [HttpDelete("{id}")]
        [CheckPermission("{{FeatureId}}", "delete")]
        [ProducesResponseType(204)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> Delete(string id)
        {
            var entity = await _context.{{EntityName}}.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);
            if (entity == null) return NotFound(new { message = "{{EntityName}} not found" });

            entity.IsDeleted = true;
            entity.DeletedAt = DateTime.UtcNow;
            entity.Active = false;
            entity.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return NoContent();
        }

        public record ToggleStatusDto
        {
            public bool Active { get; init; }
        }

        [HttpPatch("{id}/status")]
        [CheckPermission("{{FeatureId}}", "activate")]
        [ProducesResponseType(typeof({{EntityName}}ResponseDto), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> ToggleStatus(string id, [FromBody] ToggleStatusDto dto)
        {
            var entity = await _context.{{EntityName}}.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);
            if (entity == null) return NotFound(new { message = "{{EntityName}} not found" });

            entity.Active = dto.Active;
            entity.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return Ok(MapToDto(entity));
        }

        private static {{EntityName}}ResponseDto MapToDto(Database.{{EntityName}} entity)
        {
            return new {{EntityName}}ResponseDto
            {
                Id = entity.Id,
{{ResponseMappings}},
                Active = entity.Active,
                IsDeleted = entity.IsDeleted,
                CreatedAt = entity.CreatedAt,
                UpdatedAt = entity.UpdatedAt
            };
        }
    }

    public class Create{{EntityName}}DtoValidator : AbstractValidator<{{EntityName}}Controller.Create{{EntityName}}Dto>
    {
        public Create{{EntityName}}DtoValidator()
        {
{{ValidationRules}}
        }
    }
}
