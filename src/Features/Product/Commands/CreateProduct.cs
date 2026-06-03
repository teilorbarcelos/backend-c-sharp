using MageBackend.Core;
using MageBackend.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MageBackend.Core.Commands;
using FluentValidation;

namespace MageBackend.Features.Product.Commands
{
    public record CreateProductCommand : IRequest<CommandResult<ProductResponseDto>>
    {
        public string Name { get; init; } = string.Empty;
        public string Sku { get; init; } = string.Empty;
        public string Category { get; init; } = string.Empty;
        public required decimal Price { get; init; }
        public required int Stock { get; init; }
        public string? Description { get; init; }
        public string? UserId { get; init; }
    }

    public class CreateProductHandler : IRequestHandler<CreateProductCommand, CommandResult<ProductResponseDto>>
    {
        private readonly ApplicationDbContext _context;

        public CreateProductHandler(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<CommandResult<ProductResponseDto>> Handle(CreateProductCommand command, CancellationToken cancellationToken)
        {
            var skuExists = await _context.Product.AnyAsync(p => p.Sku == command.Sku && !p.IsDeleted, cancellationToken);
            if (skuExists)
            {
                return new CommandResult<ProductResponseDto>(false, Error: "Product SKU already in use.", StatusCode: 400);
            }

            var product = new Database.Product
            {
                Id = Guid.NewGuid().ToString(),
                Name = command.Name,
                Sku = command.Sku,
                Category = command.Category,
                Price = command.Price,
                Stock = command.Stock,
                Description = command.Description,
                Active = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                IdUser = command.UserId
            };

            _context.Product.Add(product);
            await _context.SaveChangesAsync(cancellationToken);

            return new CommandResult<ProductResponseDto>(true, Data: ProductMapper.MapToDto(product));
        }
    }

    public class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
    {
        public CreateProductCommandValidator()
        {
            RuleFor(x => x.Name).NotEmpty().WithMessage("Name, sku, and category are required.");
            RuleFor(x => x.Sku).NotEmpty().WithMessage("Name, sku, and category are required.");
            RuleFor(x => x.Category).NotEmpty().WithMessage("Name, sku, and category are required.");
        }
    }
}
