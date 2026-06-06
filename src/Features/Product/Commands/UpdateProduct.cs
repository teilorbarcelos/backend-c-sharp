using MageBackend.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MageBackend.Core;
using MageBackend.Shared.Cqrs;

namespace MageBackend.Features.Product.Commands
{
    public record UpdateProductCommand : IRequest<CommandResult<ProductResponseDto>>, ICommandWithId
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Sku { get; init; } = string.Empty;
        public string Category { get; init; } = string.Empty;
        public required decimal Price { get; init; }
        public required int Stock { get; init; }
        public string? Description { get; init; }

        public void SetId(string id) => Id = id;
    }

    public class UpdateProductHandler : IRequestHandler<UpdateProductCommand, CommandResult<ProductResponseDto>>
    {
        private readonly ApplicationDbContext _context;

        public UpdateProductHandler(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<CommandResult<ProductResponseDto>> Handle(UpdateProductCommand command, CancellationToken cancellationToken)
        {
            var product = await _context.Product.AsTracking().FirstOrDefaultAsync(p => p.Id == command.Id && !p.IsDeleted, cancellationToken);
            if (product == null) return new CommandResult<ProductResponseDto>(false, Error: "Product not found", StatusCode: 404);

            if (!string.IsNullOrEmpty(command.Sku) && command.Sku != product.Sku)
            {
                var skuExists = await _context.Product.AnyAsync(p => p.Sku == command.Sku && p.Id != command.Id && !p.IsDeleted, cancellationToken);
                if (skuExists) return new CommandResult<ProductResponseDto>(false, Error: "Product SKU already in use.", StatusCode: 400);
                product.Sku = command.Sku;
            }

            if (!string.IsNullOrEmpty(command.Name)) product.Name = command.Name;
            if (!string.IsNullOrEmpty(command.Category)) product.Category = command.Category;
            product.Price = command.Price;
            product.Stock = command.Stock;
            product.Description = command.Description;
            product.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);
            return new CommandResult<ProductResponseDto>(true, Data: ProductMapper.MapToDto(product));
        }
    }
}
