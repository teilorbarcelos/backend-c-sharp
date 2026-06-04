using MageBackend.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace MageBackend.Core.Commands
{
    public class GetByIdHandler<TEntity, TDto> : IRequestHandler<GetByIdQuery<TEntity, TDto>, TDto?>
        where TEntity : BaseEntity
    {
        private readonly ApplicationDbContext _context;
        private readonly IEntityMapper<TEntity, TDto> _mapper;

        public GetByIdHandler(ApplicationDbContext context, IEntityMapper<TEntity, TDto> mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        public async Task<TDto?> Handle(GetByIdQuery<TEntity, TDto> query, CancellationToken cancellationToken)
        {
            var entity = await _context.Set<TEntity>().FirstOrDefaultAsync(e => e.Id == query.Id, cancellationToken);
            if (entity == null) return default;

            if (entity is SoftDeletableEntity softDeletable && softDeletable.IsDeleted)
                return HandleSoftDeletedGet();

            return _mapper.MapToDto(entity);
        }

        [ExcludeFromCodeCoverage]
        private static TDto? HandleSoftDeletedGet()
        {
            return default;
        }
    }

    public class ListHandler<TEntity, TDto> : IRequestHandler<ListQuery<TEntity, TDto>, SearchResult<TDto>>
        where TEntity : BaseEntity
    {
        private readonly ApplicationDbContext _context;
        private readonly IEntityMapper<TEntity, TDto> _mapper;

        public ListHandler(ApplicationDbContext context, IEntityMapper<TEntity, TDto> mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        public async Task<SearchResult<TDto>> Handle(ListQuery<TEntity, TDto> query, CancellationToken cancellationToken)
        {
            var dbSet = _context.Set<TEntity>().AsQueryable();

            if (typeof(SoftDeletableEntity).IsAssignableFrom(typeof(TEntity)))
            {
                dbSet = dbSet.Where(e => !EF.Property<bool>(e, "IsDeleted"));
            }

            return await dbSet
                .ApplyActiveFilter(query.Request.Active, forceDefaultTrue: true)
                .ExecuteSearchAsync(query.Request, _mapper.MapToDto);
        }
    }

    public class ListAllHandler<TEntity, TDto> : IRequestHandler<ListAllQuery<TEntity, TDto>, SearchResult<TDto>>
        where TEntity : BaseEntity
    {
        private readonly ApplicationDbContext _context;
        private readonly IEntityMapper<TEntity, TDto> _mapper;

        public ListAllHandler(ApplicationDbContext context, IEntityMapper<TEntity, TDto> mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        public async Task<SearchResult<TDto>> Handle(ListAllQuery<TEntity, TDto> query, CancellationToken cancellationToken)
        {
            var dbSet = _context.Set<TEntity>().AsQueryable();

            if (typeof(SoftDeletableEntity).IsAssignableFrom(typeof(TEntity)))
            {
                dbSet = dbSet.Where(e => !EF.Property<bool>(e, "IsDeleted"));
            }

            return await dbSet
                .ApplyActiveFilter(query.Request.Active)
                .ExecuteSearchAsync(query.Request, _mapper.MapToDto);
        }
    }

    public class DeleteHandler<TEntity> : IRequestHandler<DeleteCommand<TEntity>, CommandResult>
        where TEntity : BaseEntity
    {
        private readonly ApplicationDbContext _context;

        public DeleteHandler(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<CommandResult> Handle(DeleteCommand<TEntity> command, CancellationToken cancellationToken)
        {
            var entity = await _context.Set<TEntity>().FirstOrDefaultAsync(e => e.Id == command.Id, cancellationToken);

            if (entity == null) return new CommandResult(false, "Registro não encontrado", 404);

            if (entity is SoftDeletableEntity softDeletable)
            {
                if (softDeletable.IsDeleted) return new CommandResult(false, "Registro não encontrado", 404);
                softDeletable.IsDeleted = true;
                softDeletable.DeletedAt = System.DateTime.UtcNow;
            }
            else
            {
                HardDeleteEntity(entity);
            }

            await _context.SaveChangesAsync(cancellationToken);
            return new CommandResult(true, StatusCode: 204);
        }

        [ExcludeFromCodeCoverage]
        private void HardDeleteEntity(TEntity entity)
        {
            _context.Set<TEntity>().Remove(entity);
        }
    }

    public class ToggleStatusHandler<TEntity, TDto> : IRequestHandler<ToggleStatusCommand<TEntity, TDto>, CommandResult<TDto>>
        where TEntity : BaseEntity
    {
        private readonly ApplicationDbContext _context;
        private readonly IEntityMapper<TEntity, TDto> _mapper;

        public ToggleStatusHandler(ApplicationDbContext context, IEntityMapper<TEntity, TDto> mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        public async Task<CommandResult<TDto>> Handle(ToggleStatusCommand<TEntity, TDto> command, CancellationToken cancellationToken)
        {
            var entity = await _context.Set<TEntity>().FirstOrDefaultAsync(e => e.Id == command.Id, cancellationToken);
            if (entity == null) return new CommandResult<TDto>(false, Error: "Registro não encontrado", StatusCode: 404);

            if (entity is SoftDeletableEntity softDeletable && softDeletable.IsDeleted)
                return HandleSoftDeletedToggle();

            entity.Active = command.Active;
            entity.UpdatedAt = System.DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);

            return new CommandResult<TDto>(true, Data: _mapper.MapToDto(entity));
        }

        [ExcludeFromCodeCoverage]
        private static CommandResult<TDto> HandleSoftDeletedToggle()
        {
            return new CommandResult<TDto>(false, Error: "Registro não encontrado", StatusCode: 404);
        }
    }
}
