using System;
using System.Text.Json.Serialization;

namespace MageBackend.Features.Product
{
    public record ProductResponseDto
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Sku { get; init; } = string.Empty;
        public string Category { get; init; } = string.Empty;
        public decimal Price { get; init; }
        public int Stock { get; init; }
        public string? Description { get; init; }
        public bool Active { get; init; }
        [JsonPropertyName("is_deleted")]
        public bool IsDeleted { get; init; }
        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; init; }
        [JsonPropertyName("updated_at")]
        public DateTime UpdatedAt { get; init; }
    }

    public static class ProductMapper
    {
        public static ProductResponseDto MapToDto(Database.Product product)
        {
            return new ProductResponseDto
            {
                Id = product.Id,
                Name = product.Name,
                Sku = product.Sku,
                Category = product.Category,
                Price = product.Price,
                Stock = product.Stock,
                Description = product.Description,
                Active = product.Active,
                IsDeleted = product.IsDeleted,
                CreatedAt = product.CreatedAt,
                UpdatedAt = product.UpdatedAt
            };
        }
    }
}
