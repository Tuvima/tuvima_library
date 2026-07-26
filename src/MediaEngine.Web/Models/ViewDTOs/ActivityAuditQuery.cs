namespace MediaEngine.Web.Models.ViewDTOs;

public sealed class ActivityAuditQuery
{
    public string? Search { get; set; }
    public string? MediaType { get; set; }
    public string? Status { get; set; }
    public string? Source { get; set; }
    public string? EventType { get; set; }
    public DateTimeOffset? Start { get; set; }
    public DateTimeOffset? End { get; set; }
    public int Offset { get; set; }
    public int Limit { get; set; } = 25;
    public string? Sort { get; set; }
    public string SortDirection { get; set; } = "desc";
}
