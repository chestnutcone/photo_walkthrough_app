using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using Microsoft.OpenApi.YamlReader;

namespace Vela.Api.Tests.Security;

/// <summary>One contract operation with the security decision the contract documents for it.</summary>
public sealed record ContractOperation(string OperationId, HttpMethod Method, string Path, bool IsPublic)
{
    public bool IsWrite => Method != HttpMethod.Get && Method != HttpMethod.Head;

    /// <summary>The path with every template parameter filled in (ids as the given guid, indexes as 0).</summary>
    public string Url(Guid id) => System.Text.RegularExpressions.Regex.Replace(Path, @"\{(\w+)\}",
        m => m.Groups[1].Value == "index" ? "0" : m.Groups[1].Value == "handle" ? "someone" : id.ToString());

    public override string ToString() => OperationId;
}

/// <summary>Reads contract/openapi.yaml so security tests cover every operation, including ones added later.</summary>
public static class SecurityContract
{
    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(System.IO.Path.Combine(dir.FullName, "contract", "openapi.yaml"))) dir = dir.Parent;
        return dir?.FullName ?? throw new FileNotFoundException("contract/openapi.yaml not found above " + AppContext.BaseDirectory);
    }

    private static readonly Lazy<IReadOnlyList<ContractOperation>> Loaded = new(Load);

    public static IReadOnlyList<ContractOperation> Operations => Loaded.Value;

    /// <summary>Theory data: one row per operation id.</summary>
    public static IEnumerable<object[]> OperationIds() => Operations.Select(o => new object[] { o.OperationId });

    public static ContractOperation Find(string operationId) => Operations.Single(o => o.OperationId == operationId);

    private static IReadOnlyList<ContractOperation> Load()
    {
        using var stream = new MemoryStream(File.ReadAllBytes(System.IO.Path.Combine(RepoRoot(), "contract", "openapi.yaml")));
        var settings = new OpenApiReaderSettings();
        settings.AddYamlReader();
        var doc = OpenApiDocument.Load(stream, "yaml", settings).Document ?? throw new InvalidOperationException("contract did not parse");
        return doc.Paths.SelectMany(p => p.Value.Operations!.Select(o =>
                new ContractOperation(o.Value.OperationId!, o.Key, p.Key, o.Value.Security is { Count: 0 })))
            .ToList();
    }
}
