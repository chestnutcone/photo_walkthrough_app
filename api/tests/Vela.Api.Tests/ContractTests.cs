using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using Microsoft.OpenApi.YamlReader;

namespace Vela.Api.Tests;

public class ContractTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly string[] PublicOperations =
    [
        "ssoSignIn", "emailRequest", "emailVerify", "refresh", "logout",
        "getArtist", "listArtistWorks", "suggestedArtists", "getWork", "listReplies", "getFeed", "search", "submitDmcaNotice",
    ];

    private readonly WebApplicationFactory<Program> _factory;

    public ContractTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private static string ContractPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "contract", "openapi.yaml")))
            dir = dir.Parent;
        return dir is null
            ? throw new FileNotFoundException("contract/openapi.yaml not found above " + AppContext.BaseDirectory)
            : Path.Combine(dir.FullName, "contract", "openapi.yaml");
    }

    private static async Task<(OpenApiDocument Doc, IList<OpenApiError> Errors)> LoadContract()
    {
        await using var stream = File.OpenRead(ContractPath());
        var settings = new OpenApiReaderSettings();
        settings.AddYamlReader();
        var result = await OpenApiDocument.LoadAsync(stream, "yaml", settings);
        return (result.Document!, result.Diagnostic?.Errors ?? []);
    }

    private static IEnumerable<(string Path, HttpMethod Method, OpenApiOperation Op)> Operations(OpenApiDocument doc) =>
        doc.Paths.SelectMany(p => p.Value.Operations!.Select(o => (p.Key, o.Key, o.Value)));

    private static Dictionary<string, RouteEndpoint> RegisteredEndpoints(WebApplicationFactory<Program> factory) =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => (Name: e.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName, Endpoint: e))
            .Where(x => x.Name is not null)
            .ToDictionary(x => x.Name!, x => x.Endpoint);

    [Fact]
    public async Task Contract_is_valid_openapi_3()
    {
        var (doc, errors) = await LoadContract();

        Assert.NotNull(doc);
        Assert.Empty(errors);
        Assert.Equal(37, Operations(doc).Count());
        Assert.All(Operations(doc), o => Assert.False(string.IsNullOrWhiteSpace(o.Op.OperationId)));
        Assert.All(Operations(doc), o => Assert.StartsWith("/v1/", o.Path));
        Assert.Equal(Operations(doc).Count(), Operations(doc).Select(o => o.Op.OperationId).Distinct().Count());
    }

    [Fact]
    public async Task Public_operations_are_exactly_those_declared_public_and_the_rest_require_bearer()
    {
        var (doc, _) = await LoadContract();

        foreach (var (path, method, op) in Operations(doc))
        {
            var isPublic = PublicOperations.Contains(op.OperationId);
            if (isPublic)
                Assert.True(op.Security is { Count: 0 }, $"{op.OperationId} must declare security: []");
            else
                Assert.True(op.Security is { Count: > 0 }, $"{method} {path} ({op.OperationId}) must declare a bearer security requirement");
        }
        Assert.Equal(PublicOperations.Length, Operations(doc).Count(o => PublicOperations.Contains(o.Op.OperationId)));
    }

    [Fact]
    public async Task Every_operationId_has_a_mapped_endpoint()
    {
        var (doc, _) = await LoadContract();
        var endpoints = RegisteredEndpoints(_factory);

        foreach (var (path, method, op) in Operations(doc))
        {
            Assert.True(endpoints.ContainsKey(op.OperationId!), $"no endpoint named {op.OperationId}");
            var ep = endpoints[op.OperationId!];
            Assert.Equal(path, "/" + ep.RoutePattern.RawText!.TrimStart('/'));
            Assert.Contains(method.Method, ep.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods);
        }
    }

    [Fact]
    public async Task Any_remaining_stub_returns_501_not_implemented_envelope()
    {
        var client = _factory.CreateClient();

        // Every operation is implemented now, so this loop is empty; it keeps guarding any stub added later.
        foreach (var (method, route, operationId) in Vela.Api.Stubs.ContractStubs.Pending)
        {
            var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), route.Replace("{id}", Guid.NewGuid().ToString())));

            Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.False(body.RootElement.GetProperty("success").GetBoolean(), operationId);
            Assert.Equal("not_implemented", body.RootElement.GetProperty("error").GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task Export_and_delete_operations_are_real_endpoints_that_require_sign_in()
    {
        var client = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.DeleteAsync("/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/v1/me/export", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/v1/me/export/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Required_schemas_exist()
    {
        var (doc, _) = await LoadContract();
        string[] names = ["TokenPair", "Profile", "Work", "Chapter", "Reply", "Notification", "Inquiry", "Page", "ErrorBody", "Envelope"];

        Assert.All(names, n => Assert.True(doc.Components!.Schemas!.ContainsKey(n), n));
        Assert.True(doc.Components!.SecuritySchemes!.ContainsKey("bearerAuth"));
    }

    private static string[] EnumValues(OpenApiDocument doc, string schema, string property) =>
        doc.Components!.Schemas![schema].Properties![property].Enum!.Select(e => e!.GetValue<string>()).ToArray();

    [Fact]
    public async Task Inquiry_type_is_a_required_five_value_enum_on_request_and_response()
    {
        var (doc, _) = await LoadContract();
        string[] expected = ["Commission", "Buy a print", "Licensing", "Collaboration", "Just saying hi"];

        Assert.Equal(expected, EnumValues(doc, "SendInquiryRequest", "type"));
        Assert.Equal(expected, EnumValues(doc, "Inquiry", "type"));
        Assert.Contains("type", doc.Components!.Schemas!["SendInquiryRequest"].Required!);
        Assert.Contains("type", doc.Components!.Schemas!["Inquiry"].Required!);
    }

    [Fact]
    public async Task Inquiry_request_requires_sender_email_and_non_empty_message()
    {
        var (doc, _) = await LoadContract();
        var request = doc.Components!.Schemas!["SendInquiryRequest"];

        Assert.Contains("email", request.Required!);
        Assert.Equal("email", request.Properties!["email"].Format);
        Assert.Equal(1, request.Properties["message"].MinLength);
    }

    [Fact]
    public async Task Report_reason_is_an_enum_and_note_is_capped()
    {
        var (doc, _) = await LoadContract();
        var report = doc.Components!.Schemas!["ReportRequest"];

        Assert.Equal(["illegal_content", "copyright", "harassment", "spam", "other"], EnumValues(doc, "ReportRequest", "reason"));
        Assert.Equal(["work", "reply", "user"], EnumValues(doc, "ReportRequest", "targetType"));
        Assert.Equal(500, report.Properties!["note"].MaxLength);
        Assert.DoesNotContain("note", report.Required!);
    }
}
