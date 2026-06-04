using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace MageBackend.Infrastructure.Auth
{
    public class PermissionClaim
    {
        public string Feature { get; set; } = string.Empty;
        public bool Create { get; set; }
        public bool View { get; set; }
        public bool Delete { get; set; }
        public bool Activate { get; set; }
    }

    public class AuthPayload
    {
        public string Id { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string RoleId { get; set; } = string.Empty;
        public List<PermissionClaim> Permissions { get; set; } = new();
        public int SessionVersion { get; set; } = 1;
    }

    public class TokenPair
    {
        public string Token { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
    }

    public class JwtProvider
    {
        private readonly string _secret;

        public JwtProvider(string secret)
        {
            _secret = secret;
        }

        public string GenerateToken(AuthPayload payload, TimeSpan expiresIn)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_secret);

            var claims = new List<Claim>
            {
                new Claim("id", payload.Id),
                new Claim("email", payload.Email),
                new Claim("roleId", payload.RoleId),
                new Claim("permissions", JsonSerializer.Serialize(payload.Permissions)),
                new Claim("sv", payload.SessionVersion.ToString())
            };

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.Add(expiresIn),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        public TokenPair GenerateTokenPair(AuthPayload payload)
        {
            var token = GenerateToken(payload, TimeSpan.FromHours(1));
            var refreshToken = GenerateToken(payload, TimeSpan.FromDays(7));
            return new TokenPair { Token = token, RefreshToken = refreshToken };
        }

        public AuthPayload VerifyToken(string token)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_secret);

            try
            {
                tokenHandler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ClockSkew = TimeSpan.Zero
                }, out SecurityToken validatedToken);

                var jwtToken = (JwtSecurityToken)validatedToken;
                var id = jwtToken.Claims.First(x => x.Type == "id").Value;
                var email = jwtToken.Claims.First(x => x.Type == "email").Value;
                var roleId = jwtToken.Claims.First(x => x.Type == "roleId").Value;
                var permissionsJson = jwtToken.Claims.First(x => x.Type == "permissions").Value;
                var svString = jwtToken.Claims.FirstOrDefault(x => x.Type == "sv")?.Value ?? "1";

                var permissions = JsonSerializer.Deserialize<List<PermissionClaim>>(permissionsJson) ?? new();
                _ = int.TryParse(svString, out var sv);

                return new AuthPayload
                {
                    Id = id,
                    Email = email,
                    RoleId = roleId,
                    Permissions = permissions,
                    SessionVersion = sv
                };
            }
            catch (Exception ex)
            {
                throw new UnauthorizedAccessException("Invalid token", ex);
            }
        }
    }
}
