using MageBackend.Database;
using MageBackend.Infrastructure.Auth;
using MediatR;
using Microsoft.EntityFrameworkCore;
using FluentValidation;

namespace MageBackend.Features.Role.Commands
{
    public record CreateRoleCommand(string Name, string Description, List<RoleFeatureDto>? Permissions) : IRequest<CreateRoleResult>;

    public record CreateRoleResult(bool Success, RoleResponseDto? Role = null, string? Error = null);

    public class CreateRoleHandler : IRequestHandler<CreateRoleCommand, CreateRoleResult>
    {
        private readonly ApplicationDbContext _context;

        public CreateRoleHandler(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<CreateRoleResult> Handle(CreateRoleCommand command, CancellationToken cancellationToken)
        {
            var id = Slugify(command.Name);
            var exists = await _context.Role.AnyAsync(r => r.Id == id, cancellationToken);
            if (exists) return new CreateRoleResult(false, Error: "Role already exists");

            var role = new Database.Role
            {
                Id = id,
                Name = command.Name,
                Description = command.Description,
                Active = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var permissionsList = command.Permissions ?? new();
            var roleFeatures = permissionsList.Select(p => new RoleFeature
            {
                IdRole = id,
                IdFeature = p.IdFeature,
                Create = p.Create,
                View = p.View,
                Delete = p.Delete,
                Activate = p.Activate
            }).ToList();

            _context.Role.Add(role);
            await _context.RoleFeature.AddRangeAsync(roleFeatures, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            return new CreateRoleResult(true, Role: RoleMapper.MapToDto(role, permissionsList));
        }

        private static string Slugify(string name)
        {
            if (string.IsNullOrEmpty(name)) return Guid.NewGuid().ToString();
            var result = name.ToLower().Replace(" ", "-");
            var filteredChars = result.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray();
            return new string(filteredChars);
        }
    }

    public class CreateRoleDtoValidator : AbstractValidator<CreateRoleDto>
    {
        public CreateRoleDtoValidator()
        {
            RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required");
        }
    }
}
