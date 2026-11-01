namespace Vela.Api.Common;

public sealed record ApiError(string Code, string Message);

public sealed record PageInfo(string? NextCursor);

public sealed record ApiEnvelope<T>(bool Success, T? Data, ApiError? Error, PageInfo? Page);
