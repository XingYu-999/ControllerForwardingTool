namespace ControllerForwardingTool.Bluetooth;

internal sealed class Ns2PendingReply(byte command, byte subcommand)
{
    private readonly TaskCompletionSource<byte[]> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<byte[]> Task => completion.Task;
    public bool Accept(byte[] bytes) => Ns2PairingProtocol.MatchesReply(bytes, command, subcommand) && completion.TrySetResult(bytes);
    public void Cancel() => completion.TrySetCanceled();
}
