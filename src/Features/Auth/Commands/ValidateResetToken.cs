using MageBackend.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace MageBackend.Features.Auth.Commands
{
    public record ValidateResetTokenCommand(string Email, string Token) : IRequest<ValidateResetTokenResult>;

    public record ValidateResetTokenResult(bool Success, string? Error = null, int StatusCode = 200);

    public class ValidateResetTokenHandler : IRequestHandler<ValidateResetTokenCommand, ValidateResetTokenResult>
    {
        /*
         * Resposta única para qualquer falha de validação de reset token: user
         * não existe, token inválido, token expirado. Mesmo status (401) e
         * mesma mensagem.
         *
         * O motivo real continua sendo logado internamente para o SOC detectar
         * tentativas de enumeração (e.g., spike de "user_not_found" para um
         * domínio específico).
         */
        private const string ErrorInvalidToken = "Invalid or expired reset token";

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
                Log.Warning("[Auth] Reset token validation failed for {Email} (reason: {Reason})", command.Email, "user_not_found");
                return new ValidateResetTokenResult(false, Error: ErrorInvalidToken, StatusCode: 401);
            }

            if (user.Auth.RequestPasswordToken != command.Token)
            {
                Log.Warning("[Auth] Reset token validation failed for {Email} (reason: {Reason})", command.Email, "token_mismatch");
                return new ValidateResetTokenResult(false, Error: ErrorInvalidToken, StatusCode: 401);
            }

            if (user.Auth.RequestPasswordExpiration.HasValue && user.Auth.RequestPasswordExpiration < DateTime.UtcNow)
            {
                Log.Warning("[Auth] Reset token validation failed for {Email} (reason: {Reason})", command.Email, "token_expired");
                return new ValidateResetTokenResult(false, Error: ErrorInvalidToken, StatusCode: 401);
            }

            return new ValidateResetTokenResult(true);
        }
    }
}
