using Microsoft.AspNetCore.Mvc;

namespace MageBackend.Core
{
    public class BaseApiController : ControllerBase
    {
        protected IActionResult ErrorResponse(string message, int statusCode = 400, object? details = null)
        {
            return StatusCode(statusCode, new { message, errors = details });
        }
    }
}
