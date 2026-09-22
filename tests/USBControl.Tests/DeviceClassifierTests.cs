using USBControl.App.Services;
using USBControl.Core;
using Xunit;

namespace USBControl.Tests;

public class DeviceClassifierTests
{
    [Theory]
    [InlineData("G512 RGB Mechanical Keyboard", "HIDClass", "keyboard")]
    [InlineData("G502 HERO Gaming Mouse", "HIDClass", "mouse")]
    [InlineData("Xbox One Controller (XINPUT)", "HIDClass", "controller")]
    [InlineData("Wireless Controller", "HIDClass", "controller")]
    [InlineData("Leverless Fightstick", "HIDClass", "controller")]
    [InlineData("HOTAS Stick", "HIDClass", "joystick")]
    [InlineData("Thrustmaster Flight Yoke", "HIDClass", "joystick")]
    [InlineData("G29 Racing Wheel", "HIDClass", "wheel")]
    [InlineData("USB Receiver", "HIDClass", "dongle")]
    [InlineData("Logitech Unifying Receiver", "HIDClass", "dongle")]
    [InlineData("Xbox Wireless Adapter for Windows", "Net", "dongle")]
    [InlineData("Generic Bluetooth Radio", "Bluetooth", "dongle")]
    [InlineData("Realtek 8812AU Wi-Fi Adapter", "Net", "network")]
    [InlineData("USB Ethernet Adapter", "Net", "network")]
    [InlineData("Studio Microphone", "MEDIA", "microphone")]
    [InlineData("G435 Wireless Headset", "MEDIA", "headset")]
    [InlineData("USB Audio Device", "MEDIA", "headset")]
    [InlineData("C922 Pro Webcam", "Camera", "webcam")]
    [InlineData("Pixel 8", "WPD", "phone")]
    [InlineData("SanDisk Ultra Flair", "DiskDrive", "storage")]
    [InlineData("Portable Device", "WPD", "storage")]
    [InlineData("HP LaserJet Printer", "Printer", "printer")]
    [InlineData("Some Gadget", "USB", "unknown")]
    public void Names_and_classes_map_to_the_expected_icon(string name, string cls, string expected)
    {
        var device = new UsbDeviceInfo { DisplayName = name, ClassName = cls, GameRelevant = cls == "HIDClass" };
        Assert.Equal(expected, DeviceClassifier.Classify(device));
    }

    [Fact]
    public void Hubs_are_hubs_whatever_their_name()
    {
        var hub = new UsbDeviceInfo { DisplayName = "USB Receiver Hub", ClassName = "USB", IsHub = true };
        Assert.Equal("hub", DeviceClassifier.Classify(hub));
    }
}
