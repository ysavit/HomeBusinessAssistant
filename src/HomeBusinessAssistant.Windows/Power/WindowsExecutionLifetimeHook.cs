using HomeBusinessAssistant.Application.Power;

namespace HomeBusinessAssistant.Windows.Power;

/// <summary>Maps explicit durable Runner power policy to the Windows power-request service.</summary>
public sealed class WindowsExecutionLifetimeHook(IPowerRequestService powerRequests) : IExecutionLifetimeHook
{
    /// <inheritdoc />
    public ValueTask<IAsyncDisposable> AcquireAsync(
        ExecutionLifetimeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.KeepDisplayOn && !request.KeepSystemAwake)
        {
            throw new PowerRequestException(
                "power.display-requires-system",
                "A display-awake request must also require the system to remain awake.");
        }

        return request.KeepSystemAwake
            ? powerRequests.AcquireSystemRequiredAsync(request.Reason, request.KeepDisplayOn, cancellationToken)
            : NoPowerHandle(cancellationToken);
    }

    private static ValueTask<IAsyncDisposable> NoPowerHandle(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IAsyncDisposable>(EmptyHandle.Instance);
    }

    private sealed class EmptyHandle : IAsyncDisposable
    {
        public static EmptyHandle Instance { get; } = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
