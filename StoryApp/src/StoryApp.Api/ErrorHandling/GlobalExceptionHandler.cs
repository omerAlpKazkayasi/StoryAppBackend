using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoryApp.Application.Common.Exceptions;

namespace StoryApp.Api.ErrorHandling;

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IProblemDetailsService _problemDetailsService;

    public GlobalExceptionHandler(
        ILogger<GlobalExceptionHandler> logger,
        IProblemDetailsService problemDetailsService)
    {
        _logger = logger;
        _problemDetailsService = problemDetailsService;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (httpContext.Response.HasStarted)
        {
            _logger.LogWarning("The response has already started. GlobalExceptionHandler cannot handle the exception.");
            return false;
        }

        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        if (exception is OperationCanceledException)
        {
            _logger.LogInformation(
                "Request was canceled by the client. RequestPath: {RequestPath}, TraceId: {TraceId}",
                httpContext.Request.Path.Value,
                traceId);
            return true;
        }

        int statusCode;
        string type;
        string title;
        string detail;

        if (exception is NotFoundException notFound)
        {
            statusCode = StatusCodes.Status404NotFound;
            type = "https://tools.ietf.org/html/rfc9110#section-15.5.5";
            title = "Not Found";
            detail = notFound.Message;

            _logger.LogWarning(
                exception,
                "Resource not found. RequestPath: {RequestPath}, TraceId: {TraceId}",
                httpContext.Request.Path.Value,
                traceId);
        }
        else if (exception is ConflictException conflict)
        {
            statusCode = StatusCodes.Status409Conflict;
            type = "https://tools.ietf.org/html/rfc9110#section-15.5.10";
            title = "Conflict";
            detail = conflict.Message;

            _logger.LogWarning(
                exception,
                "Business conflict occurred. RequestPath: {RequestPath}, TraceId: {TraceId}",
                httpContext.Request.Path.Value,
                traceId);
        }
        else if (exception is DbUpdateConcurrencyException concurrencyEx)
        {
            statusCode = StatusCodes.Status409Conflict;
            type = "https://tools.ietf.org/html/rfc9110#section-15.5.10";
            title = "Conflict";
            detail = "The resource was modified concurrently. Please refresh and try again.";

            _logger.LogWarning(
                concurrencyEx,
                "Database concurrency conflict. RequestPath: {RequestPath}, TraceId: {TraceId}",
                httpContext.Request.Path.Value,
                traceId);
        }
        else if (exception is BadHttpRequestException badRequest)
        {
            statusCode = StatusCodes.Status400BadRequest;
            type = "https://tools.ietf.org/html/rfc9110#section-15.5.1";
            title = "Bad Request";
            detail = badRequest.Message;

            _logger.LogWarning(
                badRequest,
                "Bad HTTP request. RequestPath: {RequestPath}, TraceId: {TraceId}",
                httpContext.Request.Path.Value,
                traceId);
        }
        else if (exception is UnauthorizedAccessException unauthorized)
        {
            statusCode = StatusCodes.Status401Unauthorized;
            type = "https://tools.ietf.org/html/rfc9110#section-15.5.2";
            title = "Unauthorized";
            detail = unauthorized.Message;

            _logger.LogWarning(
                exception,
                "Unauthorized access attempt. RequestPath: {RequestPath}, TraceId: {TraceId}",
                httpContext.Request.Path.Value,
                traceId);
        }
        else
        {
            statusCode = StatusCodes.Status500InternalServerError;
            type = "https://tools.ietf.org/html/rfc9110#section-15.6.1";
            title = "Internal Server Error";
            detail = "An unexpected error occurred.";

            _logger.LogError(
                exception,
                "Unhandled exception occurred while processing request. RequestPath: {RequestPath}, TraceId: {TraceId}",
                httpContext.Request.Path.Value,
                traceId);
        }

        httpContext.Response.StatusCode = statusCode;

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Type = type,
            Title = title,
            Detail = detail,
            Instance = httpContext.Request.Path.Value
        };

        problemDetails.Extensions["traceId"] = traceId;

        var handled = await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problemDetails
        });

        if (!handled)
        {
            httpContext.Response.ContentType = "application/problem+json";
            await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken: cancellationToken);
        }

        return true;
    }
}
