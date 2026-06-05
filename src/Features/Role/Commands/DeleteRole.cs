using MageBackend.Database;
using MageBackend.Infrastructure.Auth;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MageBackend.Features.Role.Commands
{
    public record DeleteRoleCommand(string Id) : IRequest<DeleteRoleResult>;

    public record DeleteRoleResult(bool Success, string? Error = null, int StatusCode = 204);

    public class DeleteRoleHandler : IRequestHandler<DeleteRoleCommand, DeleteRoleResult>
    {
        private readonly ApplicationDbContext _context;

        public DeleteRoleHandler(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<DeleteRoleResult> Handle(DeleteRoleCommand command, CancellationToken cancellationToken)
        {
            var role = await _context.Role.AsTracking().Where(r => r.Id == command.Id && !r.IsDeleted).FirstOrDefaultAsync(cancellationToken);
            if (role == null) return new DeleteRoleResult(false, Error: "Role not found", StatusCode: 404);

            role.IsDeleted = true;
            role.DeletedAt = DateTime.UtcNow;
            role.Active = false;
            role.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);

            var userIds = await _context.User
                .Where(u => u.IdRole == command.Id)
                .Select(u => u.Id)
                .ToListAsync(cancellationToken);

            await SessionManager.InvalidateManyUsersSessionsAsync(userIds, _context);

            return new DeleteRoleResult(true);
        }
    }
}
