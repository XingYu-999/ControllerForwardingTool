using ControllerForwardingTool.Core;
using ControllerForwardingTool.Input;
using ControllerForwardingTool.ViewModels;
using ControllerForwardingTool.VirtualDevice;

internal static class MappingEditorChecks
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); count++; }
        var neutral = ControllerState.Neutral(DateTimeOffset.Now);
        var config = new BridgeOptions { Ns2Buttons = new() { Bindings = new() { [ControllerButtons.A] = ControllerButtons.X } },
            KeyboardOverrides = new() { ['P'] = ControllerButtons.X, ['Q'] = ControllerButtons.Y } };
        var editor = new MappingEditSession(false, new(ControllerButtons.X), [ControllerButtons.A], ['P']);
        editor.Observe([1], neutral);
        Check(!editor.Keys.Contains(1), "opening or confirmation click cannot bind primary mouse button");
        editor.Observe([], neutral);
        editor.Observe(['T'], neutral with { Buttons = ControllerButtons.B });
        Check(editor.Keys.Contains('T') && editor.Buttons.Contains(ControllerButtons.B), "single editor accepts physical and keyboard sources together");
        Check(config.KeyboardOverrides.Count == 2 && config.Ns2Buttons.Target(ControllerButtons.B) == ControllerButtons.B,
            "recording sources cannot change live configuration before confirmation");
        editor.Observe(['T'], neutral with { Buttons = ControllerButtons.B });
        Check(editor.Keys.Count == 2 && editor.Buttons.Count == 2, "held inputs do not create duplicate sources or finish editor");
        editor.SetMouseLeft(true);
        var result = editor.Apply(config);
        Check(result.KeyboardOverrides[1] == ControllerButtons.X && result.KeyboardOverrides['T'] == ControllerButtons.X &&
            result.Ns2Buttons.Target(ControllerButtons.B) == ControllerButtons.X, "confirmation commits both source types and explicit left mouse choice");
        Check(result.KeyboardOverrides['Q'] == ControllerButtons.Y && config.KeyboardOverrides.Count == 2, "confirmation copies data and preserves unrelated rules");
        editor.SetMouseLeft(false); editor.RemoveKey('P'); editor.RemoveButton(ControllerButtons.A);
        result = editor.Apply(config);
        Check(!result.KeyboardOverrides.ContainsKey(1) && !result.KeyboardOverrides.ContainsKey('P') &&
            result.Ns2Buttons.Target(ControllerButtons.A) == ControllerButtons.None, "removing physical and supplementary sources disables only removed bindings");
        editor.Cancel();
        Check(ReferenceEquals(editor.Apply(config), config), "cancel discards the entire multi-source edit");

        var keyRule = new MappingEditSession(false, new(ControllerButtons.X), [], ['P']);
        keyRule.Target = new(ControllerButtons.Plus);
        result = keyRule.Apply(config);
        Check(result.KeyboardOverrides['P'] == ControllerButtons.Plus && result.Ns2Buttons.Target(ControllerButtons.A) == ControllerButtons.X &&
            result.KeyboardOverrides['Q'] == ControllerButtons.Y, "editing a keyboard rule's destination preserves other sources of its old output");
        var buttonRule = new MappingEditSession(false, new(ControllerButtons.X), [ControllerButtons.A], []);
        buttonRule.Target = new(ControllerButtons.Minus); result = buttonRule.Apply(config);
        Check(result.Ns2Buttons.Target(ControllerButtons.A) == ControllerButtons.Minus && result.KeyboardOverrides['P'] == ControllerButtons.X,
            "editing physical rule destination does not move keyboard rules");
        var defaults = new MappingEditSession(true, new(ControllerButtons.B), [], ['J', 32]);
        defaults.RemoveKey('J'); result = defaults.Apply(new());
        Check(result.KeyboardOverrides['J'] == ControllerButtons.None && result.KeyboardOverrides[32] == ControllerButtons.B,
            "removing a default keyboard source disables it instead of restoring the default");
        defaults = new(true, new(ControllerButtons.B), [], [32]); defaults.SetMouseLeft(true); defaults.Cancel();
        Check(defaults.Apply(config) == config, "cancel also discards explicit left mouse selection");

        var axis = new MappingEditSession(true, new(Direction: StickDirection.LeftUp), [], ['W']);
        axis.Observe([], null); axis.Observe(['T'], null);
        Check(axis.Keys.SequenceEqual(new[] { (int)'T' }), "recording a direction replaces its key instead of silently keeping several");
        result = axis.Apply(new());
        Check(KeyboardStickMapping.KeyFor(StickDirection.LeftUp, result.KeyboardStickBindings) == 'T' && result.KeyboardOverrides['W'] == ControllerButtons.None,
            "changing a direction rule removes the previous default direction source");
        axis.Target = new(ControllerButtons.ZL); result = axis.Apply(new());
        Check(result.KeyboardStickBindings[StickDirection.LeftUp] == 0 && result.KeyboardOverrides['T'] == ControllerButtons.ZL,
            "keyboard direction rule can become a button without leaving the old direction active");
        axis.Target = new(Direction: StickDirection.RightDown); axis.SetMouseLeft(true); result = axis.Apply(new());
        Check(axis.Keys.SequenceEqual(new[] { 1 }) && result.KeyboardStickBindings[StickDirection.RightDown] == 1,
            "left mouse is independently configurable for keyboard direction rules");
        var illegal = new MappingEditSession(false, new(Direction: StickDirection.LeftUp), [], ['P']);
        bool rejected = false; try { illegal.Apply(config); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "physical source mode never allows keyboard keys to replace stick axes");

        editor = new(false, new(ControllerButtons.B), [], []);
        editor.Observe(['P'], neutral with { Buttons = ControllerButtons.A });
        Check(editor.Keys.Count == 0 && editor.Buttons.Count == 0, "input held while opening editor must first release");
        editor.Observe([], neutral); editor.Observe([119], neutral);
        Check(editor.Keys.Count == 0, "reserved control keys cannot be recorded");
        editor.Observe([], neutral); editor.Observe([27], neutral);
        Check(editor.Cancelled && editor.Apply(config) == config, "Escape cancels unconfirmed edit");
        var quick = new QuickMappingSession(true, VirtualControllerMode.Ns1Pro, ControllerButtons.B);
        quick.Observe([], null); quick.Observe([1], null);
        Check(!quick.Completed && quick.KeyboardButtons.Count == 0, "full wizard also ignores ordinary primary mouse clicks");
        quick.BindMouseLeft();
        Check(quick.Completed && quick.KeyboardButtons[1] == ControllerButtons.B, "full wizard has an explicit left mouse binding action");
        var stick = new QuickMappingSession(false, VirtualControllerMode.Ns1Pro);
        stick.BindMouseLeft(); Check(stick.Index == 0, "explicit mouse action cannot replace a physical stick");

        var rows = new[] { new ButtonMappingRow(ControllerButtons.A, ControllerButtons.X, VirtualControllerMode.Ns1Pro) };
        var rules = MainViewModel.BuildMappingRules(false, VirtualControllerMode.Ns1Pro, rows, config.Ns2Buttons, config.KeyboardOverrides, config.KeyboardStickBindings);
        Check(rules.Any(r => r.ControllerSource == ControllerButtons.A) && rules.Any(r => r.KeyboardKey == 'P') && rules.Any(r => r.KeyboardKey == 'Q'),
            "all mapping rules includes physical and supplementary keyboard rules");
        Check(!rules.Any(r => r.KeyboardKey == 'W'), "physical rule list cannot fabricate default WASD stick bindings");
        rules = MainViewModel.BuildMappingRules(true, VirtualControllerMode.Ns1Pro, rows, new(), new Dictionary<int, ControllerButtons> { ['P'] = ControllerButtons.Plus }, new Dictionary<StickDirection, int>());
        Check(rules.Any(r => r.KeyboardKey == 'W' && r.Destination.Direction == StickDirection.LeftUp) &&
            rules.Any(r => r.KeyboardKey == 'J' && r.Destination.Button == ControllerButtons.B) &&
            rules.Any(r => r.KeyboardKey == 1 && r.Destination.Button == ControllerButtons.ZR) && rules.Any(r => r.KeyboardKey == 'P'),
            "all keyboard rules includes directions, default buttons, mouse buttons and custom bindings");
        Check(rules.All(r => r.ControllerSource is null), "keyboard-only catalog avoids duplicate logical controller rows");
        var keyboardGroups = MainViewModel.GroupMappingRules(rules, VirtualControllerMode.Ns1Pro);
        var defaultB = keyboardGroups.Single(g => g.Destination == new MappingDestination(ControllerButtons.B));
        Check(defaultB.KeyboardKeys.Contains('J') && defaultB.KeyboardKeys.Contains(32) && !defaultB.HasControllerSources,
            "keyboard defaults sharing an output are combined into the same card");
        Check(keyboardGroups.Any(g => g.Destination.Direction == StickDirection.LeftUp && g.KeyboardKeys.Contains('W')),
            "keyboard stick direction groups remain separate from button targets");

        var groupedConfig = new BridgeOptions { Ns2Buttons = new() { Bindings = new()
            { [ControllerButtons.A] = ControllerButtons.X, [ControllerButtons.B] = ControllerButtons.X, [ControllerButtons.Home] = ControllerButtons.Minus } },
            KeyboardOverrides = new() { ['P'] = ControllerButtons.X, [1] = ControllerButtons.X, ['Q'] = ControllerButtons.Y, ['Z'] = ControllerButtons.None } };
        var groupRows = new[] { ControllerButtons.A, ControllerButtons.B, ControllerButtons.Home, ControllerButtons.Minus, ControllerButtons.C }
            .Select(b => new ButtonMappingRow(b, groupedConfig.Ns2Buttons.Target(b), VirtualControllerMode.Ns1Pro));
        var groupedRules = MainViewModel.BuildMappingRules(false, VirtualControllerMode.Ns1Pro, groupRows, groupedConfig.Ns2Buttons,
            groupedConfig.KeyboardOverrides, groupedConfig.KeyboardStickBindings);
        var groups = MainViewModel.GroupMappingRules(groupedRules, VirtualControllerMode.Ns1Pro);
        var x = groups.Single(g => g.Destination == new MappingDestination(ControllerButtons.X));
        Check(x.ControllerSources.Length == 2 && x.KeyboardKeys.Length == 2 && x.SourceCountLabel == "4 个来源",
            "multiple physical, keyboard and mouse sources appear exactly once under their output");
        Check(x.ControllerSummary == "手柄：A、B" && x.KeyboardSummary == "键盘：P" && x.MouseSummary == "鼠标：左键",
            "group card identifies all source types without repeating output rules");
        Check(groups.Single(g => g.Destination.Button == ControllerButtons.Minus).ControllerSources.Length == 2,
            "two real buttons sharing Minus are combined");
        Check(groups.Single(g => g.Destination.Button == ControllerButtons.C).TargetLabel.Contains("此输出不支持") &&
            groups.Single(g => g.Destination == new MappingDestination()).KeyboardKeys.SequenceEqual(new[] { (int)'Z' }),
            "unsupported output stays separate from intentionally disabled bindings");
        var groupEditor = x.CreateEditor(false);
        Check(groupEditor.Buttons.Order().SequenceEqual(x.ControllerSources.Order()) && groupEditor.Keys.Order().SequenceEqual(x.KeyboardKeys.Order()),
            "double-click group loads every physical and keyboard/mouse source for confirmation");
        groupEditor.RemoveButton(ControllerButtons.A); groupEditor.RemoveKey('P');
        groupEditor.Observe([], neutral); groupEditor.Observe(['T'], neutral);
        var groupedResult = groupEditor.Apply(groupedConfig);
        Check(groupedResult.Ns2Buttons.Target(ControllerButtons.A) == ControllerButtons.None &&
            groupedResult.Ns2Buttons.Target(ControllerButtons.B) == ControllerButtons.X &&
            !groupedResult.KeyboardOverrides.ContainsKey('P') && groupedResult.KeyboardOverrides['T'] == ControllerButtons.X &&
            groupedResult.KeyboardOverrides[1] == ControllerButtons.X && groupedResult.KeyboardOverrides['Q'] == ControllerButtons.Y,
            "group edits remove/add selected sources while preserving its other sources and other outputs");
        groupEditor = x.CreateEditor(false); groupEditor.Target = new(ControllerButtons.Y);
        groupedResult = groupEditor.Apply(groupedConfig);
        Check(groupedResult.Ns2Buttons.Target(ControllerButtons.A) == ControllerButtons.Y && groupedResult.Ns2Buttons.Target(ControllerButtons.B) == ControllerButtons.Y &&
            groupedResult.KeyboardOverrides['P'] == ControllerButtons.Y && groupedResult.KeyboardOverrides[1] == ControllerButtons.Y && groupedResult.KeyboardOverrides['Q'] == ControllerButtons.Y,
            "changing a group target moves all group sources and retains existing destination sources");
        groupEditor.Cancel(); Check(ReferenceEquals(groupEditor.Apply(groupedConfig), groupedConfig), "cancel discards the whole grouped edit");
        foreach (var mode in new[] { VirtualControllerMode.DualSense, VirtualControllerMode.DualSenseEdge })
        {
            var aliases = MainViewModel.GroupMappingRules(new MappingRuleRow[]
            {
                new("手柄 · Capture", "", new(ControllerButtons.Capture), ControllerButtons.Capture),
                new("手柄 · Touchpad", "", new(ControllerButtons.Touchpad), ControllerButtons.Touchpad),
                new("键盘 · P", "", new(ControllerButtons.Touchpad), KeyboardKey: 'P'),
                new("鼠标左键", "", new(ControllerButtons.Capture), KeyboardKey: 1)
            }, mode);
            Check(aliases.Count == 1 && aliases[0].TargetLabel == "触摸板按下" && aliases[0].Sources.Count == 4,
                $"{mode} output aliases group by the actual output button");
            var aliasEdit = aliases[0].CreateEditor(false).Apply(new());
            Check(MappingPreview.OutputButtons(aliasEdit.Ns2Buttons.Target(ControllerButtons.Touchpad), mode) == ControllerButtons.Touchpad &&
                MappingPreview.OutputButtons(aliasEdit.KeyboardOverrides['P'], mode) == ControllerButtons.Touchpad,
                $"{mode} grouped alias confirmation preserves actual output behavior");
        }
        return count;
    }
}
