namespace LeadersAndFollowers.Models;

public record ConfigResult(int WriteQuorum, int MinDelayMs, int MaxDelayMs);
