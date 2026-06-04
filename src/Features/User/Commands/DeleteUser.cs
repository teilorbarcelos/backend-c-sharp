using MageBackend.Database;
using MageBackend.Infrastructure.Auth;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MageBackend.Features.User.Commands
{
    public record DeleteUserCommand(string Id) : IRequest<DeleteUserResult>;

    public record DeleteUserResult(bool Success, string? Error = null, int StatusCode = 204);

    public class DeleteUserHandler : IRequestHandler<DeleteUserCommand, DeleteUserResult>
    {
        private readonly ApplicationDbContext _context;

        public DeleteUserHandler(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<DeleteUserResult> Handle(DeleteUserCommand command, CancellationToken cancellationToken)
        {
            var user = await _context.User.FirstOrDefaultAsync(u => u.Id == command.Id && !u.IsDeleted, cancellationToken);
            if (user == null) return new DeleteUserResult(false, Error: "User not found", StatusCode: 404);

            var adminEmail = Environment.GetEnvironmentVariable("FIRST_USER") ?? "admin@email.com";
            if (user.Email == adminEmail)
            {
                return new DeleteUserResult(false, Error: "O usuário administrador inicial não pode ser excluído.", StatusCode: 400);
            }

            /* LGPD Anonymization */
            user.Name = "Deleted User";
            user.Email = $"deleted-{command.Id}@anonymized.local";
            user.Phone = null;
            user.Document = null;
            user.Avatar = null;
            user.IsDeleted = true;
            user.DeletedAt = DateTime.UtcNow;
            user.Active = false;
            user.UpdatedAt = DateTime.UtcNow;

            if (!string.IsNullOrEmpty(user.IdAuth))
            {
                var auth = await _context.Auth.FindAsync(new object[] { user.IdAuth }, cancellationToken);
                if (auth != null)
                {
                    auth.IsDeleted = true;
                    auth.DeletedAt = DateTime.UtcNow;
                    auth.Active = false;
                    auth.UpdatedAt = DateTime.UtcNow;
                }
            }

            await _context.SaveChangesAsync(cancellationToken);
            await SessionManager.InvalidateUserSessionsAsync(command.Id, _context);

            return new DeleteUserResult(true);
        }
    }
}
