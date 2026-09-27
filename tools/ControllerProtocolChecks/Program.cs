using System.Buffers.Binary;
using System.Net.Sockets;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using ControllerForwardingTool.Core;
using ControllerForwardingTool.Input;
using ControllerForwardingTool.Protocol.Ns1;
using ControllerForwardingTool.VirtualDevice;
using ControllerForwardingTool.Views;

int checks = 0;
void Check(bool value, string name)
{
    if (!value) throw new Exception(name);
    checks++;
}
// A selection pulse must stop on its own; a subsequent game's feedback must not inherit its timer.
var selectionPlayback = new ControllerForwardingTool.Bluetooth.BleRumblePlayback();
var selectionPulse = Pro2OutputPacketMapper.BuildOrdinaryPacket(64, 64, "input-identify") with
    { PlaybackDuration = TimeSpan.FromMilliseconds(250), GainOverride = 1 };
Check(selectionPlayback.Next(selectionPulse, TimeSpan.Zero) == selectionPulse, "selection pulse starts");
Check(selectionPlayback.Next(null, TimeSpan.FromMilliseconds(249)) == selectionPulse, "selection pulse lasts until 250 ms");
Check(selectionPlayback.Next(null, TimeSpan.FromMilliseconds(250)) is { Active: false }, "selection pulse stops at 250 ms");
Check(selectionPlayback.Next(null, TimeSpan.FromMilliseconds(500)) is null, "selection pulse does not restart");
selectionPlayback.Next(selectionPulse, TimeSpan.FromMilliseconds(600));
var gameFeedback = Pro2OutputPacketMapper.BuildOrdinaryPacket(128, 128, "game");
selectionPlayback.Next(gameFeedback, TimeSpan.FromMilliseconds(700));
Check(selectionPlayback.Next(null, TimeSpan.FromSeconds(2)) == gameFeedback, "game feedback replaces selection timeout");
selectionPlayback.Next(selectionPulse, TimeSpan.FromSeconds(3));
selectionPlayback.Next(selectionPulse, TimeSpan.FromMilliseconds(3100));
Check(selectionPlayback.Next(null, TimeSpan.FromMilliseconds(3250)) == selectionPulse, "another click restarts pulse duration");
Check(selectionPlayback.Next(null, TimeSpan.FromMilliseconds(3350)) is { Active: false }, "repeated click still stops");
selectionPlayback.Next(selectionPulse, TimeSpan.FromSeconds(4));
selectionPlayback.Next(Pro2OutputPacketMapper.BuildOrdinaryPacket(0, 0, "input-switch"), TimeSpan.FromMilliseconds(4100));
Check(selectionPlayback.Next(null, TimeSpan.FromMilliseconds(4200)) is null, "source switch cancels selection pulse");
short Read(byte[] data, int at) => BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(at));
var source = ControllerState.Neutral(DateTimeOffset.Now) with
{
    AccelX = 1024, AccelY = -2048, AccelZ = 4096,
    GyroX = 16384, GyroY = -8192, GyroZ = 4096,
    Buttons = ControllerButtons.A | ControllerButtons.GL, LeftX = 4095, LeftY = 0
};
var encoder = new Ns1ReportEncoder();
var encoded = encoder.Encode(source);
Check(encoded.Length == 64 && encoded[0] == 0x30 && encoded[3] == 8, "buttons/header");
Check(encoded[6] == 255 && encoded[7] == 15 && encoded[8] == 0, "stick packing");
for (int at = 13; at < 49; at += 12)
{
    // Decode as SDL's NS1 driver and compare to the canonical NS2 coordinates.
    Check(-Read(encoded, at + 2) / 4096.0 == .25 && Read(encoded, at + 4) / 4096.0 == 1 &&
        -Read(encoded, at) / 4096.0 == .5, "accel axes and gravity");
    Check(Math.Abs(-Read(encoded, at + 8) * 936.0 / 13371 - 1000) < .04 &&
        Math.Abs(Read(encoded, at + 10) * 936.0 / 13371 - 250) < .04 &&
        Math.Abs(-Read(encoded, at + 6) * 936.0 / 13371 - 500) < .04, "gyro axes and physical units");
}
Check(encoder.Encode(source, false).AsSpan(13, 36).ToArray().All(b => b == 0), "disabled IMU");
Check(Read(encoder.Encode(source with { AccelX = short.MinValue }), 15) == short.MaxValue, "negation saturates");

