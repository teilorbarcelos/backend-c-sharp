using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using MageBackend.Database;
using MageBackend.Infrastructure.Auth;
using MediatR;
using Microsoft.EntityFrameworkCore;
using FluentValidation;
using MageBackend.Shared.Cqrs;

namespace MageBackend.Features.User.Commands
{
    public record UpdateUserCommand : IRequest<CommandResult<UserResponseDto>>, Core.ICommandWithId
    {
        public string Id { get; init; } = string.Empty;
        public string? Name { get; init; }
        public string? Email { get; init; }
        public string? Password { get; init; }
        public string? Phone { get; init; }
        public string? Document { get; init; }
        public string? Avatar { get; init; }
        [JsonPropertyName("id_role")]
        public string? IdRole { get; init; }
        public bool? Active { get; init; }

        [ExcludeFromCodeCoverage]
        public void SetId(string id) => typeof(UpdateUserCommand).GetProperty("Id")!.SetValue(this, id);
    }

    public class UpdateUserHandler : IRequestHandler<UpdateUserCommand, CommandResult<UserResponseDto>>
    {
        private readonly ApplicationDbContext _context;

        public UpdateUserHandler(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<CommandResult<UserResponseDto>> Handle(UpdateUserCommand command, CancellationToken cancellationToken)
        {
            var user = await _context.User.Include(u => u.Auth).AsTracking().FirstOrDefaultAsync(u => u.Id == command.Id && !u.IsDeleted, cancellationToken);
            if (user == null) return new CommandResult<UserResponseDto>(false, Error: "User not found", StatusCode: 404);

            var adminEmail = Environment.GetEnvironmentVariable("FIRST_USER") ?? "admin@email.com";

            if (user.Email == adminEmail)
            {
                return await UpdateAdminUserAsync(user, command);
            }

            var error = await UpdateStandardUserAsync(user, command);
            if (error != null) return new CommandResult<UserResponseDto>(false, Error: error, StatusCode: 400);

            return new CommandResult<UserResponseDto>(true, Data: UserMapper.MapToDto(user));
        }

        private async Task<CommandResult<UserResponseDto>> UpdateAdminUserAsync(Database.User user, UpdateUserCommand command)
        {
            if (!string.IsNullOrEmpty(command.Password) && user.Auth != null)
            {
                user.Auth.Password = BCrypt.Net.BCrypt.HashPassword(command.Password, 12);
                user.Auth.UpdatedAt = DateTime.UtcNow;
            }
            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            await SessionManager.InvalidateUserSessionsAsync(user.Id, _context);
            return new CommandResult<UserResponseDto>(true, Data: UserMapper.MapToDto(user));
        }

        private async Task<string?> UpdateStandardUserAsync(Database.User user, UpdateUserCommand command)
        {
            if (command.Name != null) user.Name = command.Name;
            if (command.Email != null)
            {
                var emailExists = await _context.User.AnyAsync(u => u.Email == command.Email && u.Id != command.Id && !u.IsDeleted);
                if (emailExists) return "Email already in use.";
                user.Email = command.Email;
            }
            if (command.Phone != null) user.Phone = command.Phone;
            if (command.Document != null) user.Document = command.Document;
            if (command.Avatar != null) user.Avatar = command.Avatar;
            if (command.Active.HasValue) user.Active = command.Active.Value;

            if (!string.IsNullOrEmpty(command.IdRole))
            {
                var roleExists = await _context.Role.AnyAsync(r => r.Id == command.IdRole && !r.IsDeleted);
                if (!roleExists) return "Role not found.";
                user.IdRole = command.IdRole;
            }

            if (!string.IsNullOrEmpty(command.Password) && user.Auth != null)
            {
                user.Auth.Password = BCrypt.Net.BCrypt.HashPassword(command.Password, 12);
                user.Auth.UpdatedAt = DateTime.UtcNow;
            }

            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            await SessionManager.InvalidateUserSessionsAsync(user.Id, _context);
            return null;
        }
    }

    public class UpdateUserCommandValidator : AbstractValidator<UpdateUserCommand>
    {
        public UpdateUserCommandValidator()
        {
            RuleFor(x => x.Password)
                .MinimumLength(6)
                .When(x => !string.IsNullOrEmpty(x.Password))
                .WithMessage("Password must be at least 6 characters long.");
        }
    }
}
