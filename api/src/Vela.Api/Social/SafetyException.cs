namespace Vela.Api.Social;

public sealed class SafetyException(int status, string code, string message, TimeSpan? retryAfter = null) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
