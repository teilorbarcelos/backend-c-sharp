using MageBackend.Core;

namespace MageBackend.Features.Product
{
    public class ProductEntityMapper : IEntityMapper<Database.Product, ProductResponseDto>
    {
        public ProductResponseDto MapToDto(Database.Product entity) => ProductMapper.MapToDto(entity);
    }
}
