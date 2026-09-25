namespace MediaEngine.Web.Components.Shared;

public sealed record TimelinePeriod(int Key, string Label, int Count, IReadOnlyList<TimelinePeriod>? Children = null);
public enum TimelineGrouping { Year, Decade }
