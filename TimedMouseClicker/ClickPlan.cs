namespace TimedMouseClicker;

internal sealed class ClickPlan
{
    public long? Limit { get; }
    public long Completed { get; private set; }
    public bool IsComplete => Limit is { } limit && Completed >= limit;

    public ClickPlan(long? limit)
    {
        if (limit is <= 0) throw new ArgumentOutOfRangeException(nameof(limit));
        Limit = limit;
    }

    public void RecordClick()
    {
        if (IsComplete) throw new InvalidOperationException("The click plan is already complete.");
        Completed++;
    }
}
