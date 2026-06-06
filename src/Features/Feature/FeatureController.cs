using Microsoft.AspNetCore.Mvc;
using MageBackend.Web;
using MageBackend.Web.Filters;
using MageBackend.Shared;
using MageBackend.Features.Feature.Commands;
using MageBackend.Features.Feature.Queries;
using MediatR;
using FluentValidation;

namespace MageBackend.Features.Feature
{
    [ApiController]
    [Route("v1/feature")]
    public class FeatureController : BaseApiController
    {
        private readonly IMediator _mediator;
        private readonly IValidator<CreateFeatureCommand> _createValidator;
        private readonly IValidator<UpdateFeatureCommand> _updateValidator;
        private static readonly string[] AllowedFields = { "name", "description" };

        public FeatureController(
            IMediator mediator,
            IValidator<CreateFeatureCommand> createValidator,
            IValidator<UpdateFeatureCommand> updateValidator)
        {
            _mediator = mediator;
            _createValidator = createValidator;
            _updateValidator = updateValidator;
        }

        [HttpGet]
        [AuthorizeAdmin]
        [ProducesResponseType(typeof(SearchResult<Database.Feature>), 200)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> List([FromQuery] string? active)
        {
            var req = SearchRequest.Parse(Request.Query, AllowedFields, out var err);
            if (err != null) return ErrorResponse(err);

            var result = await _mediator.Send(new ListFeaturesQuery(req));
            return Ok(result);
        }

        [HttpGet("all")]
        [AuthorizeAdmin]
        [ProducesResponseType(typeof(SearchResult<Database.Feature>), 200)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> ListAll()
        {
            var req = SearchRequest.Parse(Request.Query, AllowedFields, out var err);
            if (err != null) return ErrorResponse(err);

            var result = await _mediator.Send(new ListAllFeaturesQuery(req));
            return Ok(result);
        }

        [HttpGet("{id}")]
        [AuthorizeAdmin]
        [ProducesResponseType(typeof(Database.Feature), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetById(string id)
        {
            var result = await _mediator.Send(new GetFeatureByIdQuery(id));
            if (result == null) return NotFound(new { message = "Feature not found" });
            return Ok(result);
        }

        [HttpPost]
        [AuthorizeAdmin]
        [ProducesResponseType(typeof(Database.Feature), 201)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> Create([FromBody] CreateFeatureCommand command)
        {
            var validationResult = await _createValidator.ValidateAsync(command);
            if (!validationResult.IsValid)
            {
                throw new FluentValidation.ValidationException(validationResult.Errors);
            }

            var result = await _mediator.Send(command);
            if (!result.Success) return BadRequest(new { message = result.Error });

            return StatusCode(201, result.Feature);
        }

        [HttpPut("{id}")]
        [AuthorizeAdmin]
        [ProducesResponseType(typeof(Database.Feature), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> Update(string id, [FromBody] UpdateFeatureCommand command)
        {
            var cmd = command with { Id = id };

            var validationResult = await _updateValidator.ValidateAsync(cmd);
            if (!validationResult.IsValid)
            {
                throw new FluentValidation.ValidationException(validationResult.Errors);
            }

            var result = await _mediator.Send(cmd);
            if (!result.Success) return StatusCode(result.StatusCode, new { message = result.Error });

            return Ok(result.Feature);
        }

        [HttpDelete("{id}")]
        [AuthorizeAdmin]
        [ProducesResponseType(204)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> Delete(string id)
        {
            var result = await _mediator.Send(new DeleteFeatureCommand(id));
            if (!result.Success) return StatusCode(result.StatusCode, new { message = result.Error });

            return NoContent();
        }

        [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
        public record ToggleStatusDto
        {
            public required bool Active { get; init; }
        }

        [HttpPatch("{id}/status")]
        [AuthorizeAdmin]
        [ProducesResponseType(typeof(Database.Feature), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> ToggleStatus(string id, [FromBody] ToggleStatusDto dto)
        {
            var result = await _mediator.Send(new ToggleFeatureStatusCommand(id, dto.Active));
            if (!result.Success) return StatusCode(result.StatusCode, new { message = result.Error });

            return Ok(result.Feature);
        }
    }
}
