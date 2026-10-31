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

    [Fact]
    public async Task No_operation_declares_a_501_and_no_not_implemented_response_exists()
    {
        var (doc, _) = await LoadContract();

        Assert.All(Operations(doc), o => Assert.False(o.Op.Responses!.ContainsKey("501"), $"{o.Op.OperationId} still declares 501"));
        Assert.False(doc.Components!.Responses!.ContainsKey("NotImplemented"));
        Assert.DoesNotContain("not_implemented", await File.ReadAllTextAsync(ContractPath()));
    }

    [Theory]
    [InlineData("follow")]
    [InlineData("unfollow")]
    public async Task Follow_and_unfollow_are_documented_as_idempotent(string operationId)
    {
        var (doc, _) = await LoadContract();
        var op = Operations(doc).Single(o => o.Op.OperationId == operationId).Op;

        Assert.Contains("idempotent", op.Description ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Notification_type_is_an_open_string_that_lists_the_known_values()
    {
        var (doc, _) = await LoadContract();
        var type = doc.Components!.Schemas!["Notification"].Properties!["type"];

        Assert.True(type.Enum is null || type.Enum.Count == 0, "Notification.type must not be a closed enum");
        foreach (var known in new[] { "follow", "reply", "new_story", "inquiry", "moderation" })
            Assert.Contains($"`{known}`", type.Description ?? "");
    }

    [Theory]
    [InlineData("CompleteOnboardingRequest", "displayName", 1, 60)]
    [InlineData("UpdateProfileRequest", "displayName", 1, 60)]
    [InlineData("CreateDraftRequest", "title", null, Vela.Api.Works.WorkValidator.MaxTitle)]
    [InlineData("UpdateWorkRequest", "title", 1, Vela.Api.Works.WorkValidator.MaxTitle)]
    [InlineData("UpdateWorkRequest", "intro", null, Vela.Api.Works.WorkValidator.MaxIntro)]
    [InlineData("UpdateWorkRequest", "altText", null, Vela.Api.Works.WorkValidator.MaxAltText)]
    [InlineData("Chapter", "title", 1, Vela.Api.Works.WorkValidator.MaxChapterTitle)]
    [InlineData("Chapter", "body", null, Vela.Api.Works.WorkValidator.MaxChapterBody)]
    [InlineData("Chapter", "meta", null, Vela.Api.Works.WorkValidator.MaxChapterMeta)]
    [InlineData("Chapter", "altText", null, Vela.Api.Works.WorkValidator.MaxAltText)]
    [InlineData("AddReplyRequest", "body", 1, Vela.Api.Social.ReplyText.MaxLength)]
    [InlineData("SendInquiryRequest", "message", 1, Vela.Api.Inquiries.InquiryValidator.MaxMessageLength)]
    public async Task Text_length_limits_in_the_contract_match_the_server(string schema, string property, int? min, int max)
    {
        var (doc, _) = await LoadContract();
        var p = doc.Components!.Schemas![schema].Properties![property];

        Assert.Equal(max, p.MaxLength);
        if (min is not null) Assert.Equal(min, p.MinLength);
    }

    [Fact]
    public async Task Contract_states_how_text_lengths_are_counted()
    {
        var (doc, _) = await LoadContract();

        Assert.Contains("code points", doc.Info!.Description ?? "");
    }
}
