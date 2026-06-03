namespace MageBackend.Core.Commands
{
    public record CommandResult<TDto>(bool Success, TDto? Data = default, string? Error = null, int StatusCode = 200);
    public record CommandResult(bool Success, string? Error = null, int StatusCode = 200);
}
