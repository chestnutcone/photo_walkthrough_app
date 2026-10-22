using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Vela.Api.Tests;

public class ApiEnvelopeTests
{
    private static async Task<(int Status, JsonElement Body)> Run(IResult result)
    {
        var ctx = new DefaultHttpContext();
        ctx.RequestServices = new ServiceCollection().BuildServiceProvider();
        ctx.Response.Body = new MemoryStream();
        await result.ExecuteAsync(ctx);
        ctx.Response.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(ctx.Response.Body);
        return (ctx.Response.StatusCode, doc.RootElement.Clone());
    }

    [Fact]
    public async Task Fail_returns_error_envelope_with_status()
    {
        var (status, body) = await Run(Results.Fail(404, "not_found", "Nothing here"));

        Assert.Equal(404, status);
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("data").ValueKind);
        Assert.Equal("not_found", body.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("Nothing here", body.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task Ok_returns_data_and_cursor()
    {
        var (status, body) = await Run(Results.Ok(new { n = 1 }, "abc"));

        Assert.Equal(200, status);
        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.Equal(1, body.GetProperty("data").GetProperty("n").GetInt32());
        Assert.Equal("abc", body.GetProperty("page").GetProperty("nextCursor").GetString());
    }

    [Fact]
    public async Task Ok_without_cursor_has_null_page()
    {
        var (_, body) = await Run(Results.Ok("x"));

        Assert.Equal(JsonValueKind.Null, body.GetProperty("page").ValueKind);
    }
}
