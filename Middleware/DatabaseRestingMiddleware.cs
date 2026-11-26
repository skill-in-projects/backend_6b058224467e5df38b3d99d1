using Npgsql;

namespace Backend.Middleware;

/// <summary>
/// When the database refuses because the project is over its monthly limit, every endpoint answers the same calm 503
/// instead of a server error. Any other database error keeps its normal handling.
/// </summary>
public class DatabaseRestingMiddleware
{
    private readonly RequestDelegate _next;

    public DatabaseRestingMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex) when (IsOverLimit(ex) && !context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new { code = "database_resting", error = "This app is resting and will be back soon." });
        }
    }

    private static bool IsOverLimit(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
        {
            if (ex is NpgsqlException
                && ex.Message.Contains("exceeded", StringComparison.OrdinalIgnoreCase)
                && ex.Message.Contains("quota", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
