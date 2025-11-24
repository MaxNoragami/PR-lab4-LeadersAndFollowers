using LeadersAndFollowers.Core.Abstractions;

namespace LeadersAndFollowers.Core.Implementation;

public sealed class SystemClock : ISystemClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
