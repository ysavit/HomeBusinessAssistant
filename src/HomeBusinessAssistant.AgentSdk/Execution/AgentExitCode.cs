namespace HomeBusinessAssistant.AgentSdk.Execution;

/// <summary>Stable process exit codes emitted by all agents.</summary>
public static class AgentExitCode
{
    /// <summary>The agent completed successfully.</summary>
    public const int Success = 0;
    /// <summary>The invocation arguments were invalid.</summary>
    public const int InvalidArguments = 2;
    /// <summary>The supplied configuration was invalid.</summary>
    public const int InvalidConfiguration = 3;
    /// <summary>Manual authentication is required.</summary>
    public const int AuthenticationRequired = 10;
    /// <summary>The remote service reported throttling.</summary>
    public const int Throttled = 11;
    /// <summary>An access challenge was detected.</summary>
    public const int ChallengeDetected = 12;
    /// <summary>The run was cancelled.</summary>
    public const int Cancelled = 20;
    /// <summary>A retryable failure occurred.</summary>
    public const int TransientFailure = 30;
    /// <summary>A non-retryable expected failure occurred.</summary>
    public const int PermanentFailure = 40;
    /// <summary>An unexpected failure reached the process boundary.</summary>
    public const int UnhandledFailure = 70;

    /// <summary>Returns whether a value is assigned by this version of the SDK.</summary>
    public static bool IsDefined(int value) => value is Success
        or InvalidArguments
        or InvalidConfiguration
        or AuthenticationRequired
        or Throttled
        or ChallengeDetected
        or Cancelled
        or TransientFailure
        or PermanentFailure
        or UnhandledFailure;
}
