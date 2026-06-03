using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

                var isValid = await redisDb.KeyExistsAsync(refreshKey);
                if (!isValid)
                {
                    return new LoginResult(false, Error: "Sessão encerrada. Por favor, faça login novamente.", ErrorKey: ErrorUnauthorized, StatusCode: 401);
                }

                var user = await _context.User
                    .Include(u => u.Auth)
                    .Include(u => u.Role)
                    .FirstOrDefaultAsync(u => u.Id == payload.Id && !u.IsDeleted, cancellationToken);

                if (user == null || user.Auth == null || user.Role == null || !user.Active || user.Auth.IsDeleted || !user.Auth.Active || !user.Role.Active)
                {
                    return new LoginResult(false, Error: ErrorUserDisabled, ErrorKey: ErrorUnauthorized, StatusCode: 401);
                }

                /* Delete the old refresh token session */
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
