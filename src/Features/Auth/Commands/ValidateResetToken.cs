using MageBackend.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MageBackend.Features.Auth.Commands
{
    public record ValidateResetTokenCommand(string Email, string Token) : IRequest<ValidateResetTokenResult>;

    public record ValidateResetTokenResult(bool Success, string? Error = null, int StatusCode = 200);

    public class ValidateResetTokenHandler : IRequestHandler<ValidateResetTokenCommand, ValidateResetTokenResult>
    {
        private readonly ApplicationDbContext _context;

        public ValidateResetTokenHandler(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<ValidateResetTokenResult> Handle(ValidateResetTokenCommand command, CancellationToken cancellationToken)
        {
            var user = await _context.User
                .Include(u => u.Auth)
                .FirstOrDefaultAsync(u => u.Email == command.Email && !u.IsDeleted, cancellationToken);

            if (user == null || user.Auth == null)
            {
                return new ValidateResetTokenResult(false, Error: "User not found", StatusCode: 404);
            }

            if (user.Auth.RequestPasswordToken != command.Token)
            {
                return new ValidateResetTokenResult(false, Error: "Invalid reset token", StatusCode: 401);
            }

            if (user.Auth.RequestPasswordExpiration.HasValue && user.Auth.RequestPasswordExpiration < DateTime.UtcNow)
            {
                return new ValidateResetTokenResult(false, Error: "Reset token has expired", StatusCode: 401);
            }

            return new ValidateResetTokenResult(true);
        }
    }
}
