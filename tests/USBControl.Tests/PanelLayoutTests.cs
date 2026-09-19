using USBControl.Core;
using Xunit;

namespace USBControl.Tests;

public class PanelLayoutTests
{
    [Fact]
    public void SnapX_Snaps_To_Grid_And_Clamps()
    {
        Assert.Equal(24, PanelLayoutMath.SnapX(15));
        Assert.Equal(24, PanelLayoutMath.SnapX(24));
        Assert.Equal(1400 - 238 - 8, PanelLayoutMath.SnapX(5000));
        Assert.Equal(0, PanelLayoutMath.SnapX(-100));
    }

    [Fact]
    public void SnapY_Snaps_And_Clamps()
    {
        Assert.Equal(48, PanelLayoutMath.SnapY(50));
        Assert.Equal(900 - 130 - 8, PanelLayoutMath.SnapY(5000));
        Assert.Equal(0, PanelLayoutMath.SnapY(-50));
    }

    [Fact]
    public void Overlaps_Respects_Gap()
    {
        // Exactly one gap apart on the same row → free.
        Assert.False(PanelLayoutMath.Overlaps((8, 8), (258, 8)));
        // 18px of vertical overlap (less than the gap) → colliding.
        Assert.True(PanelLayoutMath.Overlaps((8, 8), (8, 132)));
        // Far apart → clear.
        Assert.False(PanelLayoutMath.Overlaps((8, 8), (500, 500)));
    }

    [Fact]
    public void AutoPositions_Fills_Free_Slots_In_Row_Major_Order()
    {
        var occupied = new List<(double X, double Y)> { (8, 8) };
        var result = PanelLayoutMath.AutoPositions(new[] { "A", "B", "C" }, occupied);

        Assert.Equal(3, result.Count);
        Assert.Equal((258, 8), result["A"]);   // first free slot in row 1
        Assert.Equal((508, 8), result["B"]);   // second free slot in row 1
        Assert.Equal((758, 8), result["C"]);   // third free slot in row 1
    }

    [Fact]
    public void Store_Persists_PanelLayout()
    {
        var dir = Path.Combine(Path.GetTempPath(), "usbcontrol-panel-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new AppStore(dir);
            store.Load();
            store.Data.PanelLayout.Add(new PortLayout { PortKey = "HUB1#3", X = 258, Y = 150 });
            store.Save();

            var store2 = new AppStore(dir);
            store2.Load();
            var l = Assert.Single(store2.Data.PanelLayout);
            Assert.Equal("HUB1#3", l.PortKey);
            Assert.Equal(258, l.X);
            Assert.Equal(150, l.Y);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }
}
