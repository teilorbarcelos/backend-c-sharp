using MageBackend.Database;
using MageBackend.Infrastructure.Auth;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace MageBackend.Features.Auth.Commands
{
    public record LoginCommand(string Email, string Password) : IRequest<LoginResult>;

    public record LoginResult(bool Success, AuthResponseDto? Response = null, string? Error = null, string? ErrorKey = null, int StatusCode = 200);

    public class LoginHandler : IRequestHandler<LoginCommand, LoginResult>
    {
        /*
         * Resposta única para TODAS as falhas de autenticação: user não existe,
         * user deletado, role inativa, user inativo, auth inativo, senha errada.
         *
         * Vetor de enumeração fechado: atacante não consegue distinguir
         * "este email não está cadastrado" de "senha errada" via response body,
         * status code ou headers. Mesmo ErrorKey para que o client consiga
         * tratar programaticamente.
         *
         * O motivo real continua sendo logado internamente (Log.Warning com
         * tag de reason) para que o SOC possa detectar tentativas de
         * enumeration (e.g., spike de "user_not_found" para um domínio).
         */
        private const string ErrorInvalidCredentials = "Invalid email or password";
        private const string ErrorUnauthorized = "UnauthorizedError";

        private readonly ApplicationDbContext _context;
        private readonly JwtProvider _jwtProvider;

        public LoginHandler(ApplicationDbContext context, JwtProvider jwtProvider)
        {
            _context = context;
            _jwtProvider = jwtProvider;
        }

        public async Task<LoginResult> Handle(LoginCommand command, CancellationToken cancellationToken)
        {
            var user = await _context.User
                .Include(u => u.Auth)
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Email == command.Email && !u.IsDeleted, cancellationToken);

            if (!IsLoginable(user, out var reason))
            {
                Log.Warning("[Auth] Login failed for {Email} (reason: {Reason})", command.Email, reason);
                return new LoginResult(false, Error: ErrorInvalidCredentials, ErrorKey: ErrorUnauthorized, StatusCode: 401);
            }

            if (!BCrypt.Net.BCrypt.Verify(command.Password, user!.Auth!.Password))
            {
                Log.Warning("[Auth] Login failed for {Email} (reason: {Reason})", command.Email, "wrong_password");
                return new LoginResult(false, Error: ErrorInvalidCredentials, ErrorKey: ErrorUnauthorized, StatusCode: 401);
            }

            var response = await AuthHelper.GenerateAuthResponse(user, _context, _jwtProvider);
            return new LoginResult(true, Response: response);
        }

        /*
         * Decisão de loginability extraída para manter Cognitive Complexity
         * do Handle abaixo de 15. Cada estado terminal tem um reason
         * distinto para o log do SOC.
         */
        private static bool IsLoginable(Database.User? user, out string reason)
        {
            if (user == null) { reason = "user_not_found_or_deleted"; return false; }
            if (user.Auth == null) { reason = "auth_record_missing"; return false; }
            if (user.Role == null) { reason = "role_missing"; return false; }
            if (!user.Active) { reason = "user_inactive"; return false; }
            if (user.Auth.IsDeleted) { reason = "auth_deleted"; return false; }
            if (!user.Auth.Active) { reason = "auth_inactive"; return false; }
            if (!user.Role.Active) { reason = "role_inactive"; return false; }
            reason = "ok";
            return true;
        }
    }
}
