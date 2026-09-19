namespace Inkhound.Web.Middleware;

public class ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await next(ctx);
        }
        catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested)
        {
            // Le client a abandonné la requête (navigation, appel HTTP annulé côté Angular) : le
            // token HttpContext.RequestAborted a déclenché l'annulation dans EF Core / HttpClient.
            // Ce n'est pas une erreur applicative — personne n'attend plus la réponse, on ne logge
            // ni ne renvoie rien (et le débogueur n'a plus à s'arrêter dessus).
            logger.LogDebug("Request {Method} {Path} aborted by the client.", ctx.Request.Method, ctx.Request.Path);
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
