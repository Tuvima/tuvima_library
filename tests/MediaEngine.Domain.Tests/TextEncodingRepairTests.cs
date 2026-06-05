using MediaEngine.Domain.Services;

namespace MediaEngine.Domain.Tests;

public sealed class TextEncodingRepairTests
{
    [Fact]
    public void RepairMojibake_FixesEdithPiafArtistName()
    {
        var repaired = TextEncodingRepair.RepairMojibake("\u0102\u2030dith Piaf - La Vie en rose");

        Assert.Equal("\u00C9dith Piaf - La Vie en rose", repaired);
    }
}
