namespace MS3DPRINT.Manager.Core.Revenues;

/// <summary>A manual bank-statement check; a null date reverses the check without removing history.</summary>
public sealed record JournalPointing(Guid[] EntryIds, DateTimeOffset? PointedAt);
