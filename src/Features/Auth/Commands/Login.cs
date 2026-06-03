using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MageBackend.Database;
using MageBackend.Infrastructure.Auth;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MageBackend.Features.Auth.Commands
{
    public record LoginCommand(string Email, string Password) : IRequest<LoginResult>;

    public record LoginResult(bool Success, AuthResponseDto? Response = null, string? Error = null, string? ErrorKey = null, int StatusCode = 200);

    public class LoginHandler : IRequestHandler<LoginCommand, LoginResult>
    {
        private const string ErrorUserDisabled = "User not found or account is disabled/removed";
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

            if (user == null || user.Auth == null || user.Role == null || !user.Active || user.Auth.IsDeleted || !user.Auth.Active || !user.Role.Active)
            {
                return new LoginResult(false, Error: ErrorUserDisabled, ErrorKey: ErrorUnauthorized, StatusCode: 401);
            }

            var passwordMatches = BCrypt.Net.BCrypt.Verify(command.Password, user.Auth.Password);
            if (!passwordMatches)
            {
                return new LoginResult(false, Error: "Invalid email or password", ErrorKey: ErrorUnauthorized, StatusCode: 401);
            }

            var response = await AuthHelper.GenerateAuthResponse(user, _context, _jwtProvider);
            return new LoginResult(true, Response: response);
        }
    }
}
