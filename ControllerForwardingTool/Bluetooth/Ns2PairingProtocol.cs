using System.Security.Cryptography;

namespace ControllerForwardingTool.Bluetooth;

// Independently implemented from ndeadly/switch2_controller_research:
// commands.md (0x15) and bluetooth_interface.md (pairing and AES byte order).
internal static class Ns2PairingProtocol
{
    internal static bool IsAddress(ulong value) => value is > 0 and < 0xFFFFFFFFFFFF;

    internal static bool MatchesReply(ReadOnlySpan<byte> response, byte command, byte subcommand) =>
        response.Length >= 4 && response[0] == command && response[2] == 1 && response[3] == subcommand;

    internal static byte[] Packet(byte subcommand, ReadOnlySpan<byte> payload, byte transport = 1)
    {
        if (payload.Length > 255) throw new ArgumentOutOfRangeException(nameof(payload));
        byte[] packet = new byte[8 + payload.Length];
        packet[0] = 0x15; packet[1] = 0x91; packet[2] = transport; packet[3] = subcommand;
        packet[5] = (byte)payload.Length;
        payload.CopyTo(packet.AsSpan(8));
        return packet;
    }

    internal static byte[] ValidateReply(byte[] response, byte subcommand, int payloadLength, byte transport = 1)
    {
        if (response.Length < 8 + payloadLength || response[0] != 0x15 || response[2] != transport || response[3] != subcommand ||
            response[1] != 1 || response[5] != (transport == 0 ? 0xF8 : 0x78) || response[8] != 1)
            throw new InvalidOperationException($"主机注册 15/{subcommand:X2} 回复无效或被拒绝（长度 {response.Length}）");
        return response.AsSpan(8, payloadLength).ToArray();
    }

    internal static byte[] AddressPayload(ulong host)
    {
        if (!IsAddress(host)) throw new ArgumentException("无法确认本机蓝牙地址，未执行主机注册");
        byte[] payload = new byte[14]; payload[1] = 2;
        for (int i = 0; i < 6; i++) payload[2 + i] = payload[8 + i] = (byte)(host >> (8 * i));
        return payload;
    }

    internal static ulong ReadAddress(ReadOnlySpan<byte> data)
    {
        if (data.Length != 6) throw new ArgumentException("蓝牙地址必须为 6 字节");
        ulong value = 0;
        for (int i = 0; i < 6; i++) value |= (ulong)data[i] << (8 * i);
        return value;
    }

    internal static byte[] Confirmation(ReadOnlySpan<byte> hostKey, ReadOnlySpan<byte> deviceKey, ReadOnlySpan<byte> challenge)
    {
        if (hostKey.Length != 16 || deviceKey.Length != 16 || challenge.Length != 16)
            throw new ArgumentException("注册密钥和挑战必须为 16 字节");
        byte[] key = new byte[16], block = new byte[16];
        try
        {
            for (int i = 0; i < 16; i++)
            {
                key[15 - i] = (byte)(hostKey[i] ^ deviceKey[i]);
                block[15 - i] = challenge[i];
            }
            using var aes = Aes.Create();
            aes.Key = key;
            // The published wire vector is the AES output itself; do not reverse it again.
            return aes.EncryptEcb(block, PaddingMode.None);
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(block); }
    }

    internal static async Task<BleHostRegistration> RegisterAsync(ulong host, ulong controller,
        Func<byte[], CancellationToken, Task<byte[]>> exchange, Action<string> progress, CancellationToken token,
        byte transport = 1, Action<ulong>? controllerDiscovered = null)
    {
        if (transport > 1 || (!IsAddress(controller) && !(transport == 0 && controller == 0))) throw new ArgumentException("手柄蓝牙地址无效");
        byte[] addressPacket = Packet(1, AddressPayload(host), transport);
        byte[] hostKey = RandomNumberGenerator.GetBytes(16), challenge = RandomNumberGenerator.GetBytes(16);
        byte[]? deviceKey = null, expected = null;
        try
        {
            token.ThrowIfCancellationRequested();
            progress("正在注册主机：交换蓝牙地址（1/4）");
            byte[] addressReply = ValidateReply(await exchange(addressPacket, token), 1, 9, transport);
            ulong returnedAddress = ReadAddress(addressReply.AsSpan(3, 6));
            if (addressReply[2] != 1 || !IsAddress(returnedAddress) || (controller != 0 && returnedAddress != controller))
                throw new InvalidOperationException("主机注册地址回复与当前手柄不匹配，已中止");
            controllerDiscovered?.Invoke(returnedAddress);

            token.ThrowIfCancellationRequested();
            progress("正在注册主机：交换密钥（2/4）");
            deviceKey = ValidateReply(await exchange(Packet(4, WithPrefix(hostKey), transport), token), 4, 17, transport)[1..];

            token.ThrowIfCancellationRequested();
            progress("正在注册主机：验证密钥挑战（3/4）");
            byte[] answer = ValidateReply(await exchange(Packet(2, WithPrefix(challenge), transport), token), 2, 17, transport);
            expected = Confirmation(hostKey, deviceKey, challenge);
            if (!CryptographicOperations.FixedTimeEquals(expected, answer.AsSpan(1)))
                throw new InvalidOperationException("主机注册挑战验证失败，未提交注册");

            token.ThrowIfCancellationRequested();
            progress("正在注册主机：保存到手柄（4/4）");
            // Do not automatically resend this commit: a missing ACK is ambiguous.
            ValidateReply(await exchange(Packet(3, new byte[] { 0 }, transport), token), 3, 1, transport);
            token.ThrowIfCancellationRequested();
            return new(host, DateTimeOffset.UtcNow);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(hostKey); CryptographicOperations.ZeroMemory(challenge);
            if (deviceKey is not null) CryptographicOperations.ZeroMemory(deviceKey);
            if (expected is not null) CryptographicOperations.ZeroMemory(expected);
        }
    }

    private static byte[] WithPrefix(byte[] value)
    {
        byte[] payload = new byte[value.Length + 1]; value.CopyTo(payload, 1); return payload;
    }
}

// Evidence of a verified commit, not a Windows SMP bond. No key material is persisted.
public sealed record BleHostRegistration(ulong HostAddress, DateTimeOffset ConfirmedAt)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValid => Ns2PairingProtocol.IsAddress(HostAddress) && ConfirmedAt != default;
}
