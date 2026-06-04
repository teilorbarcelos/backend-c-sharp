using MageBackend.Database;
using MageBackend.Infrastructure.Auth;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MageBackend.Features.Role.Commands
{
    public record ToggleRoleStatusCommand(string Id, bool Active) : IRequest<ToggleRoleStatusResult>;

    public record ToggleRoleStatusResult(bool Success, RoleResponseDto? Role = null, string? Error = null, int StatusCode = 200);

    public class ToggleRoleStatusHandler : IRequestHandler<ToggleRoleStatusCommand, ToggleRoleStatusResult>
    {
        private readonly ApplicationDbContext _context;

        public ToggleRoleStatusHandler(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<ToggleRoleStatusResult> Handle(ToggleRoleStatusCommand command, CancellationToken cancellationToken)
        {
            var role = await _context.Role.Where(r => r.Id == command.Id && !r.IsDeleted).FirstOrDefaultAsync(cancellationToken);
            if (role == null) return new ToggleRoleStatusResult(false, Error: "Role not found", StatusCode: 404);

            role.Active = command.Active;
            role.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);

            var userIds = await _context.User
                .Where(u => u.IdRole == command.Id)
                .Select(u => u.Id)
                .ToListAsync(cancellationToken);

            await SessionManager.InvalidateManyUsersSessionsAsync(userIds, _context);

            var roleFeatures = await _context.RoleFeature
                .Where(rf => rf.IdRole == command.Id)
                .Select(rf => new RoleFeatureDto
                {
                    IdFeature = rf.IdFeature,
                    Create = rf.Create,
                    View = rf.View,
                    Delete = rf.Delete,
                    Activate = rf.Activate
                }).ToListAsync(cancellationToken);

            return new ToggleRoleStatusResult(true, Role: RoleMapper.MapToDto(role, roleFeatures));
        }
    }
}
