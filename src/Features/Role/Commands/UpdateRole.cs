using MageBackend.Database;
using MageBackend.Infrastructure.Auth;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MageBackend.Features.Role.Commands
{
    public record UpdateRoleCommand(string Id, string Name, string Description, List<RoleFeatureDto>? Permissions) : IRequest<UpdateRoleResult>;

    public record UpdateRoleResult(bool Success, RoleResponseDto? Role = null, string? Error = null, int StatusCode = 200);

    public class UpdateRoleHandler : IRequestHandler<UpdateRoleCommand, UpdateRoleResult>
    {
        private readonly ApplicationDbContext _context;

        public UpdateRoleHandler(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<UpdateRoleResult> Handle(UpdateRoleCommand command, CancellationToken cancellationToken)
        {
            var role = await _context.Role.AsTracking().Where(r => r.Id == command.Id && !r.IsDeleted).FirstOrDefaultAsync(cancellationToken);
            if (role == null) return new UpdateRoleResult(false, Error: "Role not found", StatusCode: 404);

            role.Name = command.Name;
            role.Description = command.Description;
            role.UpdatedAt = DateTime.UtcNow;

            List<RoleFeatureDto> returnedPermissions;

            if (command.Permissions != null)
            {
                /* Remove existing permissions */
                var existingFeatures = await _context.RoleFeature.Where(rf => rf.IdRole == command.Id).ToListAsync(cancellationToken);
                _context.RoleFeature.RemoveRange(existingFeatures);

                /* Add new permissions */
                var roleFeatures = command.Permissions.Select(p => new RoleFeature
                {
                    IdRole = command.Id,
                    IdFeature = p.IdFeature,
                    Create = p.Create,
                    View = p.View,
                    Delete = p.Delete,
                    Activate = p.Activate
                }).ToList();

                await _context.RoleFeature.AddRangeAsync(roleFeatures, cancellationToken);
                returnedPermissions = command.Permissions;
            }
            else
            {
                /* Retain existing permissions */
                var existingFeatures = await _context.RoleFeature
                    .Where(rf => rf.IdRole == command.Id)
                    .Select(rf => new RoleFeatureDto
                    {
                        IdFeature = rf.IdFeature,
                        Create = rf.Create,
                        View = rf.View,
                        Delete = rf.Delete,
                        Activate = rf.Activate
                    }).ToListAsync(cancellationToken);
                returnedPermissions = existingFeatures;
            }

            await _context.SaveChangesAsync(cancellationToken);
            await InvalidateUserSessions(command.Id, cancellationToken);

            return new UpdateRoleResult(true, Role: RoleMapper.MapToDto(role, returnedPermissions));
        }

        private async Task InvalidateUserSessions(string roleId, CancellationToken cancellationToken)
        {
            var userIds = await _context.User
                .Where(u => u.IdRole == roleId)
                .Select(u => u.Id)
                .ToListAsync(cancellationToken);

            await SessionManager.InvalidateManyUsersSessionsAsync(userIds, _context);
        }
    }
}
