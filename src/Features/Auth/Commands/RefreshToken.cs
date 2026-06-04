using System.Security.Cryptography;
using System.Text;
using MageBackend.Database;
using MageBackend.Infrastructure.Auth;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MageBackend.Features.Auth.Commands
{
    public record RefreshTokenCommand(string RefreshToken) : IRequest<LoginResult>;

    public class RefreshTokenHandler : IRequestHandler<RefreshTokenCommand, LoginResult>
    {
        private const string ErrorUserDisabled = "User not found or account is disabled/removed";
        private const string ErrorUnauthorized = "UnauthorizedError";

        private readonly ApplicationDbContext _context;
        private readonly JwtProvider _jwtProvider;

        public RefreshTokenHandler(ApplicationDbContext context, JwtProvider jwtProvider)
        {
            _context = context;
            _jwtProvider = jwtProvider;
        }

        public async Task<LoginResult> Handle(RefreshTokenCommand command, CancellationToken cancellationToken)
        {
            try
            {
                var payload = _jwtProvider.VerifyToken(command.RefreshToken);

                var refreshBytes = Encoding.UTF8.GetBytes(command.RefreshToken);
                var refreshHashBytes = SHA256.HashData(refreshBytes);
                var refreshTokenHash = Convert.ToHexString(refreshHashBytes).ToLower();

                var refreshKey = $"session:user:{payload.Id}:refresh:{refreshTokenHash}";
                var redisDb = RedisProvider.Database;

                /*
                 * Defesa primária: a chave de refresh específica desta sessão
                 * precisa existir no Redis. Quando a sessão é invalidada
                 * (SessionManager.InvalidateUserSessionsAsync), TODAS as chaves
                 * session:user:{id}:refresh:* são deletadas.
                 */
                var isValid = await redisDb.KeyExistsAsync(refreshKey);
                if (!isValid)
                {
                    return new LoginResult(false, Error: "Sessão encerrada. Por favor, faça login novamente.", ErrorKey: ErrorUnauthorized, StatusCode: 401);
                }

                /*
                 * Defesa em profundidade: além da chave existir, valida que o
                 * sv claim do refresh token bate com a SessionVersion atual do
                 * user. Cobre cenários hipotéticos em que a chave tenha
                 * sobrevivido à invalidação (bug, race condition, refactor) —
                 * mesmo padrão do TokenSessionValidationMiddleware para o JWT.
                 */
                var currentVersion = await SessionManager.GetCurrentVersionAsync(payload.Id, _context);
                if (currentVersion == null || payload.SessionVersion != currentVersion.Value)
                {
                    return new LoginResult(false, Error: "Sessão inválida ou expirada. Faça login novamente.", ErrorKey: ErrorUnauthorized, StatusCode: 401);
                }

                var user = await _context.User
                    .Include(u => u.Auth)
                    .Include(u => u.Role)
                    .FirstOrDefaultAsync(u => u.Id == payload.Id && !u.IsDeleted, cancellationToken);

                if (user == null || user.Auth == null || user.Role == null || !user.Active || user.Auth.IsDeleted || !user.Auth.Active || !user.Role.Active)
                {
                    return new LoginResult(false, Error: ErrorUserDisabled, ErrorKey: ErrorUnauthorized, StatusCode: 401);
                }

                /* Delete the old refresh token session (rotation) */
                await redisDb.KeyDeleteAsync(refreshKey);

                var response = await AuthHelper.GenerateAuthResponse(user, _context, _jwtProvider);
                return new LoginResult(true, Response: response);
            }
            catch
            {
                return new LoginResult(false, Error: "Invalid or expired refresh token", ErrorKey: ErrorUnauthorized, StatusCode: 401);
            }
        }
    }
}
