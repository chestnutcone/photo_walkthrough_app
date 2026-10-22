using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace Vela.Api.Common;

/// <summary>Builds every API response inside the standard envelope.</summary>
public static class Results
{
    public static IResult Ok<T>(T data, string? nextCursor = null) =>
        new EnvelopeResult<T>(
            StatusCodes.Status200OK,
            new ApiEnvelope<T>(true, data, null, nextCursor is null ? null : new PageInfo(nextCursor)));

    public static IResult Fail(int status, string code, string message) =>
        new EnvelopeResult<object>(
            status,
            new ApiEnvelope<object>(false, null, new ApiError(code, message), null));

    private sealed class EnvelopeResult<T>(int status, ApiEnvelope<T> body) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            var options = httpContext.RequestServices.GetService<IOptions<JsonOptions>>()?.Value.SerializerOptions
                          ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);
            httpContext.Response.StatusCode = status;
            return httpContext.Response.WriteAsJsonAsync(body, options);
        }
    }
}
