namespace LeadersAndFollowers.Core.Abstractions;

/// <summary>
/// Clock abstraction for testability.
/// </summary>
public interface ISystemClock
{
    DateTimeOffset UtcNow { get; }
}
