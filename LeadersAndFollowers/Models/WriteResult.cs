namespace LeadersAndFollowers.Models;

public record WriteResult(bool Success, int Quorum, int Acks);
