using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace MageBackend.Features.Debug
{
    [ApiController]
    [Route("v1/debug")]
    public class DebugController : ControllerBase
    {
        private static readonly byte[] DummyPdfBytes = Encoding.UTF8.GetBytes(
            "%PDF-1.4\n" +
            "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n" +
            "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n" +
            "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> /Contents 4 0 R >>\nendobj\n" +
            "4 0 obj\n<< /Length 20 >>\nstream\nBT /F1 12 Tf ET\nendstream\nendobj\n" +
            "xref\n0 5\n0000000000 65535 f\n0000000009 00000 n\n0000000058 00000 n\n0000000115 00000 n\n0000000219 00000 n\n" +
            "trailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n290\n%%EOF"
        );

        [HttpGet("pdf")]
        [ProducesResponseType(typeof(FileResult), 200)]
        public IActionResult GetPdf()
        {
            Response.Headers["Content-Disposition"] = "inline; filename=\"test.pdf\"";
            return File(DummyPdfBytes, "application/pdf");
        }

        public class PdfPostRequest
        {
            public string? Template { get; set; }
            public object? Data { get; set; }
        }

        [HttpPost("pdf")]
        [ProducesResponseType(typeof(FileResult), 200)]
        public IActionResult PostPdf([FromBody] PdfPostRequest request)
        {
            Response.Headers["Content-Disposition"] = "attachment; filename=\"test.pdf\"";
            return File(DummyPdfBytes, "application/pdf");
        }

        [HttpGet("error")]
        public IActionResult ThrowError()
        {
            throw new System.Exception("Test error for debug");
        }
    }
}
