using System.Runtime.InteropServices;
using ControllerForwardingTool.Core;

namespace ControllerForwardingTool.Input;

public static class KeyboardMapping
{
    public static bool CanBind(int key) => key is >= 1 and <= 254 && key is not (3 or 18 or 27 or 91 or 92 or 119) && key is not (>= 160 and <= 165);
    public static int[] ReadPressedKeys() => Enumerable.Range(1, 254)
        .Where(k => k is not (>= 160 and <= 165) && (GetAsyncKeyState(k) & 0x8000) != 0).ToArray();
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    public static string KeyName(int key) => key switch
    {
        1 => "鼠标左键", 2 => "鼠标右键", 4 => "鼠标中键", 5 => "鼠标侧键 1", 6 => "鼠标侧键 2",
        8 => "Backspace", 9 => "Tab", 13 => "Enter", 16 => "Shift", 17 => "Ctrl", 18 => "Alt", 20 => "Caps Lock", 27 => "Esc", 32 => "空格",
        33 => "Page Up", 34 => "Page Down", 35 => "End", 36 => "Home", 37 => "←", 38 => "↑", 39 => "→", 40 => "↓", 45 => "Insert", 46 => "Delete",
        19 => "Pause", 44 => "Print Screen", 91 or 92 => "Windows", 145 => "Scroll Lock",
        >= 48 and <= 90 => ((char)key).ToString(), >= 112 and <= 123 => $"F{key - 111}",
        >= 96 and <= 105 => $"Num {key - 96}", 186 => ";", 187 => "=", 188 => ",", 189 => "−", 190 => ".", 191 => "/", 192 => "`", 219 => "[", 220 => "\\", 221 => "]", 222 => "'",
        _ => $"按键 {key:X2}"
    };
    public static ControllerState Apply(ControllerState raw, IReadOnlySet<int> keys, Ns2ButtonMapping mapping,
        IReadOnlyDictionary<int, ControllerButtons> overrides, IReadOnlyDictionary<StickDirection, int>? stickBindings = null)
    {
        stickBindings ??= new Dictionary<StickDirection, int>();
        var stickKeys = Enum.GetValues<StickDirection>().Select(d => KeyboardStickMapping.KeyFor(d, stickBindings)).ToHashSet();
        var defaults = KeyboardMouseMapper.Map(k => keys.Contains(k) && !overrides.ContainsKey(k) && !stickKeys.Contains(k), 0, 0, .008, MouseEmulationMode.RightStick, raw.ReceivedAt);
        var left = KeyboardStickMapping.Read(StickSource.Left, keys, stickBindings, overrides);
        var right = KeyboardStickMapping.Read(StickSource.Right, keys, stickBindings, overrides);
        var mapped = mapping.Apply(raw with { Buttons = defaults.Buttons, LeftX = left.X, LeftY = left.Y,
            RightX = right.Active ? right.X : raw.RightX, RightY = right.Active ? right.Y : raw.RightY });
        var buttons = mapped.Buttons;
        foreach (var key in keys) if (overrides.TryGetValue(key, out var target)) buttons |= target;
        return mapped with { Buttons = buttons };
    }
}
