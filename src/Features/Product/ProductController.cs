using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using MageBackend.Database;
using MageBackend.Core;
using MageBackend.Core.Middleware;
using MageBackend.Core.Filters;
using FluentValidation;

namespace MageBackend.Features.Product
{
    [ApiController]
    [Route("v1/product")]
    public class ProductController : BaseApiController
    {
        private readonly ApplicationDbContext _context;
        private readonly IValidator<CreateProductDto> _createValidator;
        private static readonly string[] AllowedFields = { "name", "sku", "category", "active", "created_at", "updated_at" };

        public ProductController(ApplicationDbContext context, IValidator<CreateProductDto> createValidator)
        {
            _context = context;
            _createValidator = createValidator;
        }

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

        [HttpGet]
        [CheckPermission("product", "view")]
        [ProducesResponseType(typeof(SearchResult<ProductResponseDto>), 200)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> List()
        {
            var req = SearchRequest.Parse(Request.Query, AllowedFields, out var err);
            if (err != null) return ErrorResponse(err);

            var result = await _context.Product
                .Where(p => !p.IsDeleted)
                .ApplyActiveFilter(req.Active, forceDefaultTrue: true)
                .ExecuteSearchAsync(req, MapToDto);

            return Ok(result);
        }

        [HttpGet("all")]
        [CheckPermission("product", "view")]
        [ProducesResponseType(typeof(SearchResult<ProductResponseDto>), 200)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> ListAll()
        {
            var req = SearchRequest.Parse(Request.Query, AllowedFields, out var err);
            if (err != null) return ErrorResponse(err);

            var result = await _context.Product
                .Where(p => !p.IsDeleted)
                .ApplyActiveFilter(req.Active)
                .ExecuteSearchAsync(req, MapToDto);

            return Ok(result);
        }

        [HttpGet("{id}")]
        [CheckPermission("product", "view")]
        [ProducesResponseType(typeof(ProductResponseDto), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetById(string id)
        {
            var product = await _context.Product.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);
            if (product == null) return NotFound(new { message = "Product not found" });

            return Ok(MapToDto(product));
        }

        public record CreateProductDto
        {
            public string Name { get; init; } = string.Empty;
            public string Sku { get; init; } = string.Empty;
            public string Category { get; init; } = string.Empty;
            public decimal Price { get; init; }
            public int Stock { get; init; }
            public string? Description { get; init; }
        }

        [HttpPost]
        [CheckPermission("product", "create")]
        [ProducesResponseType(typeof(ProductResponseDto), 201)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> Create([FromBody] CreateProductDto dto)
        {
            var validationResult = await _createValidator.ValidateAsync(dto);
            if (!validationResult.IsValid)
            {
                throw new FluentValidation.ValidationException(validationResult.Errors);
            }

            var skuExists = await _context.Product.AnyAsync(p => p.Sku == dto.Sku && !p.IsDeleted);
            if (skuExists)
            {
                return BadRequest(new { message = "Product SKU already in use." });
            }

            var userId = User.FindFirst("id")?.Value;
            var product = new Database.Product
            {
                Id = Guid.NewGuid().ToString(),
                Name = dto.Name,
                Sku = dto.Sku,
                Category = dto.Category,
                Price = dto.Price,
                Stock = dto.Stock,
                Description = dto.Description,
                Active = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                IdUser = userId
            };

            _context.Product.Add(product);
            await _context.SaveChangesAsync();

            return StatusCode(201, MapToDto(product));
        }

        [HttpPut("{id}")]
        [CheckPermission("product", "create")]
        [ProducesResponseType(typeof(ProductResponseDto), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> Update(string id, [FromBody] CreateProductDto dto)
        {
            var product = await _context.Product.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);
            if (product == null) return NotFound(new { message = "Product not found" });

            if (!string.IsNullOrEmpty(dto.Sku) && dto.Sku != product.Sku)
            {
                var skuExists = await _context.Product.AnyAsync(p => p.Sku == dto.Sku && p.Id != id && !p.IsDeleted);
                if (skuExists) return BadRequest(new { message = "Product SKU already in use." });
                product.Sku = dto.Sku;
            }

            if (!string.IsNullOrEmpty(dto.Name)) product.Name = dto.Name;
            if (!string.IsNullOrEmpty(dto.Category)) product.Category = dto.Category;
            product.Price = dto.Price;
            product.Stock = dto.Stock;
            product.Description = dto.Description;
            product.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return Ok(MapToDto(product));
        }

        [HttpDelete("{id}")]
        [CheckPermission("product", "delete")]
        [ProducesResponseType(204)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> Delete(string id)
        {
            var product = await _context.Product.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);
            if (product == null) return NotFound(new { message = "Product not found" });

            product.IsDeleted = true;
            product.DeletedAt = DateTime.UtcNow;
            product.Active = false;
            product.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return NoContent();
        }

        public record ToggleStatusDto
        {
            public bool Active { get; init; }
        }

        [HttpPatch("{id}/status")]
        [CheckPermission("product", "activate")]
        [ProducesResponseType(typeof(ProductResponseDto), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> ToggleStatus(string id, [FromBody] ToggleStatusDto dto)
        {
            var product = await _context.Product.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);
            if (product == null) return NotFound(new { message = "Product not found" });

            product.Active = dto.Active;
            product.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return Ok(MapToDto(product));
        }

        private static ProductResponseDto MapToDto(Database.Product product)
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

    public class CreateProductDtoValidator : AbstractValidator<ProductController.CreateProductDto>
    {
        public CreateProductDtoValidator()
        {
            RuleFor(x => x.Name).NotEmpty().WithMessage("Name, sku, and category are required.");
            RuleFor(x => x.Sku).NotEmpty().WithMessage("Name, sku, and category are required.");
            RuleFor(x => x.Category).NotEmpty().WithMessage("Name, sku, and category are required.");
        }
    }
}
