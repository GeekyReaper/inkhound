namespace Inkhound.Web.Middleware;

public class ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await next(ctx);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception: {Message}", ex.Message);
            await WriteErrorAsync(ctx, ex);
        }
    }

    private static async Task WriteErrorAsync(HttpContext ctx, Exception ex)
    {
        ctx.Response.ContentType = "application/json";

        ctx.Response.StatusCode = ex switch
        {
            KeyNotFoundException        => StatusCodes.Status404NotFound,
            InvalidOperationException   => StatusCodes.Status409Conflict,
            ArgumentException           => StatusCodes.Status400BadRequest,
            UnauthorizedAccessException => StatusCodes.Status401Unauthorized,
            _                           => StatusCodes.Status500InternalServerError
        };

        var payload = new
        {
            status  = ctx.Response.StatusCode,
            message = ctx.Response.StatusCode == 500
                          ? "An internal error occurred."
                          : ex.Message
        };

        await ctx.Response.WriteAsJsonAsync(payload);
    }
}
