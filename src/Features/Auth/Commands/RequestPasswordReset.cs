using MageBackend.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Security.Cryptography;

namespace MageBackend.Features.Auth.Commands
{
    public record RequestPasswordResetCommand(string Email) : IRequest<Unit>;

    public class RequestPasswordResetHandler : IRequestHandler<RequestPasswordResetCommand, Unit>
    {
        private readonly ApplicationDbContext _context;

        public RequestPasswordResetHandler(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<Unit> Handle(RequestPasswordResetCommand command, CancellationToken cancellationToken)
        {
            var user = await _context.User
                .Include(u => u.Auth)
                .AsTracking()
                .FirstOrDefaultAsync(u => u.Email == command.Email && !u.IsDeleted, cancellationToken);

            if (user != null && user.Auth != null)
            {
                var resetToken = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
                var expiration = DateTime.UtcNow.AddMinutes(15);

                user.Auth.RequestPasswordToken = resetToken;
                user.Auth.RequestPasswordExpiration = expiration;
                user.Auth.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync(cancellationToken);
                Log.Information("[PasswordReset] Reset code for {Email}: {ResetToken}", command.Email, resetToken);
            }

            return Unit.Value;
        }
    }
}