byte[] Command(byte subcommand, byte value)
{
    byte[] report = new byte[49]; report[0] = 1; report[10] = subcommand; report[11] = value;
    return report;
}
var device = new UsbIpNs1Device();
device.Publish(source);
Check(device.ReadInput(64).AsSpan(13, 36).ToArray().All(b => b == 0), "IMU off until host enable");
device.HandleInterruptOut(Command(0x40, 1));
var ack = device.ReadInput(64);
Check(ack[0] == 0x21 && ack[13] == 0x80 && ack[14] == 0x40, "IMU enable ACK");
Check(Read(device.ReadInput(64), 17) == 4096, "IMU enabled on device");
var spi = Command(0x10, 0x20); spi[12] = 0x60; spi[15] = 24;
device.HandleInterruptOut(spi);
var calibration = device.ReadInput(64);
Check(Read(calibration, 26) == 16384 && Read(calibration, 38) == 13371, "SPI matches motion units");
device.HandleInterruptOut(Command(0x40, 0)); device.ReadInput(64);
Check(Read(device.ReadInput(64), 17) == 0, "IMU disable");
device.ResetSession();
device.Publish(source); var first = device.ReadInput(64); var second = device.ReadInput(64);
Check(first[1] != second[1], "fresh report timer without source changes");
device.HandleInterruptOut(Command(0x40, 1)); device.ReadInput(64);
Thread.Sleep(280);
Check(device.ReadInput(64).AsSpan(13, 36).ToArray().All(b => b == 0), "lost source zeros motion");

byte[] neutral = [0x10, 0, 0, 1, 0x40, 0x40, 0, 1, 0x40, 0x40];
Check(Pro2OutputPacketMapper.TryMapFeedback(VirtualControllerMode.Ns1Pro, neutral, out var stop, out _) && !stop.Active, "neutral rumble stays off");
byte[] rumble = [0x10, 0, 0, 0x89, 0x40, 0x62, 0, 1, 0x40, 0x40];
Check(Pro2OutputPacketMapper.TryMapFeedback(VirtualControllerMode.Ns1Pro, rumble, out var active, out _) && active.Active, "NS1 rumble-only output");
// Table code 0x88 is about 0.501 amplitude, so physical envelope should be half strength.
var settings = WindowsRumble.FromPacket(active, 1);
Check(settings.Low > 30000 && settings.Low < 35000 && settings.High > 30000 && settings.High < 35000, "log amplitude decoding");
Check(active.Report.AsSpan(18, 5).SequenceEqual(stop.Report.AsSpan(18, 5)), "independent right side remains off");
byte[] ble = new byte[33];
Check(Pro2BleRumblePacketEncoder.TryEncodeRaw02(active.Report, 0, ble, out bool on, out _) && on, "NS1 feedback reaches BLE encoder");
Check(Pro2BleRumblePacketEncoder.TryEncodeRaw02(stop.Report, 1, ble, out on, out _) && !on, "explicit physical rumble stop");
var piggyback = Command(0x30, 1); rumble.AsSpan(2, 8).CopyTo(piggyback.AsSpan(2));
Check(Pro2OutputPacketMapper.TryMapFeedback(VirtualControllerMode.Ns1Pro, piggyback, out active, out _) && active.Active, "subcommand rumble");
Check(Pro2OutputPacketMapper.TryMapFeedback(VirtualControllerMode.Ns1Pro, Command(0x48, 0), out stop, out _) && !stop.Active, "disable vibration stops motors");
Check(!Pro2OutputPacketMapper.TryMapFeedback(VirtualControllerMode.Ns1Pro, [0x80, 1], out _, out _), "USB handshake is not rumble");
Check(!Pro2OutputPacketMapper.TryMapFeedback(VirtualControllerMode.Ns1Pro, rumble.AsSpan(0, 9), out _, out _), "truncated report rejected");

