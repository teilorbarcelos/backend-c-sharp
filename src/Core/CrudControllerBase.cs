using Microsoft.AspNetCore.Mvc;
using MageBackend.Shared.Cqrs;
using MageBackend.Core.Filters;
using MageBackend.Domain;
using MageBackend.Shared;
using MediatR;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using System;

namespace MageBackend.Core
{
    public interface ICommandWithId
    {
        void SetId(string id);
    }

#pragma warning disable S2436
    [Route("[controller]")]
    public abstract class CrudControllerBase<TEntity, TDto, TCreateCmd, TUpdateCmd> : BaseApiController
        where TEntity : BaseEntity
        where TDto : class
#pragma warning restore S2436
        where TCreateCmd : IRequest<CommandResult<TDto>>
        where TUpdateCmd : IRequest<CommandResult<TDto>>
    {
        protected readonly IMediator Mediator;
        protected readonly string[] AllowedFields;

        protected CrudControllerBase(IMediator mediator, string[] allowedFields)
        {
            Mediator = mediator;
            AllowedFields = allowedFields;
        }

        [HttpGet]
        [CheckPermission("view")]
        [ProducesResponseType(200)]
        [ProducesResponseType(400)]
        public virtual async Task<ActionResult<SearchResult<TDto>>> List(
            [FromQuery] string? q = null,
            [FromQuery] int? page = null,
            [FromQuery] int? pageSize = null,
            [FromQuery] string? order = null,
            [FromQuery] string? filter = null)
        {
            var req = SearchRequest.Parse(Request.Query, AllowedFields, out var err);
            if (err != null) return BadRequest(new { message = err });

            var result = await Mediator.Send(new ListQuery<TEntity, TDto>(req));
            return Ok(result);
        }

        [HttpGet("all")]
        [CheckPermission("view")]
        [ProducesResponseType(200)]
        [ProducesResponseType(400)]
        public virtual async Task<ActionResult<SearchResult<TDto>>> ListAll(
            [FromQuery] string? q = null,
            [FromQuery] string? order = null,
            [FromQuery] string? filter = null)
        {
            var req = SearchRequest.Parse(Request.Query, AllowedFields, out var err);
            if (err != null) return BadRequest(new { message = err });

            var result = await Mediator.Send(new ListAllQuery<TEntity, TDto>(req));
            return Ok(result);
        }

        [HttpGet("{id}")]
        [CheckPermission("view")]
        [ProducesResponseType(200)]
        [ProducesResponseType(404)]
        public virtual async Task<ActionResult<TDto>> GetById(string id)
        {
            var result = await Mediator.Send(new GetByIdQuery<TEntity, TDto>(id));
            if (result == null) return NotFound(new { message = "Registro não encontrado" });
            return Ok(result);
        }

        [HttpPost]
        [CheckPermission("create")]
        [ProducesResponseType(201)]
        [ProducesResponseType(400)]
        [ExcludeFromCodeCoverage]
        public virtual async Task<ActionResult<TDto>> Create([FromBody] TCreateCmd command)
        {
            var result = await Mediator.Send(command);
            if (!result.Success) return StatusCode(result.StatusCode, new { message = result.Error });

            return StatusCode(201, result.Data);
        }

        [HttpPut("{id}")]
        [CheckPermission("create")]
        [ProducesResponseType(200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(404)]
        public virtual async Task<ActionResult<TDto>> Update(string id, [FromBody] TUpdateCmd command)
        {
            if (command is ICommandWithId cmdWithId)
            {
                cmdWithId.SetId(id);
            }

            var result = await Mediator.Send(command);
            if (!result.Success) return StatusCode(result.StatusCode, new { message = result.Error });

            return Ok(result.Data);
        }

        [HttpDelete("{id}")]
        [CheckPermission("delete")]
        [ProducesResponseType(204)]
        [ProducesResponseType(404)]
        public virtual async Task<ActionResult> Delete(string id)
        {
            var result = await Mediator.Send(new DeleteCommand<TEntity>(id));
            if (!result.Success) return StatusCode(result.StatusCode, new { message = result.Error });

            return NoContent();
        }

        [HttpPatch("{id}/status")]
        [CheckPermission("activate")]
        [ProducesResponseType(200)]
        [ProducesResponseType(404)]
        public virtual async Task<ActionResult<TDto>> ToggleStatus(string id, [FromBody] ToggleStatusDto dto)
        {
            var result = await Mediator.Send(new ToggleStatusCommand<TEntity, TDto>(id, dto.Active));
            if (!result.Success) return StatusCode(result.StatusCode, new { message = result.Error });

            return Ok(result.Data);
        }
    }

    public record ToggleStatusDto
    {
        public required bool Active { get; init; }
    }
}
