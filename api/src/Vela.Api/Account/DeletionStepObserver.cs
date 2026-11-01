namespace Vela.Api.Account;

/// <summary>Called after each row-deletion statement inside the deletion transaction. Production does nothing; tests use it to fail midway.</summary>
public interface IDeletionStepObserver
{
    Task AfterStepAsync();
}

public sealed class NoOpDeletionStepObserver : IDeletionStepObserver
{
    public Task AfterStepAsync() => Task.CompletedTask;
}
