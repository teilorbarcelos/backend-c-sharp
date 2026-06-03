using MageBackend.Infrastructure.Auth;
using MediatR;

namespace MageBackend.Features.Auth.Commands
{
    public record LogoutCommand(string? UserId) : IRequest<Unit>;

    public class LogoutHandler : IRequestHandler<LogoutCommand, Unit>
    {
        public async Task<Unit> Handle(LogoutCommand command, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrEmpty(command.UserId))
            {
                await SessionManager.InvalidateUserSessionsAsync(command.UserId);
            }

            return Unit.Value;
        }
    }
}
