using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace MageBackend.Features.Debug
{
    [ApiController]
    [Route("v1/debug")]
    public class DebugController : ControllerBase
    {
        [HttpGet("error")]
        public IActionResult ThrowError()
        {
            throw new System.Exception("Test error for debug");
        }
    }
}

