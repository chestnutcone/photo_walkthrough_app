namespace Vela.Api.Profiles;

/// <summary>A refused profile operation; the endpoint turns it into an envelope error.</summary>
public sealed class ProfileException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;

    public static ProfileException Validation(string field, string problem) =>
        new(StatusCodes.Status400BadRequest, "validation_failed", $"{field} {problem}");
}
