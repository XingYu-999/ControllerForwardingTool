namespace ControllerForwardingTool.Bluetooth;

// Captured sequence from the research source described in docs/02.
// Its meaning and firmware compatibility are unverified. Version it separately
// from the transport so experiments can replace it without changing GATT code.
internal static class Ns2InitProfile
{
    public const string Version = "bridge-v6.2.32-r3-no-unnegotiated-pairing";

    public static IReadOnlyList<byte[]> ObservedSequence { get; } =
    [
        Convert.FromHexString("0391010D000800000100FFFFFFFFFFFF"),
        Convert.FromHexString("0791010100000000"),
        Convert.FromHexString("1691010100000000"),
        // 0x15/0x03 commits pairing data. It is not an input initialization command
        // and must never run without an address/key exchange and validation.
        Convert.FromHexString("0C91010200040000FF000000"),
        Convert.FromHexString("1191010300000000"),
        Convert.FromHexString("0A9101080014000001FFFFFFFFFFFFFFFF3500460000000000000000"),
        Convert.FromHexString("0C91010400040000FF000000"),
        Convert.FromHexString("0391010A0004000009000000"),
        Convert.FromHexString("1091010100000000"),
        Convert.FromHexString("0191010C00000000"),
        Convert.FromHexString("019101010004000000000000"),
        Convert.FromHexString("09910107000800000100000000000000"),
        Convert.FromHexString("0291010400080000097E0000A8300100"),
        Convert.FromHexString("0291010400080000097E0000E8300100")
    ];
}
