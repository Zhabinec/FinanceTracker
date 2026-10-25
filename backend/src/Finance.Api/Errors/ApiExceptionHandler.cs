using Finance.Application.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Finance.Api.Errors;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception,
        CancellationToken cancellationToken)
    {
        ProblemDetails problem;
        if (exception is RequestValidationException validation)
            problem = new ValidationProblemDetails(validation.Errors.ToDictionary(pair => pair.Key, pair => pair.Value))
            { Status = 400, Title = validation.Message };
        else if (exception is ConflictException)
            problem = new ProblemDetails { Status = 409, Title = exception.Message };
        else if (exception is Finance.Domain.Common.DomainException)
            problem = new ProblemDetails { Status = 400, Title = exception.Message };
        else if (exception is UnauthorizedAccessException)
            problem = new ProblemDetails { Status = 401, Title = "Authentication is required." };
        else
        {
            logger.LogError(exception, "Request {Method} {Path} failed. Trace {TraceId}",
                context.Request.Method, context.Request.Path, context.TraceIdentifier);
            problem = new ProblemDetails { Status = 500, Title = "An unexpected error occurred." };
        }
        problem.Extensions["traceId"] = context.TraceIdentifier;
        context.Response.StatusCode = problem.Status!.Value;
        await context.Response.WriteAsJsonAsync(problem, options: (System.Text.Json.JsonSerializerOptions?)null,
            contentType: "application/problem+json", cancellationToken: cancellationToken);
        return true;
    }
}
