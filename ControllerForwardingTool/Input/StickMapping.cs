using ControllerForwardingTool.Core;

namespace ControllerForwardingTool.Input;

public enum StickSource { Left, Right }
public enum StickDirection { LeftUp, LeftDown, LeftLeft, LeftRight, RightUp, RightDown, RightLeft, RightRight }

/// <summary>Physical sticks only: copy both axes from the same raw/calibrated source.</summary>
public sealed record StickMapping
{
    public StickSource Left { get; init; } = StickSource.Left;
    public StickSource Right { get; init; } = StickSource.Right;
    public StickMapping Normalize() => new()
    {
        Left = Enum.IsDefined(Left) ? Left : StickSource.Left,
        Right = Enum.IsDefined(Right) ? Right : StickSource.Right
    };
    public ControllerState Apply(ControllerState state) => state with
    {
        LeftX = Left == StickSource.Left ? state.LeftX : state.RightX,
        LeftY = Left == StickSource.Left ? state.LeftY : state.RightY,
        RightX = Right == StickSource.Right ? state.RightX : state.LeftX,
        RightY = Right == StickSource.Right ? state.RightY : state.LeftY
    };
}

public static class KeyboardStickMapping
{
    public static int KeyFor(StickDirection direction, IReadOnlyDictionary<StickDirection, int> bindings) =>
        bindings.TryGetValue(direction, out int key) ? key : direction switch
        {
            StickDirection.LeftUp => 'W', StickDirection.LeftDown => 'S',
            StickDirection.LeftLeft => 'A', StickDirection.LeftRight => 'D', _ => 0
        };
    public static Dictionary<StickDirection, int> Normalize(IReadOnlyDictionary<StickDirection, int>? bindings) =>
        (bindings ?? new Dictionary<StickDirection, int>()).Where(p => Enum.IsDefined(p.Key) && (p.Value == 0 || KeyboardMapping.CanBind(p.Value))).ToDictionary();
    public static string Name(StickDirection direction) => direction switch
    {
        StickDirection.LeftUp => "左摇杆 ↑", StickDirection.LeftDown => "左摇杆 ↓",
        StickDirection.LeftLeft => "左摇杆 ←", StickDirection.LeftRight => "左摇杆 →",
        StickDirection.RightUp => "右摇杆 ↑", StickDirection.RightDown => "右摇杆 ↓",
        StickDirection.RightLeft => "右摇杆 ←", _ => "右摇杆 →"
    };
    public static (ushort X, ushort Y, bool Active) Read(StickSource stick, IReadOnlySet<int> keys,
        IReadOnlyDictionary<StickDirection, int> bindings, IReadOnlyDictionary<int, ControllerButtons> buttons)
    {
        int start = stick == StickSource.Left ? 0 : 4;
        bool Down(int offset)
        {
            int key = KeyFor((StickDirection)(start + offset), bindings);
            return key != 0 && keys.Contains(key) && !buttons.ContainsKey(key);
        }
        bool up = Down(0), down = Down(1), left = Down(2), right = Down(3);
        double x = (right ? 1 : 0) - (left ? 1 : 0), y = (down ? 1 : 0) - (up ? 1 : 0);
        double length = Math.Max(1, Math.Sqrt(x * x + y * y));
        return (StickCoordinates.FromSdl(x / length), StickCoordinates.FromSdl(y / length, true), up || down || left || right);
    }
}
