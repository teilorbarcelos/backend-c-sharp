using Microsoft.AspNetCore.Mvc;
using MageBackend.Core;
using MageBackend.Core.Filters;
using MageBackend.Features.Auth.Commands;
using MageBackend.Features.Auth.Queries;
using MediatR;
using FluentValidation;

namespace MageBackend.Features.Auth
{
    [ApiController]
    [Route("v1/auth")]
    public class AuthController : BaseApiController
    {
        private readonly IMediator _mediator;
        private readonly IValidator<LoginDto> _loginValidator;
        private readonly IValidator<RefreshDto> _refreshValidator;
        private readonly IValidator<ResetRequestDto> _resetRequestValidator;
        private readonly IValidator<ResetValidateDto> _resetValidateValidator;
        private readonly IValidator<ChangePasswordDto> _changePasswordValidator;

        public AuthController(
            IMediator mediator,
            IValidator<LoginDto> loginValidator,
            IValidator<RefreshDto> refreshValidator,
            IValidator<ResetRequestDto> resetRequestValidator,
            IValidator<ResetValidateDto> resetValidateValidator,
            IValidator<ChangePasswordDto> changePasswordValidator)
        {
            _mediator = mediator;
            _loginValidator = loginValidator;
            _refreshValidator = refreshValidator;
            _resetRequestValidator = resetRequestValidator;
            _resetValidateValidator = resetValidateValidator;
            _changePasswordValidator = changePasswordValidator;
        }

        [HttpPost("login")]
        [ProducesResponseType(typeof(AuthResponseDto), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            var validationResult = await _loginValidator.ValidateAsync(dto);
            if (!validationResult.IsValid)
            {
                throw new FluentValidation.ValidationException(validationResult.Errors);
            }

            var result = await _mediator.Send(new LoginCommand(dto.Email, dto.Password));
            if (!result.Success) return StatusCode(result.StatusCode, new { error = result.ErrorKey, message = result.Error });

            return Ok(result.Response);
        }

        [HttpPost("refresh")]
        [ProducesResponseType(typeof(AuthResponseDto), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> Refresh([FromBody] RefreshDto dto)
        {
            var validationResult = await _refreshValidator.ValidateAsync(dto);
            if (!validationResult.IsValid)
            {
                throw new FluentValidation.ValidationException(validationResult.Errors);
            }

            var result = await _mediator.Send(new RefreshTokenCommand(dto.RefreshToken));
            if (!result.Success) return StatusCode(result.StatusCode, new { error = result.ErrorKey, message = result.Error });

            return Ok(result.Response);
        }

        [HttpGet("me")]
        [ProducesResponseType(typeof(AuthResponseDto), 200)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> GetMe([FromHeader(Name = "Authorization")] string authorization)
        {
            var userId = User.FindFirst("id")?.Value;
            var result = await _mediator.Send(new GetMeQuery(userId, authorization));
            if (!result.Success)
            {
                return BuildUnauthorizedResponse(result);
            }

            return Ok(result.Response);
        }

        [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
        private IActionResult BuildUnauthorizedResponse(GetMeResult result)
        {
            if (result.StatusCode == 401)
                return Unauthorized(new { error = "UnauthorizedError", message = result.Error });
            return StatusCode(result.StatusCode, new { error = "UnauthorizedError", message = result.Error });
        }

        [HttpPost("password/request")]
        [ProducesResponseType(200)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> RequestPasswordReset([FromBody] ResetRequestDto dto)
        {
            var validationResult = await _resetRequestValidator.ValidateAsync(dto);
            if (!validationResult.IsValid)
            {
                throw new FluentValidation.ValidationException(validationResult.Errors);
            }

            await _mediator.Send(new RequestPasswordResetCommand(dto.Email));
            return Ok(new { message = "E-mail de recuperação enviado com sucesso!" });
        }

        [HttpPost("password/validate")]
        [ProducesResponseType(200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> ValidateResetToken([FromBody] ResetValidateDto dto)
        {
            var validationResult = await _resetValidateValidator.ValidateAsync(dto);
            if (!validationResult.IsValid)
            {
                throw new FluentValidation.ValidationException(validationResult.Errors);
            }

            var result = await _mediator.Send(new ValidateResetTokenCommand(dto.Email, dto.Token));
            if (!result.Success) return StatusCode(result.StatusCode, new { message = result.Error });

            return Ok(new { valid = true });
        }

        [HttpPost("password/change")]
        [ProducesResponseType(200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
        {
            var validationResult = await _changePasswordValidator.ValidateAsync(dto);
            if (!validationResult.IsValid)
            {
                throw new FluentValidation.ValidationException(validationResult.Errors);
            }

            var result = await _mediator.Send(new ChangePasswordCommand(dto.Email, dto.Token, dto.Password));
            if (!result.Success) return StatusCode(result.StatusCode, new { message = result.Error });

            return Ok(new { message = "Senha alterada com sucesso!" });
        }

        [HttpPost("logout")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> Logout()
        {
            var userId = User.FindFirst("id")?.Value;
            await _mediator.Send(new LogoutCommand(userId));
            return Ok(new { message = "Logout realizado com sucesso!" });
        }
    }

    public class LoginDtoValidator : AbstractValidator<LoginDto>
    {
        public LoginDtoValidator()
        {
            RuleFor(x => x.Email).NotEmpty().WithMessage("Email and password are required.");
            RuleFor(x => x.Password).NotEmpty().WithMessage("Email and password are required.");
        }
    }

    public class RefreshDtoValidator : AbstractValidator<RefreshDto>
    {
        public RefreshDtoValidator()
        {
            RuleFor(x => x.RefreshToken).NotEmpty().WithMessage("RefreshToken is required.");
        }
    }

    public class ResetRequestDtoValidator : AbstractValidator<ResetRequestDto>
    {
        public ResetRequestDtoValidator()
        {
            RuleFor(x => x.Email).NotEmpty().WithMessage("Email is required.");
        }
    }

    public class ResetValidateDtoValidator : AbstractValidator<ResetValidateDto>
    {
        public ResetValidateDtoValidator()
        {
            RuleFor(x => x.Email).NotEmpty().WithMessage("Email and token are required.");
            RuleFor(x => x.Token).NotEmpty().WithMessage("Email and token are required.");
        }
    }

    public class ChangePasswordDtoValidator : AbstractValidator<ChangePasswordDto>
    {
        public ChangePasswordDtoValidator()
        {
            RuleFor(x => x.Email).NotEmpty().WithMessage("Email, token, and password are required.");
            RuleFor(x => x.Token).NotEmpty().WithMessage("Email, token, and password are required.");
            RuleFor(x => x.Password).NotEmpty().WithMessage("Email, token, and password are required.");
        }
    }
}
