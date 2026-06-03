using Microsoft.AspNetCore.Mvc;
using MageBackend.Core;
using MageBackend.Core.Filters;
using MageBackend.Features.Product.Commands;
using MediatR;
using FluentValidation;
using System.Threading.Tasks;

namespace MageBackend.Features.Product
{
    [ApiController]
    [Route("v1/product")]
    [FeatureName("product")]
    public class ProductController : CrudControllerBase<Database.Product, ProductResponseDto, CreateProductCommand, UpdateProductCommand>
    {
        private readonly IValidator<CreateProductCommand> _createValidator;
        private static readonly string[] ProductAllowedFields = { "name", "sku", "category", "active", "created_at", "updated_at" };

        public ProductController(IMediator mediator, IValidator<CreateProductCommand> createValidator)
            : base(mediator, ProductAllowedFields)
        {
            _createValidator = createValidator;
        }

        [HttpPost]
        [CheckPermission("create")]
        [ProducesResponseType(400)]
        public override async Task<ActionResult<ProductResponseDto>> Create([FromBody] CreateProductCommand command)
        {
            var validationResult = await _createValidator.ValidateAsync(command);
            if (!validationResult.IsValid)
            {
                throw new FluentValidation.ValidationException(validationResult.Errors);
            }

            var userId = User.FindFirst("id")?.Value;
            var cmd = command with { UserId = userId };

            var result = await Mediator.Send(cmd);
            if (!result.Success) return BadRequest(new { message = result.Error });

            return StatusCode(201, result.Data);
        }
    }
}
