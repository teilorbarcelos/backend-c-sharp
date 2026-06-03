using MediatR;

namespace MageBackend.Core.Commands
{
#pragma warning disable S2326
    public record GetByIdQuery<TEntity, TDto>(string Id) : IRequest<TDto?> where TEntity : BaseEntity;
    public record ListQuery<TEntity, TDto>(SearchRequest Request) : IRequest<SearchResult<TDto>> where TEntity : BaseEntity;
    public record ListAllQuery<TEntity, TDto>(SearchRequest Request) : IRequest<SearchResult<TDto>> where TEntity : BaseEntity;
    public record DeleteCommand<TEntity>(string Id) : IRequest<CommandResult> where TEntity : BaseEntity;
    public record ToggleStatusCommand<TEntity, TDto>(string Id, bool Active) : IRequest<CommandResult<TDto>> where TEntity : BaseEntity;
#pragma warning restore S2326
}
