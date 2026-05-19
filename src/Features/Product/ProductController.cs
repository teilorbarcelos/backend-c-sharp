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

namespace MageBackend.Features.Product
{
    [ApiController]
    [Route("v1/product")]
    public class ProductController : BaseApiController
    {
        private readonly ApplicationDbContext _context;
        private static readonly string[] AllowedFields = { "name", "sku", "category", "active", "created_at", "updated_at" };

        public ProductController(ApplicationDbContext context)
        {
            _context = context;
        }

        public class ProductResponseDto
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Sku { get; set; } = string.Empty;
            public string Category { get; set; } = string.Empty;
            public decimal Price { get; set; }
            public int Stock { get; set; }
            public string? Description { get; set; }
            public bool Active { get; set; }
            [JsonPropertyName("is_deleted")]
            public bool IsDeleted { get; set; }
            [JsonPropertyName("created_at")]
            public DateTime CreatedAt { get; set; }
            [JsonPropertyName("updated_at")]
            public DateTime UpdatedAt { get; set; }
        }

        [HttpGet]
        [CheckPermission("product", "view")]
        public async Task<IActionResult> List()
        {
            var req = SearchRequest.Parse(Request.Query, AllowedFields, out var err);
            if (err != null) return ErrorResponse(err);

            var query = _context.Product.Where(p => !p.IsDeleted);

            if (req.Active.HasValue)
            {
                query = query.Where(p => p.Active == req.Active.Value);
            }
            else
            {
                query = query.Where(p => p.Active == true);
            }

            return await ExecuteSearch(query, req);
        }

        [HttpGet("all")]
        [CheckPermission("product", "view")]
        public async Task<IActionResult> ListAll()
        {
            var req = SearchRequest.Parse(Request.Query, AllowedFields, out var err);
            if (err != null) return ErrorResponse(err);

            var query = _context.Product.Where(p => !p.IsDeleted);

            if (req.Active.HasValue)
            {
                query = query.Where(p => p.Active == req.Active.Value);
            }

            return await ExecuteSearch(query, req);
        }

        [HttpGet("{id}")]
        [CheckPermission("product", "view")]
        public async Task<IActionResult> GetById(string id)
        {
            var product = await _context.Product.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);
            if (product == null) return NotFound(new { message = "Product not found" });

            return Ok(MapToDto(product));
        }

        public class CreateProductDto
        {
            public string Name { get; set; } = string.Empty;
            public string Sku { get; set; } = string.Empty;
            public string Category { get; set; } = string.Empty;
            public decimal Price { get; set; }
            public int Stock { get; set; }
            public string? Description { get; set; }
        }

        [HttpPost]
        [CheckPermission("product", "create")]
        public async Task<IActionResult> Create([FromBody] CreateProductDto dto)
        {
            if (string.IsNullOrEmpty(dto.Name) || string.IsNullOrEmpty(dto.Sku) || string.IsNullOrEmpty(dto.Category))
            {
                return BadRequest(new { message = "Name, sku, and category are required." });
            }

            var skuExists = await _context.Product.AnyAsync(p => p.Sku == dto.Sku && !p.IsDeleted);
            if (skuExists)
            {
                return BadRequest(new { message = "Product SKU already in use." });
            }

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
                UpdatedAt = DateTime.UtcNow
            };

            _context.Product.Add(product);
            await _context.SaveChangesAsync();

            return StatusCode(201, MapToDto(product));
        }

        [HttpPut("{id}")]
        [CheckPermission("product", "create")]
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

        public class ToggleStatusDto
        {
            public bool Active { get; set; }
        }

        [HttpPatch("{id}/status")]
        [CheckPermission("product", "activate")]
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

        private async Task<IActionResult> ExecuteSearch(IQueryable<Database.Product> query, SearchRequest req)
        {
            // Apply SearchWord
            if (!string.IsNullOrEmpty(req.SearchWord) && !string.IsNullOrEmpty(req.SearchFields))
            {
                var fields = req.SearchFields.Split(',').Select(f => f.Trim().ToLower()).ToList();
                query = query.Where(p =>
                    (fields.Contains("name") && p.Name.Contains(req.SearchWord)) ||
                    (fields.Contains("sku") && p.Sku.Contains(req.SearchWord)) ||
                    (fields.Contains("category") && p.Category.Contains(req.SearchWord))
                );
            }

            // Apply Date Filters
            if (req.CreatedAtStart.HasValue)
            {
                query = query.Where(p => p.CreatedAt >= req.CreatedAtStart.Value);
            }
            if (req.CreatedAtEnd.HasValue)
            {
                query = query.Where(p => p.CreatedAt <= req.CreatedAtEnd.Value);
            }

            var total = await query.CountAsync();

            // Apply Sorting
            if (!string.IsNullOrEmpty(req.OrderBy))
            {
                var isDesc = req.OrderDirection.Equals("desc", StringComparison.OrdinalIgnoreCase);
                query = req.OrderBy.ToLower() switch
                {
                    "name" => isDesc ? query.OrderByDescending(p => p.Name) : query.OrderBy(p => p.Name),
                    "sku" => isDesc ? query.OrderByDescending(p => p.Sku) : query.OrderBy(p => p.Sku),
                    "category" => isDesc ? query.OrderByDescending(p => p.Category) : query.OrderBy(p => p.Category),
                    _ => isDesc ? query.OrderByDescending(p => p.CreatedAt) : query.OrderBy(p => p.CreatedAt)
                };
            }
            else
            {
                query = query.OrderByDescending(p => p.CreatedAt);
            }

            // Apply Pagination
            var items = await query.Skip(req.Page * req.Size).Take(req.Size).ToListAsync();

            var dtos = items.Select(MapToDto).ToList();
            return Ok(new SearchResult<ProductResponseDto>(dtos, total, req.Page, req.Size));
        }
    }
}
