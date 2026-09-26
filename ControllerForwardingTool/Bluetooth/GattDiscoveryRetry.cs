using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace ControllerForwardingTool.Bluetooth;

internal static class GattDiscoveryRetry
{
    public static async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> discover,
        Func<T, GattCommunicationStatus> status, Action<T> release, Action<int, T> report,
        CancellationToken token, Func<CancellationToken, Task>? beforeRetry = null)
    {
        for (int attempt = 1; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            T result = await discover(token);
            report(attempt, result);
            if (status(result) != GattCommunicationStatus.Unreachable || attempt == 3) return result;
            release(result);
            await (beforeRetry?.Invoke(token) ?? Task.Delay(800, token));
        }
    }
}
