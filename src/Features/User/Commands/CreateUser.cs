using System.Text.Json.Serialization;
using MageBackend.Database;
using MageBackend.Infrastructure.Auth;
using MediatR;
using Microsoft.EntityFrameworkCore;
using FluentValidation;

using MageBackend.Shared.Cqrs;

namespace MageBackend.Features.User.Commands
{
    public record CreateUserCommand : IRequest<CommandResult<UserResponseDto>>
    {
        public string Name { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public string Password { get; init; } = string.Empty;
        public string? Phone { get; init; }
        public string? Document { get; init; }
        public string? Avatar { get; init; }
        [JsonPropertyName("id_role")]
        public string IdRole { get; init; } = string.Empty;
    }

    public class CreateUserHandler : IRequestHandler<CreateUserCommand, CommandResult<UserResponseDto>>
    {
        private readonly ApplicationDbContext _context;

        public CreateUserHandler(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<CommandResult<UserResponseDto>> Handle(CreateUserCommand command, CancellationToken cancellationToken)
        {
            var emailExists = await _context.User.AnyAsync(u => u.Email == command.Email && !u.IsDeleted, cancellationToken);
            if (emailExists)
            {
                return new CommandResult<UserResponseDto>(false, Error: "Email already in use.", StatusCode: 400);
            }

            var roleExists = await _context.Role.AnyAsync(r => r.Id == command.IdRole && !r.IsDeleted, cancellationToken);
            if (!roleExists)
            {
                return new CommandResult<UserResponseDto>(false, Error: "Role not found.", StatusCode: 400);
            }

            var hashedPassword = BCrypt.Net.BCrypt.HashPassword(command.Password, 10);
            var auth = new Database.Auth
            {
                Id = Guid.NewGuid().ToString(),
                Password = hashedPassword,
                Active = true,
                FirstAccess = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var user = new Database.User
            {
                Id = Guid.NewGuid().ToString(),
                Name = command.Name,
                Email = command.Email,
                Phone = command.Phone,
                Document = command.Document,
                Avatar = command.Avatar,
                Active = true,
                IdRole = command.IdRole,
                IdAuth = auth.Id,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Auth.Add(auth);
            _context.User.Add(user);
            await _context.SaveChangesAsync(cancellationToken);

            return new CommandResult<UserResponseDto>(true, Data: UserMapper.MapToDto(user));
        }
    }

    public class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
    {
        private const string RequiredMessage = "Name, email, password, and id_role are required.";

        public CreateUserCommandValidator()
        {
            RuleFor(x => x.Name).NotEmpty().WithMessage(RequiredMessage);
            RuleFor(x => x.Email).NotEmpty().WithMessage(RequiredMessage);
            RuleFor(x => x.Password).NotEmpty().WithMessage(RequiredMessage);
            RuleFor(x => x.IdRole).NotEmpty().WithMessage(RequiredMessage);

            RuleFor(x => x.Password)
                .MinimumLength(6)
                .When(x => !string.IsNullOrEmpty(x.Password))
                .WithMessage("Password must be at least 6 characters long.");
        }
    }
}
