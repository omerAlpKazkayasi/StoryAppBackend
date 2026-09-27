using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using StoryApp.Api.ErrorHandling;
using Xunit;

namespace StoryApp.Tests;

public class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task TryHandleAsync_Returns500ProblemDetails_WithoutLeakingInternalExceptionDetails()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                context.ProblemDetails.Instance ??= context.HttpContext.Request.Path.Value;
                var traceId = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
                context.ProblemDetails.Extensions.TryAdd("traceId", traceId);
            };
        });
        using var serviceProvider = services.BuildServiceProvider();

        var problemDetailsService = serviceProvider.GetRequiredService<IProblemDetailsService>();
        var logger = NullLogger<GlobalExceptionHandler>.Instance;
        var handler = new GlobalExceptionHandler(logger, problemDetailsService);

        var context = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };
        context.Request.Path = "/api/test-resource";
        context.TraceIdentifier = "test-trace-id-12345";

        var responseBodyStream = new MemoryStream();
        context.Response.Body = responseBodyStream;

        const string sensitiveErrorMessage = "Database connection to Server=production-sql;User=sa;Password=supersecret failed!";
        var internalException = new InvalidOperationException(sensitiveErrorMessage);

        // Act
        var handled = await handler.TryHandleAsync(context, internalException, CancellationToken.None);

        // Assert
        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        responseBodyStream.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(responseBodyStream);
        var responseJson = await reader.ReadToEndAsync();

        Assert.NotEmpty(responseJson);

        // Verify sensitive details are NEVER leaked to client
        Assert.DoesNotContain(sensitiveErrorMessage, responseJson);
        Assert.DoesNotContain("InvalidOperationException", responseJson);
        Assert.DoesNotContain("StackTrace", responseJson, StringComparison.OrdinalIgnoreCase);

        // Verify RFC ProblemDetails structure
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        Assert.Equal(500, root.GetProperty("status").GetInt32());
        Assert.Equal("Internal Server Error", root.GetProperty("title").GetString());
        Assert.Equal("An unexpected error occurred.", root.GetProperty("detail").GetString());
        Assert.Equal("/api/test-resource", root.GetProperty("instance").GetString());

        // Verify traceId is present in extensions
        Assert.True(root.TryGetProperty("traceId", out var traceIdProp));
        Assert.Equal("test-trace-id-12345", traceIdProp.GetString());
    }

    [Fact]
    public async Task TryHandleAsync_LogsRequestPathAndTraceId()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddProblemDetails();
        using var serviceProvider = services.BuildServiceProvider();

        var problemDetailsService = serviceProvider.GetRequiredService<IProblemDetailsService>();
        var testLogger = new TestLogger<GlobalExceptionHandler>();
        var handler = new GlobalExceptionHandler(testLogger, problemDetailsService);

        var context = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };
        context.Request.Path = "/api/test-error-endpoint";
        context.TraceIdentifier = "trace-custom-999";
        context.Response.Body = new MemoryStream();

        var ex = new Exception("Something went wrong");

        // Act
        await handler.TryHandleAsync(context, ex, CancellationToken.None);

        // Assert
        Assert.Single(testLogger.LoggedMessages);
        var log = testLogger.LoggedMessages[0];
        Assert.Equal(LogLevel.Error, log.LogLevel);
        Assert.Contains("/api/test-error-endpoint", log.Message);
        Assert.Contains("trace-custom-999", log.Message);
        Assert.Same(ex, log.Exception);
    }

    [Fact]
    public void ValidationProblemDetails_MatchesExpectedContractAndTraceIdExtension()
    {
        // Arrange
        var modelState = new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary();
        modelState.AddModelError("Username", "The Username field is required.");
        modelState.AddModelError("Age", "The field Age must be between 3 and 12.");

        // Act
        var validationProblem = new ValidationProblemDetails(modelState)
        {
            Status = StatusCodes.Status400BadRequest,
            Instance = "/api/auth/register"
        };
        validationProblem.Extensions["traceId"] = "trace-val-123";

        // Assert
        Assert.Equal(400, validationProblem.Status);
        Assert.Equal("One or more validation errors occurred.", validationProblem.Title);
        Assert.Equal("/api/auth/register", validationProblem.Instance);
        Assert.True(validationProblem.Errors.ContainsKey("Username"));
        Assert.True(validationProblem.Errors.ContainsKey("Age"));
        Assert.Equal("trace-val-123", validationProblem.Extensions["traceId"]);
    }

    [Fact]
    public async Task TryHandleAsync_WhenNotFoundException_Returns404ProblemDetails_AndLogsWarning()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                context.ProblemDetails.Instance ??= context.HttpContext.Request.Path.Value;
                var traceId = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
                context.ProblemDetails.Extensions.TryAdd("traceId", traceId);
            };
        });
        using var serviceProvider = services.BuildServiceProvider();

        var problemDetailsService = serviceProvider.GetRequiredService<IProblemDetailsService>();
        var testLogger = new TestLogger<GlobalExceptionHandler>();
        var handler = new GlobalExceptionHandler(testLogger, problemDetailsService);

        var context = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };
        context.Request.Path = "/api/test-notfound";
        context.TraceIdentifier = "trace-notfound-1";
        context.Response.Body = new MemoryStream();

        var notFoundEx = new StoryApp.Application.Common.Exceptions.NotFoundException("Resource XYZ not found.");

        // Act
        var handled = await handler.TryHandleAsync(context, notFoundEx, CancellationToken.None);

        // Assert
        Assert.True(handled);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var json = await reader.ReadToEndAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(404, root.GetProperty("status").GetInt32());
        Assert.Equal("Not Found", root.GetProperty("title").GetString());
        Assert.Equal("Resource XYZ not found.", root.GetProperty("detail").GetString());

        Assert.Single(testLogger.LoggedMessages);
        Assert.Equal(LogLevel.Warning, testLogger.LoggedMessages[0].LogLevel);
    }

    [Fact]
    public async Task TryHandleAsync_WhenConflictException_Returns409ProblemDetails_AndLogsWarning()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                context.ProblemDetails.Instance ??= context.HttpContext.Request.Path.Value;
                var traceId = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
                context.ProblemDetails.Extensions.TryAdd("traceId", traceId);
            };
        });
        using var serviceProvider = services.BuildServiceProvider();

        var problemDetailsService = serviceProvider.GetRequiredService<IProblemDetailsService>();
        var testLogger = new TestLogger<GlobalExceptionHandler>();
        var handler = new GlobalExceptionHandler(testLogger, problemDetailsService);

        var context = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };
        context.Request.Path = "/api/test-conflict";
        context.TraceIdentifier = "trace-conflict-1";
        context.Response.Body = new MemoryStream();

        var conflictEx = new StoryApp.Application.Common.Exceptions.ConflictException("Conflict occurred.");

        // Act
        var handled = await handler.TryHandleAsync(context, conflictEx, CancellationToken.None);

        // Assert
        Assert.True(handled);
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var json = await reader.ReadToEndAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(409, root.GetProperty("status").GetInt32());
        Assert.Equal("Conflict", root.GetProperty("title").GetString());
        Assert.Equal("Conflict occurred.", root.GetProperty("detail").GetString());

        Assert.Single(testLogger.LoggedMessages);
        Assert.Equal(LogLevel.Warning, testLogger.LoggedMessages[0].LogLevel);
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<(LogLevel LogLevel, string Message, Exception? Exception)> LoggedMessages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            LoggedMessages.Add((logLevel, formatter(state, exception), exception));
        }
    }
}
