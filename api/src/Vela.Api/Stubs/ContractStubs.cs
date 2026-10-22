namespace Vela.Api.Stubs;

/// <summary>
/// Registers every contract operation that has no real handler yet as a 501 not_implemented
/// stub named by its operationId. A task that implements an operation maps the real endpoint
/// (with the same <c>.WithName(operationId)</c>) and deletes that operation's line below.
/// </summary>
public static class ContractStubs
{
    private static readonly (string Method, string Route, string OperationId)[] Operations =
    [
        ("DELETE", "/v1/me", "deleteAccount"),
        ("POST", "/v1/me/export", "requestExport"),
        ("GET", "/v1/me/export/{id}", "getExport"),
    ];

    public static IEndpointRouteBuilder MapContractStubs(this IEndpointRouteBuilder app)
    {
        foreach (var (method, route, operationId) in Operations)
        {
            app.MapMethods(route, [method], () => Results.Fail(StatusCodes.Status501NotImplemented, "not_implemented", "This operation is not implemented yet."))
                .WithName(operationId);
        }

        return app;
    }
}
