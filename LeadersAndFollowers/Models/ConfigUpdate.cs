namespace LeadersAndFollowers.Models;

public record ConfigUpdate(int? WriteQuorum, int? MinDelayMs, int? MaxDelayMs);
