using MageBackend.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MageBackend.Features.Auth.Commands
{
    public record ChangePasswordCommand(string Email, string Token, string Password) : IRequest<ChangePasswordResult>;

    public record ChangePasswordResult(bool Success, string? Error = null, int StatusCode = 200);

    public class ChangePasswordHandler : IRequestHandler<ChangePasswordCommand, ChangePasswordResult>
    {
        private readonly ApplicationDbContext _context;

        public ChangePasswordHandler(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<ChangePasswordResult> Handle(ChangePasswordCommand command, CancellationToken cancellationToken)
        {
            var user = await _context.User
                .Include(u => u.Auth)
                .FirstOrDefaultAsync(u => u.Email == command.Email && !u.IsDeleted, cancellationToken);

            if (user == null || user.Auth == null)
            {
                return new ChangePasswordResult(false, Error: "User not found", StatusCode: 404);
            }

            if (user.Auth.RequestPasswordToken != command.Token)
            {
                return new ChangePasswordResult(false, Error: "Invalid reset token", StatusCode: 401);
            }

            if (user.Auth.RequestPasswordExpiration.HasValue && user.Auth.RequestPasswordExpiration < DateTime.UtcNow)
            {
                return new ChangePasswordResult(false, Error: "Reset token has expired", StatusCode: 401);
            }

            var hashedPassword = BCrypt.Net.BCrypt.HashPassword(command.Password, 12);
            user.Auth.Password = hashedPassword;
            user.Auth.RequestPasswordToken = null;
            user.Auth.RequestPasswordExpiration = null;
            user.Auth.Retries = 0;
            user.Auth.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);
            return new ChangePasswordResult(true);
        }
    }
}
