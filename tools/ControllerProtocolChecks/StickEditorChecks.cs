using ControllerForwardingTool.Core;
using ControllerForwardingTool.Input;
using ControllerForwardingTool.VirtualDevice;

internal static class StickEditorChecks
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); count++; }
        var neutral = ControllerState.Neutral(DateTimeOffset.Now);
        var options = new BridgeOptions { KeyboardOverrides = new() { ['P'] = ControllerButtons.ZL },
            KeyboardStickBindings = new() { [StickDirection.RightUp] = 'T' } };
        var editor = new StickMappingEditSession(true, StickSource.Left, options);
        Check(editor.Keys.Count == 4 && editor.Keys[StickDirection.LeftUp] == 'W' && editor.Keys[StickDirection.LeftRight] == 'D',
            "stick editor loads all four directions with keyboard defaults");
        editor.Observe([], null); editor.Observe(['P'], null);
        Check(editor.Keys[StickDirection.LeftUp] == 'W', "direction input is not captured before choosing a direction");
        editor.BeginDirection(StickDirection.LeftUp); editor.Observe(['P'], null);
        Check(editor.Keys[StickDirection.LeftUp] == 'W', "keys held when starting a direction must first release");
        editor.Observe([], null); editor.Observe([1], null);
        Check(editor.Keys[StickDirection.LeftUp] == 'W', "ordinary left clicks never become direction sources");
        editor.Observe([], null); editor.Observe([119], null);
        Check(editor.Keys[StickDirection.LeftUp] == 'W', "reserved F8 cannot become a stick direction");
        void Record(StickMappingEditSession session, StickDirection direction, int key)
        { session.BeginDirection(direction); session.Observe([], null); session.Observe([key], null); }
        Record(editor, StickDirection.LeftUp, 'P');
        Record(editor, StickDirection.LeftDown, 'T');
        Record(editor, StickDirection.LeftLeft, 'F');
        Record(editor, StickDirection.LeftRight, 'H');
        Check(options.KeyboardOverrides['P'] == ControllerButtons.ZL && !options.KeyboardStickBindings.ContainsKey(StickDirection.LeftUp),
            "direction recording leaves the current configuration untouched before confirmation");
        var result = editor.Apply(options);
        Check(result.KeyboardStickBindings[StickDirection.LeftUp] == 'P' && result.KeyboardStickBindings[StickDirection.LeftDown] == 'T' &&
            result.KeyboardStickBindings[StickDirection.LeftLeft] == 'F' && result.KeyboardStickBindings[StickDirection.LeftRight] == 'H',
            "confirmation applies all four direction edits together");
        Check(!result.KeyboardOverrides.ContainsKey('P') && result.KeyboardStickBindings[StickDirection.RightUp] == 0,
            "new direction keys stop triggering previous button and other-stick mappings");
        var directionState = KeyboardMapping.Apply(neutral, new HashSet<int> { 'P', 'H' }, result.Ns2Buttons, result.KeyboardOverrides, result.KeyboardStickBindings);
        Check(directionState.LeftX > 2048 && directionState.LeftY > 2048 && directionState.Buttons == ControllerButtons.None,
            "confirmed keyboard directions move the correct axes without leaking old buttons");
        Check(KeyboardStickMapping.Read(StickSource.Left, new HashSet<int> { 'P', 'T' }, result.KeyboardStickBindings, result.KeyboardOverrides).Y == 2048,
            "opposite direction keys still cancel after editing");
        editor.RequireRelease(); editor.Observe(['K'], null);
        Check(editor.Recording is null && editor.Keys[StickDirection.LeftUp] == 'P', "tab changes disarm recording and retain its draft");
        editor.ClearDirection(StickDirection.LeftUp); result = editor.Apply(options);
        Check(result.KeyboardStickBindings[StickDirection.LeftUp] == 0, "cleared direction is explicitly unbound instead of restoring WASD");
        editor.BeginDirection(StickDirection.LeftUp); editor.BindMouseLeft(); result = editor.Apply(options);
        Check(result.KeyboardStickBindings[StickDirection.LeftUp] == 1, "direction left mouse binding requires the explicit action");
        editor.Cancel(); Check(ReferenceEquals(editor.Apply(options), options), "cancel discards all stick direction edits");

        var swap = new StickMappingEditSession(true, StickSource.Left, new());
        Record(swap, StickDirection.LeftUp, 'S'); Record(swap, StickDirection.LeftDown, 'W');
        result = swap.Apply(new());
        Check(result.KeyboardStickBindings[StickDirection.LeftUp] == 'S' && result.KeyboardStickBindings[StickDirection.LeftDown] == 'W',
            "swapping two direction keys does not erase either destination");
        var right = new StickMappingEditSession(true, StickSource.Right, options);
        Record(right, StickDirection.RightRight, 'H'); result = right.Apply(options);
        Check(result.KeyboardStickBindings[StickDirection.RightRight] == 'H' && KeyboardStickMapping.KeyFor(StickDirection.LeftUp, result.KeyboardStickBindings) == 'W',
            "right stick editing preserves the left stick's directions");

        var physical = new StickMappingEditSession(false, StickSource.Left, options);
        physical.Observe([], neutral with { RightX = 4095 });
        Check(physical.Source == StickSource.Left, "physical stick held when opening the axes tab must return to center");
        physical.Observe([], neutral); physical.Observe(['W'], neutral);
        physical.BeginDirection(StickDirection.LeftUp); physical.BindMouseLeft();
        Check(physical.Recording is null && physical.Source == StickSource.Left, "keyboard and mouse cannot replace physical stick axes");
        physical.Observe([], neutral); physical.Observe([], neutral with { Buttons = ControllerButtons.LeftStick });
        Check(physical.Source == StickSource.Left, "stick click cannot stand in for physical stick movement");
        physical.Observe([], neutral); physical.Observe([], neutral with { LeftX = 4095, RightX = 4095 });
        Check(physical.Source == StickSource.Left, "two simultaneous source sticks are not captured");
        physical.Observe([], neutral); physical.Observe([], neutral with { RightY = 4095 });
        Check(physical.Source == StickSource.Right && options.ControllerSticks.Left == StickSource.Left,
            "moving a single physical stick records only an uncommitted source choice");
        result = physical.Apply(options);
        var mapped = result.ControllerSticks.Apply(neutral with { LeftX = 3000, LeftY = 1000, RightX = 2200, RightY = 4000 });
        Check(mapped.LeftX == 2200 && mapped.LeftY == 4000 && mapped.RightX == 2200 && mapped.RightY == 4000,
            "physical stick choice maps both axes together and preserves the other output stick");
        Check(result.KeyboardOverrides['P'] == ControllerButtons.ZL && result.Ns2Buttons.Target(ControllerButtons.LeftStick) == ControllerButtons.LeftStick,
            "editing axes preserves click and supplemental button mappings");
        var click = new MappingEditSession(false, new(ControllerButtons.LeftStick), [ControllerButtons.LeftStick], []);
        click.RemoveButton(ControllerButtons.LeftStick); click.Observe([], neutral); click.Observe(['K'], neutral with { Buttons = ControllerButtons.A });
        result = physical.Apply(click.Apply(options));
        Check(result.ControllerSticks.Left == StickSource.Right && result.Ns2Buttons.Target(ControllerButtons.A) == ControllerButtons.LeftStick &&
            result.KeyboardOverrides['K'] == ControllerButtons.LeftStick,
            "one confirmation commits physical stick axes and multi-source stick click independently");
        click.RequireRelease(); click.Observe(['J'], neutral with { Buttons = ControllerButtons.B });
        Check(!click.Keys.Contains('J') && !click.Buttons.Contains(ControllerButtons.B), "returning to click tab cannot capture keys held on the axes tab");
        physical.Observe([27], neutral);
        Check(physical.Cancelled && ReferenceEquals(physical.Apply(options), options), "Escape discards physical stick source changes");
        return count;
    }
}