var rate = new InputReportRate();
Check(rate.Read(0) is null, "no fabricated initial rate");
for (ulong i = 0; i <= 250; i++)
{
    double now = i / 250.0;
    rate.Observe(i, now); rate.Observe(i, now); rate.Observe(i, now);
}
Check(rate.Read(1) == 250, "250 Hz with duplicate sub-samples");
Check(rate.Read(2.1) == 0, "rate clears on no reports");
Check(new InputReportRate().Read(2.1) is null, "device rates remain separate");

// Exercise the actual session callback chain via USB/IP, without attaching a driver.
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
await using (var session = new VirtualControllerSession())
{
    var mapped = new TaskCompletionSource<Pro2OutputPacket>(TaskCreationOptions.RunContinuationsAsynchronously);
    session.RumbleReceived += packet => { if (packet.Active) mapped.TrySetResult(packet); };
    await session.StartBackendOnlyAsync(VirtualControllerMode.Ns1Pro, timeout.Token);
    int port = int.Parse(session.Endpoints.Split(':').Last());
    using var client = new TcpClient(); await client.ConnectAsync("127.0.0.1", port, timeout.Token);
    var stream = client.GetStream();
    byte[] import = new byte[40]; import[0] = 1; import[1] = 0x11; import[2] = 0x80; import[3] = 3;
    import[8] = (byte)'1'; import[9] = (byte)'-'; import[10] = (byte)'1';
    await stream.WriteAsync(import, timeout.Token);
    await stream.ReadExactlyAsync(new byte[320], timeout.Token);
    byte[] transfer = new byte[48 + rumble.Length];
    BinaryPrimitives.WriteUInt32BigEndian(transfer, 1);
    BinaryPrimitives.WriteUInt32BigEndian(transfer.AsSpan(4), 1);
    BinaryPrimitives.WriteUInt32BigEndian(transfer.AsSpan(16), 1);
    BinaryPrimitives.WriteInt32BigEndian(transfer.AsSpan(24), rumble.Length);
    rumble.CopyTo(transfer, 48);
    await stream.WriteAsync(transfer, timeout.Token);
    await stream.ReadExactlyAsync(new byte[48], timeout.Token);
    Check((await mapped.Task.WaitAsync(timeout.Token)).Active && session.FeedbackCount == 1, "NS1 session emits physical rumble callback");
    session.Publish(source with { ReceivedAt = DateTimeOffset.Now });
    byte[] inputRequest = new byte[48];
    BinaryPrimitives.WriteUInt32BigEndian(inputRequest, 1);
    BinaryPrimitives.WriteUInt32BigEndian(inputRequest.AsSpan(4), 2);
    BinaryPrimitives.WriteUInt32BigEndian(inputRequest.AsSpan(12), 1);
    BinaryPrimitives.WriteUInt32BigEndian(inputRequest.AsSpan(16), 1);
    BinaryPrimitives.WriteInt32BigEndian(inputRequest.AsSpan(24), 64);
    await stream.WriteAsync(inputRequest, timeout.Token);
    byte[] inputReply = new byte[112];
    await stream.ReadExactlyAsync(inputReply, timeout.Token);
    Check(inputReply[48] == 0x30 && inputReply[51] == 8, "NS1 session sends input report");
    while (session.Sent == 0) await Task.Delay(1, timeout.Token);
    Check(session.Sent == 1, "NS1 diagnostics count actual reports");
}
checks += await Ns2UsbChecks.RunAsync();
checks += KeyboardMouseChecks.Run();
checks += AllInputMappingChecks.Run();
checks += MappingCaptureChecks.Run();
checks += QuickMappingChecks.Run();
checks += HybridInputChecks.Run();
checks += MappingEditorChecks.Run();
checks += StickEditorChecks.Run();
checks += MappingDraftChecks.Run();
Console.WriteLine($"PASS: {checks} protocol, feedback, USB registration, keyboard/mouse, and report-rate checks.");

