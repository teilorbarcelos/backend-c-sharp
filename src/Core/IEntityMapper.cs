namespace MageBackend.Core
{
    public interface IEntityMapper<in TEntity, out TDto>
    {
        TDto MapToDto(TEntity entity);
    }
}
