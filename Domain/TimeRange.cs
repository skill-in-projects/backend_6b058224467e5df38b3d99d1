namespace Backend.Domain;

/// <summary>A half-open UTC range [Start, End): a slot ending at 10:00 does not clash with one starting at 10:00.</summary>
public readonly record struct TimeRange(DateTime Start, DateTime End)
{
    public bool Overlaps(TimeRange other) => Start < other.End && other.Start < End;
}
