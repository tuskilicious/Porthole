using USBControl.Core;
using Xunit;

namespace USBControl.Tests;

public class AppStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "usbcontrol-tests-" + Guid.NewGuid().ToString("N"));

    private AppStore MakeStore() => new(_dir);

    [Fact]
    public void Save_And_Load_RoundTrips_Metadata()
    {
        var store = MakeStore();
        store.Load();
        var meta = store.GetOrCreateDevice("VID_045E&PID_0B12", "USB\\VID_045E&PID_0B12\\A1", "Pad");
        meta.FriendlyName = "Leverless";
        meta.Note = "main pad";
        var port = store.GetOrCreatePort("HUB1#3");
        port.Label = "Rear bottom left";
        port.Hidden = true;
        port.Zone = "Front";
        store.Save();

        var store2 = MakeStore();
        store2.Load();

        Assert.Equal("Leverless", store2.Data.Devices["VID_045E&PID_0B12"].FriendlyName);
        Assert.Equal("main pad", store2.Data.Devices["VID_045E&PID_0B12"].Note);
        Assert.Equal("Rear bottom left", store2.Data.Ports["HUB1#3"].Label);
        Assert.True(store2.Data.Ports["HUB1#3"].Hidden);
        Assert.Equal("Front", store2.Data.Ports["HUB1#3"].Zone);
    }

    [Fact]
    public void Profiles_RoundTrip()
    {
        var store = MakeStore();
        store.Load();
        store.Data.Profiles.Add(new Profile
        {
            Name = "Flight sim",
            Devices = { new ProfileEntry { Identity = "VID_0F0D&PID_00C1", Enabled = true, Order = 0, Name = "Stick" } },
        });
        store.Save();

        var store2 = MakeStore();
        store2.Load();
        var p = Assert.Single(store2.Data.Profiles);
        Assert.Equal("Flight sim", p.Name);
        Assert.Equal("VID_0F0D&PID_00C1", p.Devices[0].Identity);
    }

    [Fact]
    public void ImportPhoto_Copies_Into_PhotosDir()
    {
        var store = MakeStore();
        store.Load();
        var src = Path.Combine(_dir, "src.png");
        File.WriteAllBytes(src, new byte[] { 1, 2, 3 });

        var name = store.ImportPhoto(src);

        Assert.True(File.Exists(Path.Combine(store.PhotosDir, name)));
    }

    [Fact]
    public void Corrupt_Store_Falls_Back_To_Empty()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "store.json"), "{ not json !!!");
        var store = MakeStore();
        store.Load();
        Assert.Empty(store.Data.Devices);
        Assert.Empty(store.Data.Profiles);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }
}
