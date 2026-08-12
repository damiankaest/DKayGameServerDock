namespace DKay.GameServerDock.Api;

using Microsoft.AspNetCore.WebUtilities;

public sealed class ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (KeyNotFoundException exception)
        {
            await WriteProblemAsync(context, StatusCodes.Status404NotFound, exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            await WriteProblemAsync(context, StatusCodes.Status409Conflict, exception.Message);
        }
        catch (ArgumentException exception)
        {
            await WriteProblemAsync(context, StatusCodes.Status400BadRequest, exception.Message);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled API error for {Method} {Path}.", context.Request.Method, context.Request.Path);
            await WriteProblemAsync(context, StatusCodes.Status500InternalServerError, "An unexpected server error occurred.");
        }
    }

    private static Task WriteProblemAsync(HttpContext context, int status, string detail)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new
        {
            type = "about:blank",
            title = ReasonPhrases.GetReasonPhrase(status),
            status,
            detail
        });
    }
}
