using MageBackend.Database;
using MageBackend.Infrastructure.Auth;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MageBackend.Features.User.Commands
{
    public record ToggleUserStatusCommand(string Id, bool Active) : IRequest<ToggleUserStatusResult>;

    public record ToggleUserStatusResult(bool Success, UserResponseDto? User = null, string? Error = null, int StatusCode = 200);

    public class ToggleUserStatusHandler : IRequestHandler<ToggleUserStatusCommand, ToggleUserStatusResult>
    {
        private readonly ApplicationDbContext _context;

        public ToggleUserStatusHandler(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<ToggleUserStatusResult> Handle(ToggleUserStatusCommand command, CancellationToken cancellationToken)
        {
            var user = await _context.User.FirstOrDefaultAsync(u => u.Id == command.Id && !u.IsDeleted, cancellationToken);
            if (user == null) return new ToggleUserStatusResult(false, Error: "User not found", StatusCode: 404);

            var adminEmail = Environment.GetEnvironmentVariable("FIRST_USER") ?? "admin@email.com";
            if (user.Email == adminEmail && !command.Active)
            {
                return new ToggleUserStatusResult(false, Error: "O usuário administrador inicial não pode ser desativado.", StatusCode: 400);
            }

            user.Active = command.Active;
            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);
            await SessionManager.InvalidateUserSessionsAsync(command.Id, _context);

            return new ToggleUserStatusResult(true, User: UserMapper.MapToDto(user));
        }
    }
}
