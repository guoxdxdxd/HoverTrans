namespace HoverTrans.Core.Updates;

/// <summary>Asks the release feed whether a newer version exists.</summary>
public interface IUpdateChecker
{
    /// <summary>
    /// Looks for a release newer than the running version. Never throws for something the person can act on (no
    /// network, rate limit, bad payload): those come back as <see cref="UpdateStatus.Failed"/>. Cancellation does throw,
    /// because that means the caller stopped caring.
    /// </summary>
    Task<UpdateCheck> CheckAsync(CancellationToken cancellationToken = default);
}