if (args.Length == 2 && args[0] == "--render-hybrid-mapping") HybridMappingPreview.Render(args[1]);
if (args.Length == 2 && args[0] == "--check-mapping-ui") MappingUiChecks.Run(args[1]);

if (args.Length == 2 && args[0] == "--render")
{
    AppBuilder.Configure<Application>().UsePlatformDetect().SetupWithoutStarting();
    Directory.CreateDirectory(args[1]);
    foreach (var layout in new[] { ControllerLayout.Switch2Pro, ControllerLayout.DualSenseEdge })
    {
        var control = new ControllerTesterControl { Layout = layout, IsOnline = true,
            Buttons = ControllerButtons.GL, Width = 600, Height = 440 };
        control.Measure(new Size(600, 440)); control.Arrange(new Rect(0, 0, 600, 440));
        using var bitmap = new RenderTargetBitmap(new PixelSize(600, 440), new Vector(96, 96));
        bitmap.Render(control); bitmap.Save(Path.Combine(args[1], $"{layout}.png"), PngBitmapEncoderOptions.Default);
    }
    Console.WriteLine("Rendered controller diagrams.");
}
if (args.Length == 2 && args[0] == "--render-keyboard")
{
    AppBuilder.Configure<Application>().UsePlatformDetect().SetupWithoutStarting();
    Directory.CreateDirectory(args[1]);
    foreach (int width in new[] { 1100, 760 })
    {
        var control = new KeyboardMouseControl { Width = width, Height = 360,
            PressedKeys = ['W', 'J', 1, 5], SelectedKey = 'P', MouseDelta = new Vector(16, -12) };
        control.Measure(new Size(width, 360)); control.Arrange(new Rect(0, 0, width, 360));
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, 360), new Vector(96, 96));
        bitmap.Render(control); bitmap.Save(Path.Combine(args[1], $"keyboard-{width}.png"), PngBitmapEncoderOptions.Default);
    }
    Console.WriteLine("Rendered raised keyboard/mouse input states.");
}
if (args.Length == 2 && args[0] == "--render-quick-mapping")
{
    AppBuilder.Configure<ControllerForwardingTool.App>().UsePlatformDetect().SetupWithoutStarting();
    Directory.CreateDirectory(args[1]);
    foreach (bool keyboard in new[] { true, false })
    {
        // Render the real dialog without creating a live VM, connecting devices or opening a window.
        var dialog = new QuickMappingDialog();
        dialog.FindControl<TextBlock>("ProgressText")!.Text = keyboard ? "键盘 / 鼠标 · 第 1 / 25 项" : "手柄 → 手柄 · 第 1 / 19 项";
        dialog.FindControl<TextBlock>("PromptText")!.Text = keyboard ? "请按下左摇杆 ↑映射源" : "请按下左摇杆映射源";
        dialog.FindControl<TextBlock>("HintText")!.Text = keyboard
            ? "请按一个源键；鼠标按键也可使用。分别为上、下、左、右录入一个方向键。"
            : "先松开按键，并让摇杆回中。推动要使用的源摇杆，按钮和摇杆按下不能代替摇杆。";
        var diagram = dialog.FindControl<ControllerTesterControl>("TargetDiagram")!;
        diagram.Layout = ControllerLayout.SwitchPro; diagram.SelectedButton = ControllerButtons.LeftStick;
        var content = (Avalonia.Controls.Control)dialog.Content!;
        content.Measure(new Size(620, double.PositiveInfinity));
        int height = (int)Math.Ceiling(content.DesiredSize.Height);
        content.Arrange(new Rect(0, 0, 620, height));
        using var bitmap = new RenderTargetBitmap(new PixelSize(620, height), new Vector(96, 96));
        bitmap.Render(content); bitmap.Save(Path.Combine(args[1], keyboard ? "keyboard-wizard.png" : "controller-wizard.png"), PngBitmapEncoderOptions.Default);
        dialog.Close();
    }
    Console.WriteLine("Rendered quick mapping dialogs.");
}
