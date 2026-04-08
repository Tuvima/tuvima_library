using System.Text;
using System.Text.Json;

namespace MediaEngine.Api.DevSupport;

/// <summary>
/// A single row in the reconciliation report — one expected outcome
/// compared against the actual post-ingestion state of the corresponding
/// Work row.
/// </summary>
public sealed record ReconciliationReportItem(
    string FileName,
    string ExpectedStatus,
    string ActualStatus,
    string? ExpectedTrigger,
    string? ActualTrigger,
    bool Matched,
    string? Reason);

/// <summary>
/// Aggregate report produced by the integration test reconciliation pass.
/// Compares declared seed expectations against actual database state and
/// renders the result as either an HTML table or a JSON object.
/// </summary>
public sealed class ReconciliationReport
{
    public int Total { get; set; }

    public int Matched { get; set; }

    public int Mismatched => Total - Matched;

    public List<ReconciliationReportItem> Items { get; set; } = [];

    /// <summary>Items where expected and actual outcomes agree.</summary>
    public IEnumerable<ReconciliationReportItem> MatchedExpected() =>
        Items.Where(i => i.Matched);

    /// <summary>Items expected to be Identified but landed in InReview.</summary>
    public IEnumerable<ReconciliationReportItem> UnexpectedReview() =>
        Items.Where(i => !i.Matched
                      && i.ExpectedStatus.Equals("Identified", StringComparison.OrdinalIgnoreCase)
                      && i.ActualStatus.StartsWith("InReview", StringComparison.OrdinalIgnoreCase));

    /// <summary>Items expected to be InReview but landed Identified.</summary>
    public IEnumerable<ReconciliationReportItem> UnexpectedIdentified() =>
        Items.Where(i => !i.Matched
                      && i.ExpectedStatus.StartsWith("InReview", StringComparison.OrdinalIgnoreCase)
                      && i.ActualStatus.Equals("Identified", StringComparison.OrdinalIgnoreCase));

    /// <summary>Items in review with the wrong trigger code.</summary>
    public IEnumerable<ReconciliationReportItem> WrongTrigger() =>
        Items.Where(i => !i.Matched
                      && !string.IsNullOrWhiteSpace(i.ExpectedTrigger)
                      && !string.IsNullOrWhiteSpace(i.ActualTrigger)
                      && !string.Equals(i.ExpectedTrigger, i.ActualTrigger, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Renders the report as a basic HTML fragment with a summary header
    /// and one table per status group. Designed to be embedded inside the
    /// existing integration test HTML report — no &lt;html&gt; wrapper.
    /// </summary>
    public string ToHtml()
    {
        var sb = new StringBuilder();
        sb.Append("<section class=\"reconciliation\">");
        sb.Append("<h2>Reconciliation</h2>");
        sb.Append($"<p><strong>{Matched}</strong>/<strong>{Total}</strong> seed fixtures matched their expected outcome ({Mismatched} mismatch{(Mismatched == 1 ? "" : "es")}).</p>");

        AppendGroup(sb, "Matched",                MatchedExpected());
        AppendGroup(sb, "Unexpected Review",      UnexpectedReview());
        AppendGroup(sb, "Unexpected Identified",  UnexpectedIdentified());
        AppendGroup(sb, "Wrong Trigger",          WrongTrigger());

        sb.Append("</section>");
        return sb.ToString();
    }

    /// <summary>Returns the report as a JSON document for API responses.</summary>
    public string ToJson() => JsonSerializer.Serialize(new
    {
        expected = Total,
        matched = Matched,
        mismatched = Mismatched,
        mismatches = Items
            .Where(i => !i.Matched)
            .Select(i => new
            {
                file_name        = i.FileName,
                expected_status  = i.ExpectedStatus,
                actual_status    = i.ActualStatus,
                expected_trigger = i.ExpectedTrigger,
                actual_trigger   = i.ActualTrigger,
                reason           = i.Reason,
            }),
    }, new JsonSerializerOptions { WriteIndented = true });

    private static void AppendGroup(StringBuilder sb, string title, IEnumerable<ReconciliationReportItem> items)
    {
        var list = items.ToList();
        if (list.Count == 0) return;

        sb.Append($"<h3>{System.Net.WebUtility.HtmlEncode(title)} ({list.Count})</h3>");
        sb.Append("<table><thead><tr>");
        sb.Append("<th>File</th><th>Expected</th><th>Actual</th><th>Trigger</th><th>Reason</th>");
        sb.Append("</tr></thead><tbody>");

        foreach (var item in list)
        {
            sb.Append("<tr>");
            sb.Append($"<td>{System.Net.WebUtility.HtmlEncode(item.FileName)}</td>");
            sb.Append($"<td>{System.Net.WebUtility.HtmlEncode(item.ExpectedStatus)}</td>");
            sb.Append($"<td>{System.Net.WebUtility.HtmlEncode(item.ActualStatus)}</td>");
            sb.Append($"<td>{System.Net.WebUtility.HtmlEncode(item.ActualTrigger ?? item.ExpectedTrigger ?? "")}</td>");
            sb.Append($"<td>{System.Net.WebUtility.HtmlEncode(item.Reason ?? "")}</td>");
            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");
    }
}
