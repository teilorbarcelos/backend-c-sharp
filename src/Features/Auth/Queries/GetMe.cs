using MageBackend.Database;
using MageBackend.Infrastructure.Auth;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MageBackend.Features.Auth.Queries
{
    public record GetMeQuery(string? UserId, string? AuthorizationHeader) : IRequest<GetMeResult>;

    public record GetMeResult(bool Success, AuthResponseDto? Response = null, string? Error = null, int StatusCode = 200);

    public class GetMeHandler : IRequestHandler<GetMeQuery, GetMeResult>
    {
        private readonly ApplicationDbContext _context;

        public GetMeHandler(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<GetMeResult> Handle(GetMeQuery query, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(query.UserId))
            {
                return new GetMeResult(false, Error: "Usuário não autenticado", StatusCode: 401);
            }

            var user = await _context.User
                .Include(u => u.Auth)
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Id == query.UserId && !u.IsDeleted, cancellationToken);

            if (user == null || user.Auth == null || !user.Active || user.Auth.IsDeleted || !user.Auth.Active)
            {
                return new GetMeResult(false, Error: "User not found or account is disabled/removed", StatusCode: 401);
            }

            var permissions = await _context.RoleFeature
                .Where(rf => rf.IdRole == user.IdRole)
                .Select(rf => new PermissionClaim
                {
                    Feature = rf.IdFeature,
                    Create = rf.Create,
                    View = rf.View,
                    Delete = rf.Delete,
                    Activate = rf.Activate
                }).ToListAsync(cancellationToken);

            var response = new AuthResponseDto
            {
                Token = query.AuthorizationHeader?.Replace("Bearer ", "") ?? "",
                RefreshToken = "", /* No refresh token returned on getMe */
                User = new AuthUserDto
                {
                    Id = user.Id,
                    Name = user.Name,
                    Email = user.Email,
                    Role = new AuthUserRoleDto
                    {
                        Id = user.IdRole,
                        Name = user.Role?.Name ?? "",
                        Description = user.Role?.Description,
                        Permissions = permissions
                    }
                }
            };

            return new GetMeResult(true, Response: response);
        }
    }
}
