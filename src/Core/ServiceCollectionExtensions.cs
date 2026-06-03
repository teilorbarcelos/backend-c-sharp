using MageBackend.Core;
using MageBackend.Core.Commands;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddCrudHandlers<TEntity, TDto>(this IServiceCollection services)
            where TEntity : BaseEntity
        {
            services.AddTransient<IRequestHandler<GetByIdQuery<TEntity, TDto>, TDto?>, GetByIdHandler<TEntity, TDto>>();
            services.AddTransient<IRequestHandler<ListQuery<TEntity, TDto>, SearchResult<TDto>>, ListHandler<TEntity, TDto>>();
            services.AddTransient<IRequestHandler<ListAllQuery<TEntity, TDto>, SearchResult<TDto>>, ListAllHandler<TEntity, TDto>>();
            services.AddTransient<IRequestHandler<DeleteCommand<TEntity>, CommandResult>, DeleteHandler<TEntity>>();
            services.AddTransient<IRequestHandler<ToggleStatusCommand<TEntity, TDto>, CommandResult<TDto>>, ToggleStatusHandler<TEntity, TDto>>();

            return services;
        }
    }
}
