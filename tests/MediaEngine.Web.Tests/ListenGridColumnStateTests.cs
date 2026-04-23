using MediaEngine.Web.Components.Listen;

namespace MediaEngine.Web.Tests;

public sealed class ListenGridColumnStateTests
{
    private static readonly string[] KnownColumns = ["title", "artist", "album", "dateAdded"];
    private static readonly string[] DefaultHiddenColumns = ["album"];

    [Fact]
    public void ResolveHiddenColumns_IgnoresUnknownSavedColumns()
    {
        var hiddenColumns = ListenGridColumnState.ResolveHiddenColumns(
            """["legacy_title","legacy_artist"]""",
            KnownColumns,
            DefaultHiddenColumns);

        Assert.Equal(DefaultHiddenColumns, hiddenColumns.OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void ResolveHiddenColumns_AppliesRecognizedSavedColumns()
    {
        var hiddenColumns = ListenGridColumnState.ResolveHiddenColumns(
            """["title","artist"]""",
            KnownColumns,
            DefaultHiddenColumns);

        Assert.Equal(["album", "dateAdded"], hiddenColumns.OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
    }
}
