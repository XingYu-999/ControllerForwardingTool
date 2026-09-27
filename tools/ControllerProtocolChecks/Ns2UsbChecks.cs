using System.Buffers.Binary;
using ControllerForwardingTool.Bluetooth;
using ControllerForwardingTool.Core;
using ControllerForwardingTool.Input;
using ControllerForwardingTool.Usb;
using ControllerForwardingTool.VirtualDevice;

internal static class Ns2UsbChecks
{
    internal static async Task<int> RunAsync()
    {
        int count = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); count++; }
        Check(Ns2UsbRecovery.IsUsbCandidate(0x057E, 0x2069, 1, ""), "busy USB is detected without a readable serial");
        Check(!Ns2UsbRecovery.IsUsbCandidate(0x057E, 0x2069, 2, "real"), "BLE does not trigger USB recovery");
        Check(!Ns2UsbRecovery.IsUsbCandidate(0x057E, 0x2009, 1, "real"), "NS1 does not trigger NS2 recovery");
        Check(!Ns2UsbRecovery.IsUsbCandidate(0x057E, 0x2069, 1, "ns2prowin11-virtual"), "own virtual USB is excluded from recovery");
        var recovery = new Ns2UsbRecovery();
        Check(recovery.ShouldRetry(0, true, true, false), "failed initial USB open is retried");
        Check(!recovery.ShouldRetry(1, true, true, false), "USB retry is throttled while another app owns it");
        Check(recovery.ShouldRetry(3, true, true, false), "USB retries without a hotplug after owner exits");
        Check(!recovery.ShouldRetry(6, true, true, true), "working USB is never reset");
        Check(!recovery.ShouldRetry(9, false, true, false), "released registration interface is never reacquired");
        Check(!recovery.ShouldRetry(12, true, false, false), "unplugged USB is not retried");
        recovery.RequestRetry();
        Check(recovery.ShouldRetry(12, true, true, false), "manual detection bypasses retry delay");
        recovery.RequestRetry();
        Check(!recovery.ShouldRetry(12, true, true, true), "manual detection also preserves a working device");
        const ulong host = 0x123456789ABC, peer = 0x98E255C21688;
        var fake = new Controller(host, peer);
        var state = await Ns2UsbProtocol.RunAsync(UsbRegistrationAction.Inspect, host, fake.Exchange, default);
        Check(!state.Registered && fake.Writes == 0, "USB inspection never writes registration");
        state = await Ns2UsbProtocol.RunAsync(UsbRegistrationAction.Register, host, fake.Exchange, default);
        Check(state.Registered && state.Controller == peer && fake.Sequence.SequenceEqual(new byte[] { 1, 4, 2, 3 }), "USB address/key/challenge/commit plus readback");
        int written = fake.Writes;
        state = await Ns2UsbProtocol.RunAsync(UsbRegistrationAction.Register, host, fake.Exchange, default);
        Check(state.Registered && fake.Writes == written, "already registered does not rewrite flash");
        state = await Ns2UsbProtocol.RunAsync(UsbRegistrationAction.Delete, host, fake.Exchange, default);
        Check(!state.Registered && fake.Hosts.Length == 0, "delete is verified from device storage");
        fake.Hosts = [host, 0xAABBCCDDEEFF]; written = fake.Writes;
        try { await Ns2UsbProtocol.RunAsync(UsbRegistrationAction.Delete, host, fake.Exchange, default); Check(false, "must refuse foreign host clear"); }
        catch (InvalidOperationException) { Check(fake.Writes == written, "foreign registration is preserved"); }
        fake.Hosts = [];
        fake.BadChallenge = true;
        try { await Ns2UsbProtocol.RunAsync(UsbRegistrationAction.Register, host, fake.Exchange, default); Check(false, "must reject bad challenge"); }
        catch (InvalidOperationException) { Check(fake.Hosts.Length == 0, "challenge failure never commits"); }
        fake.BadChallenge = false; fake.IgnoreCommit = true;
        try { await Ns2UsbProtocol.RunAsync(UsbRegistrationAction.Register, host, fake.Exchange, default); Check(false, "must verify commit storage"); }
        catch (InvalidOperationException) { Check(true, "ACK alone does not mark registered"); }
        fake.IgnoreCommit = false; fake.Hosts = [host]; fake.IgnoreClear = true;
        try { await Ns2UsbProtocol.RunAsync(UsbRegistrationAction.Delete, host, fake.Exchange, default); Check(false, "must verify deletion"); }
        catch (InvalidOperationException) { Check(true, "clear ACK alone does not mark unregistered"); }
        using var canceled = new CancellationTokenSource(); canceled.Cancel(); written = fake.Writes;
        try { await Ns2UsbProtocol.RunAsync(UsbRegistrationAction.Delete, host, fake.Exchange, canceled.Token); Check(false, "cancellation"); }
        catch (OperationCanceledException) { Check(fake.Writes == written, "canceled requests never write"); }
        fake.BadAddress = true;
        try { Ns2UsbProtocol.ReadHosts(fake.Exchange); Check(false, "read address mismatch"); }
        catch (InvalidOperationException) { Check(true, "wrong memory reply rejected"); }
        fake.BadAddress = false; fake.Hosts = [host, host, host];
        try { Ns2UsbProtocol.ReadHosts(fake.Exchange); Check(false, "unknown storage layout"); }
        catch (InvalidOperationException) { Check(true, "unknown storage layout rejected"); }
        var vector = Ns2PairingProtocol.Confirmation(Convert.FromHexString("3503e92982877124bea80c664615834b"),
            Convert.FromHexString("5cf6ee792cdf05e1ba2b6325c41a5f10"), Convert.FromHexString("6fc6df8ad8fedf15bb8c15e91f320544"));
        Check(Convert.ToHexString(vector) == "134C97F511B9B6DD4D86FD40F536E9ED", "published AES vector");
        foreach (byte transport in new byte[] { 0, 1 })
        {
            var ack = new byte[9]; ack[0] = 0x15; ack[1] = 1; ack[2] = transport; ack[3] = 3; ack[5] = transport == 0 ? (byte)0xF8 : (byte)0x78; ack[8] = 1;
            Check(Ns2PairingProtocol.ValidateReply(ack, 3, 1, transport)[0] == 1, "USB and BLE ACK formats");
            ack[2] ^= 1;
            try { Ns2PairingProtocol.ValidateReply(ack, 3, 1, transport); Check(false, "wrong transport accepted"); }
            catch (InvalidOperationException) { Check(true, "wrong transport rejected"); }
        }

        ControllerState last = ControllerState.Neutral(DateTimeOffset.MinValue);
        var bridge = new ControllerInputBridge(s => last = s, () => new BridgeOptions(), _ => null);
        var pad = new GamepadDevice(15, "NS2 USB", 0x057E, 0x2069, true, ControllerLayout.Switch2Pro);
        bridge.Select(BridgeInputKind.Ns2Ble, null); bridge.SelectNs2Usb(pad);
        var input = new GamepadSnapshot(ControllerButtons.A, [0, 0, 0, 0, 0, 0], [], [], 80, true, null, null, []);
        bridge.Windows(new([pad], new Dictionary<uint, GamepadSnapshot> { [pad.Id] = input }, "", 1), 1);
        Check(last.Buttons == ControllerButtons.A, "NS2 automatic route accepts USB");
        bridge.Ble(ControllerState.Neutral(DateTimeOffset.Now) with { Buttons = ControllerButtons.B }, null);
        Check(last.Buttons == ControllerButtons.A, "BLE cannot overwrite active USB input");
        bridge.DisconnectBle(); Check(last.Buttons == ControllerButtons.A, "BLE disconnect cannot clear USB input");
        bridge.Windows(new([], new Dictionary<uint, GamepadSnapshot>(), "", 2), 2);
        Check(last.ReceivedAt == DateTimeOffset.MinValue && last.Buttons == 0, "unplug immediately neutralizes output");
        bridge.Ble(ControllerState.Neutral(DateTimeOffset.Now) with { Buttons = ControllerButtons.B }, null);
        Check(last.Buttons == ControllerButtons.B, "BLE resumes after USB unplug");
        bridge.Windows(new([], new Dictionary<uint, GamepadSnapshot>(), "", 3), 3);
        Check(last.Buttons == ControllerButtons.B, "empty SDL snapshots do not clear resumed BLE");
        return count;
    }

    private sealed class Controller(ulong host, ulong peer)
    {
        internal ulong[] Hosts = [];
        internal int Writes;
        internal bool BadChallenge, IgnoreCommit, IgnoreClear, BadAddress;
        internal List<byte> Sequence = [];
        private byte[] hostKey = [], deviceKey = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();
        internal byte[] Exchange(byte[] request, int length)
        {
            var reply = new byte[length]; reply[0] = request[0]; reply[1] = 1; reply[3] = request[3]; reply[5] = 0xF8;
            if (request[0] == 2)
            {
                uint address = BinaryPrimitives.ReadUInt32LittleEndian(request.AsSpan(12));
                int size = request[8];
                BinaryPrimitives.WriteUInt32LittleEndian(reply.AsSpan(8), (uint)size);
                BinaryPrimitives.WriteUInt32LittleEndian(reply.AsSpan(12), BadAddress ? address + 1 : address);
                if (address == 0x1FA000) reply[16] = (byte)Hosts.Length;
                else
                {
                    ulong value = Hosts[(address - 0x1FA008) / 0x28];
                    for (int i = 0; i < 6; i++) reply[16 + i] = (byte)(value >> (8 * (5 - i)));
                }
                return reply;
            }
            Writes++;
            if (request[0] == 3) { if (!IgnoreClear) Hosts = []; return reply; }
            Sequence.Add(request[3]); reply[8] = 1;
            switch (request[3])
            {
                case 1:
                    reply[10] = 1;
                    for (int i = 0; i < 6; i++) reply[11 + i] = (byte)(peer >> (8 * i));
                    break;
                case 4: hostKey = request[9..]; deviceKey.CopyTo(reply, 9); break;
                case 2:
                    Ns2PairingProtocol.Confirmation(hostKey, deviceKey, request.AsSpan(9)).CopyTo(reply, 9);
                    if (BadChallenge) reply[9] ^= 1;
                    break;
                case 3: if (!IgnoreCommit) Hosts = [host, host]; break;
            }
            return reply;
        }
    }
}
