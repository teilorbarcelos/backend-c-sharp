using MageBackend.Database;
using MageBackend.Infrastructure.Auth;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace MageBackend.Features.Auth.Commands
{
    public record ChangePasswordCommand(string Email, string Token, string Password) : IRequest<ChangePasswordResult>;

    public record ChangePasswordResult(bool Success, string? Error = null, int StatusCode = 200);

    public class ChangePasswordHandler : IRequestHandler<ChangePasswordCommand, ChangePasswordResult>
    {
        /*
         * Resposta única para qualquer falha de change-password: user não
         * existe, token inválido, token expirado. Mesmo status (401) e
         * mesma mensagem.
         *
         * Por consistência com o ValidateResetToken, a falha no flow de
         * "trocar senha" também colapsa em uma única resposta — sem vetor
         * para o atacante distinguir "este email existe" de "este email
         * não existe" durante a fase de reset.
         */
        private const string ErrorInvalidToken = "Invalid or expired reset token";

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
                Log.Warning("[Auth] Password change failed for {Email} (reason: {Reason})", command.Email, "user_not_found");
                return new ChangePasswordResult(false, Error: ErrorInvalidToken, StatusCode: 401);
            }

            if (user.Auth.RequestPasswordToken != command.Token)
            {
                Log.Warning("[Auth] Password change failed for {Email} (reason: {Reason})", command.Email, "token_mismatch");
                return new ChangePasswordResult(false, Error: ErrorInvalidToken, StatusCode: 401);
            }

            if (user.Auth.RequestPasswordExpiration.HasValue && user.Auth.RequestPasswordExpiration < DateTime.UtcNow)
            {
                Log.Warning("[Auth] Password change failed for {Email} (reason: {Reason})", command.Email, "token_expired");
                return new ChangePasswordResult(false, Error: ErrorInvalidToken, StatusCode: 401);
            }

            var hashedPassword = BCrypt.Net.BCrypt.HashPassword(command.Password, 12);
            user.Auth.Password = hashedPassword;
            user.Auth.RequestPasswordToken = null;
            user.Auth.RequestPasswordExpiration = null;
            user.Auth.Retries = 0;
            user.Auth.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);
            await SessionManager.InvalidateUserSessionsAsync(user.Id, _context);
            return new ChangePasswordResult(true);
        }
    }
}
