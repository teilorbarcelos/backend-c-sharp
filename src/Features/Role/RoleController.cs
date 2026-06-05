using Microsoft.AspNetCore.Mvc;
using MageBackend.Core;
using MageBackend.Core.Filters;
using MageBackend.Shared;
using MageBackend.Features.Role.Commands;
using MageBackend.Features.Role.Queries;
using MediatR;
using FluentValidation;

namespace MageBackend.Features.Role
{
    [ApiController]
    [Route("v1/role")]
    public class RoleController : BaseApiController
    {
        private readonly IMediator _mediator;
        private readonly IValidator<CreateRoleDto> _roleValidator;
        private static readonly string[] AllowedFields = { "name", "description" };

        public RoleController(IMediator mediator, IValidator<CreateRoleDto> roleValidator)
        {
            _mediator = mediator;
            _roleValidator = roleValidator;
        }

        [HttpGet("features")]
        [CheckPermission("role", "view")]
        [ProducesResponseType(typeof(List<Database.Feature>), 200)]
        public async Task<IActionResult> ListFeatures()
        {
            var features = await _mediator.Send(new ListFeaturesQuery());
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

            var result = await _mediator.Send(new ListRolesQuery(req));
            return Ok(result);
        }

        [HttpGet("all")]
        [CheckPermission("role", "view")]
        [ProducesResponseType(typeof(SearchResult<RoleResponseDto>), 200)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> ListAll()
        {
            var req = SearchRequest.Parse(Request.Query, AllowedFields, out var err);
            if (err != null) return ErrorResponse(err);

            var result = await _mediator.Send(new ListAllRolesQuery(req));
            return Ok(result);
        }

        [HttpGet("{id}")]
        [CheckPermission("role", "view")]
        [ProducesResponseType(typeof(RoleResponseDto), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetById(string id)
        {
            var result = await _mediator.Send(new GetRoleByIdQuery(id));
            if (result == null) return NotFound(new { message = "Role not found" });
            return Ok(result);
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
                throw new FluentValidation.ValidationException(validationResult.Errors);
            }

            var result = await _mediator.Send(new CreateRoleCommand(dto.Name, dto.Description, dto.Permissions));
            if (!result.Success) return BadRequest(new { message = result.Error });

            return StatusCode(201, result.Role);
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
                throw new FluentValidation.ValidationException(validationResult.Errors);
            }

            var result = await _mediator.Send(new UpdateRoleCommand(id, dto.Name, dto.Description, dto.Permissions));
            if (!result.Success) return StatusCode(result.StatusCode, new { message = result.Error });

            return Ok(result.Role);
        }

        [HttpDelete("{id}")]
        [CheckPermission("role", "delete")]
        [ProducesResponseType(204)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> Delete(string id)
        {
            var result = await _mediator.Send(new DeleteRoleCommand(id));
            if (!result.Success) return StatusCode(result.StatusCode, new { message = result.Error });

            return NoContent();
        }

        public record ToggleStatusDto
        {
            public required bool Active { get; init; }
        }

        [HttpPatch("{id}/status")]
        [CheckPermission("role", "activate")]
        [ProducesResponseType(typeof(RoleResponseDto), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> ToggleStatus(string id, [FromBody] ToggleStatusDto dto)
        {
            var result = await _mediator.Send(new ToggleRoleStatusCommand(id, dto.Active));
            if (!result.Success) return StatusCode(result.StatusCode, new { message = result.Error });

            return Ok(result.Role);
        }
    }
}
