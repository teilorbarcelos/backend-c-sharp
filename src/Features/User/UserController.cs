using Microsoft.AspNetCore.Mvc;
using MageBackend.Web;
using MageBackend.Web.Filters;
using MageBackend.Features.User.Commands;
using MageBackend.Features.User.Queries;
using MageBackend.Infrastructure.Auth;
using MageBackend.Shared;
using MediatR;
using FluentValidation;

namespace MageBackend.Features.User
{
    [ApiController]
    [Route("v1/user")]
    [FeatureName("user")]
    public class UserController : CrudControllerBase<Database.User, UserResponseDto, CreateUserCommand, UpdateUserCommand>
    {
        private readonly IValidator<CreateUserCommand> _createUserValidator;
        private readonly IValidator<UpdateUserCommand> _updateUserValidator;
        private new static readonly string[] AllowedFields = { "name", "email", "active", "created_at", "updated_at", "Role.name" };

        public UserController(
            IMediator mediator,
            IValidator<CreateUserCommand> createUserValidator,
            IValidator<UpdateUserCommand> updateUserValidator) : base(mediator, AllowedFields)
        {
            _createUserValidator = createUserValidator;
            _updateUserValidator = updateUserValidator;
        }



        [HttpGet("export/pdf")]
        [CheckPermission("user", "view")]
        [ProducesResponseType(typeof(FileResult), 200)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> ExportPdf()
        {
            var req = SearchRequest.Parse(Request.Query, AllowedFields, out var err);
            if (err != null) return ErrorResponse(err);

            try
            {
                var pdfStream = await Mediator.Send(new ExportUsersPdfQuery(req));
                Response.Headers.ContentDisposition = "attachment; filename=\"usuarios.pdf\"";
                return File(pdfStream, "application/pdf");
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }



        [HttpPost]
        [CheckPermission("user", "create")]
        [ProducesResponseType(typeof(UserResponseDto), 201)]
        [ProducesResponseType(400)]
        public override async Task<ActionResult<UserResponseDto>> Create([FromBody] CreateUserCommand command)
        {
            var validationResult = await _createUserValidator.ValidateAsync(command);
            if (!validationResult.IsValid)
            {
                throw new FluentValidation.ValidationException(validationResult.Errors);
            }

            var result = await Mediator.Send(command);
            if (!result.Success) return BadRequest(new { message = result.Error });

            return StatusCode(201, result.Data);
        }

        [HttpPut("{id}")]
        [CheckPermission("user", "create")]
        [ProducesResponseType(typeof(UserResponseDto), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(404)]
        public override async Task<ActionResult<UserResponseDto>> Update(string id, [FromBody] UpdateUserCommand command)
        {
            var cmd = command with { Id = id };

            var validationResult = await _updateUserValidator.ValidateAsync(cmd);
            if (!validationResult.IsValid)
            {
                throw new FluentValidation.ValidationException(validationResult.Errors);
            }

            var result = await Mediator.Send(cmd);
            if (!result.Success) return StatusCode(result.StatusCode, new { message = result.Error });

            return Ok(result.Data);
        }

        [HttpDelete("{id}")]
        [CheckPermission("user", "delete")]
        [ProducesResponseType(204)]
        [ProducesResponseType(400)]
        [ProducesResponseType(404)]
        public override async Task<ActionResult> Delete(string id)
        {
            var result = await Mediator.Send(new DeleteUserCommand(id));
            if (!result.Success) return StatusCode(result.StatusCode, new { message = result.Error });

            return NoContent();
        }

        [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
        public record ToggleStatusDto
        {
            public required bool Active { get; init; }
        }

        [HttpPatch("{id}/status")]
        [CheckPermission("user", "activate")]
        [ProducesResponseType(typeof(UserResponseDto), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(404)]
        public override async Task<ActionResult<UserResponseDto>> ToggleStatus(string id, [FromBody] MageBackend.Web.ToggleStatusDto dto)
        {
            var result = await Mediator.Send(new ToggleUserStatusCommand(id, dto.Active));
            if (!result.Success) return StatusCode(result.StatusCode, new { message = result.Error });

            return Ok(result.User);
        }
    }
}
