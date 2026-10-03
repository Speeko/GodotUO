// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.IO;
using System.Linq;
using Godot;
using GUO.Configuration;
using GUO.Game;
using GUO.Game.GameObjects;
using GUO.Game.Managers;
using GUO.Game.UI.Gumps;
using GUO.Input;
using GUO.Input.Gamepad;

namespace GUO.Host;

/// <summary>
/// The pad's controller feel (docs/wiki/Controller.md) driven by injected
/// joypad events in the world: the first-login offer of "Set controls" and
/// B skipping it; the LT menu wheel opening the backpack and the paperdoll,
/// the tap reopening the last, the middle opening nothing; the RT interact
/// radar choosing a mobile with the right stick, looking (X), using (A),
/// stepping (RB), its menu (Y), using on release, and picking a target for a
/// target cursor; R3 war mode and L3 always run; Start and the Options entry;
/// the wizard cancelled by three inputs, then run through with one job
/// rebound (and swapped), its wheel slice changed, and the saved map taking
/// effect; and the Steam Deck trackpad (a mouse beside the pad) leaving the
/// mode on Gamepad. A picture at each step.
/// </summary>
/// <remarks>
/// Runs in a scratch home (--pad-wheels-probe implies --scratch-profile), so
/// padbindings.json and the profile start new and the real ones are never
/// touched. Injected events come from no real pad: the layout is set to
/// "labels", as GamepadProbe does. Where no mobile is within reach it asks the
/// shard for a horse beside the character ("[add", an owner command).
/// </remarks>
internal static class PadWheelsProbe
{
    public static bool Passed { get; private set; }

    private static int _failed;
    private static string _dir;
    private static int _shot;

    private static void Check(string what, bool ok, string detail = "")
    {
        GD.Print($"[GUO] pad wheels probe: {(ok ? "ok  " : "FAIL")} {what}{(detail.Length > 0 ? " -- " + detail : "")}");

        if (!ok)
        {
            _failed++;
        }
    }

    private static async System.Threading.Tasks.Task Shot(Node host, string name)
    {
        await InputProbe.Wait(host, 3);
        string path = Path.Combine(_dir, $"{++_shot:D2}_{name}.png");
        Error e = host.GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"[GUO] pad wheels probe: shot {path} {(e == Error.Ok ? "" : e.ToString())}");
    }

    public static async System.Threading.Tasks.Task Run(Node host, string screenshotDir)
    {
        _dir = string.IsNullOrEmpty(screenshotDir) ? Path.Combine(OS.GetUserDataDir(), "pad-wheels") : screenshotDir;
        Directory.CreateDirectory(_dir);

        for (int i = 0; i < 1200 && Client.Game?.UO?.World == null; i++)
        {
            await InputProbe.Wait(host, 1);
        }

        if (!Client.Game.UO.World.InGame)
        {
            await InputProbe.EnterTheWorld(host, 200);
        }

        World world = Client.Game.UO.World;

        if (!world.InGame)
        {
            Check("got into the world", false);
            return;
        }

        await InputProbe.Wait(host, 60);
        Profile profile = ProfileManager.CurrentProfile;
        GamepadInput.Forced = true;
        profile.GamepadLayout = "labels";
        GamepadInput.ForgetLayouts();
        GD.Print($"[GUO] pad wheels probe: bindings file {PadBindings.FilePath}, offered {PadBindings.WizardOffered}");

        for (int i = 0; i < 120 && !PadArt.Warm(); i++)
        {
            await InputProbe.Wait(host, 1);
        }

        Check("the wheel and radar art is loaded before anything shows", PadArt.Ready);

        await Offer(host);
        await Wheel(host, world);
        await Radar(host, world);
        await Buttons(host, world, profile);
        await Wizard(host, world);
        await Trackpad(host, world);

        PadBindings.ResetToDefaults();
        GamepadInput.Forced = null;
        InputMode.PadBesideMouse = null;
        Passed = _failed == 0;
        GD.Print($"[GUO] pad wheels probe: {(Passed ? "PASS" : $"FAIL ({_failed})")}");
    }

    // --- the first login on a pad ---------------------------------------------------------------

    private static async System.Threading.Tasks.Task Offer(Node host)
    {
        // A scratch home: never offered yet. The first pad button brings it up.
        bool before = PadBindings.WizardOffered;
        await Button(host, JoyButton.DpadRight);

        for (int i = 0; i < 60 && !PadWizard.Offering; i++)
        {
            await InputProbe.Wait(host, 1);
        }

        Check("\"Set controls\" is offered after the first login on a pad", !before && PadWizard.Offering);
        await Shot(host, "offer");

        await Button(host, JoyButton.B);
        string file = File.Exists(PadBindings.FilePath) ? File.ReadAllText(PadBindings.FilePath) : "";
        Check("B skips the offer, and it is remembered (offered once)",
            !PadWizard.IsOpen && PadWizard.LastResult == "skipped" && file.Contains("\"wizardOffered\": true"),
            $"open {PadWizard.IsOpen}, result {PadWizard.LastResult}");
    }

    // --- LT: the menu wheel --------------------------------------------------------------------

    private static async System.Threading.Tasks.Task Wheel(Node host, World world)
    {
        Item backpack = world.Player.FindItemByLayer(GUO.Game.Data.Layer.Backpack);

        // Backpack: the top slice, the left stick up.
        Axis(JoyAxis.TriggerLeft, 1f);
        await InputProbe.Wait(host, 6);
        bool open = PadWheel.IsOpen;
        Axis(JoyAxis.LeftY, -1f);
        await InputProbe.Wait(host, 8);
        int focus = PadWheel.Focus;
        await Shot(host, "wheel_backpack");
        Axis(JoyAxis.TriggerLeft, 0f);
        await InputProbe.Wait(host, 2);
        Axis(JoyAxis.LeftY, 0f);
        bool opened = await Until(host, () => backpack != null && UIManager.GetGump<Gump>(backpack.Serial) != null, 120);
        Check("LT opens the wheel; the left stick up points at the backpack; letting go opens it",
            open && focus == 0 && PadWheel.LastResult == "opened Backpack" && opened,
            $"open {open}, focus {focus}, {PadWheel.LastResult}, gump {opened}");
        await Shot(host, "backpack_open");

        // Paperdoll: top right, with the right stick this time.
        Axis(JoyAxis.TriggerLeft, 1f);
        await InputProbe.Wait(host, 6);
        Axis(JoyAxis.RightX, 0.75f);
        Axis(JoyAxis.RightY, -0.75f);
        await InputProbe.Wait(host, 8);
        focus = PadWheel.Focus;
        await Shot(host, "wheel_paperdoll");
        Axis(JoyAxis.TriggerLeft, 0f);
        await InputProbe.Wait(host, 2);
        Axis(JoyAxis.RightX, 0f);
        Axis(JoyAxis.RightY, 0f);
        opened = await Until(host, () => UIManager.GetGump<PaperDollGump>(world.Player.Serial) != null, 180);
        Check("the right stick points too: top right opens the paperdoll", focus == 1 && opened, $"focus {focus}, {PadWheel.LastResult}");
        await Shot(host, "paperdoll_open");

        // A tap reopens the last window the wheel opened.
        UIManager.GetGump<PaperDollGump>(world.Player.Serial)?.Dispose();
        await InputProbe.Wait(host, 10);
        Axis(JoyAxis.TriggerLeft, 1f);
        await InputProbe.Wait(host, 3);
        Axis(JoyAxis.TriggerLeft, 0f);
        opened = await Until(host, () => UIManager.GetGump<PaperDollGump>(world.Player.Serial) != null, 180);
        Check("a quick tap of LT reopens the last window (the paperdoll)", PadWheel.LastResult == "reopened Paperdoll" && opened, PadWheel.LastResult);

        // Held, no stick, let go in the middle: nothing.
        int gumps = UIManager.Gumps.Count();
        Axis(JoyAxis.TriggerLeft, 1f);
        await InputProbe.Wait(host, 30);
        Axis(JoyAxis.TriggerLeft, 0f);
        await InputProbe.Wait(host, 30);
        Check("let go in the middle: nothing opens", PadWheel.LastResult == "nothing" && UIManager.Gumps.Count() == gumps,
            $"{PadWheel.LastResult}, gumps {gumps} -> {UIManager.Gumps.Count()}");

        UIManager.GetGump<PaperDollGump>(world.Player.Serial)?.Dispose();

        if (backpack != null)
        {
            UIManager.GetGump<Gump>(backpack.Serial)?.Dispose();
        }

        await InputProbe.Wait(host, 10);
    }

    // --- RT: the interact radar -------------------------------------------------------------------

    private static async System.Threading.Tasks.Task Radar(Node host, World world)
    {
        Mobile near = Nearest(world);

        if (near == null)
        {
            GD.Print("[GUO] pad wheels probe: no mobile within reach; asking the shard for a horse");
            await InputProbe.Say(host, "[add Horse");

            if (await Until(host, () => world.TargetManager.IsTargeting, 180))
            {
                world.TargetManager.Target(0, (ushort) (world.Player.X + 2), (ushort) (world.Player.Y + 1), world.Player.Z);
            }

            await Until(host, () => (near = Nearest(world)) != null, 240);
        }

        if (near == null)
        {
            Check("a mobile within the radar's reach", false);
            return;
        }

        Axis(JoyAxis.TriggerRight, 1f);
        await InputProbe.Wait(host, 10);
        Check("RT opens the radar, with what is near listed", PadRadar.IsOpen && PadRadar.Near.Contains(near),
            $"{PadRadar.Near.Count} near: {string.Join(", ", PadRadar.Near.Take(6).Select(PadRadar.NameOf))}");
        await Shot(host, "radar_open");

        // The right stick, pushed toward the mobile on screen.
        Vector2 dir = (RadarView.Foot(near) - RadarView.Foot(world.Player)).Normalized();
        Axis(JoyAxis.RightX, dir.X * 0.95f);
        Axis(JoyAxis.RightY, dir.Y * 0.95f);
        await InputProbe.Wait(host, 6);
        Axis(JoyAxis.RightX, 0f);
        Axis(JoyAxis.RightY, 0f);
        await InputProbe.Wait(host, 6);
        Entity chosen = PadRadar.Chosen;
        Check("the right stick snaps the choice to the thing that way", chosen == near || chosen is Mobile,
            $"pushed ({dir.X:0.00},{dir.Y:0.00}), chose {PadRadar.NameOf(chosen)} 0x{chosen?.Serial ?? 0:X8}, wanted {PadRadar.NameOf(near)} 0x{near.Serial:X8}");
        Check("the world highlights the choice as the mouse would", ReferenceEquals(SelectedObject.Object, chosen),
            $"selected {SelectedObject.Object?.GetType().Name}");
        await Shot(host, "radar_chosen");

        await Button(host, JoyButton.X);
        Check("X looks (a single click on the choice)", PadRadar.LastAction == $"look 0x{chosen?.Serial ?? 0:X8}", PadRadar.LastAction);
        await InputProbe.Wait(host, 30);
        await Shot(host, "radar_look");

        int at = PadRadar.Near.ToList().IndexOf(chosen);
        await Button(host, JoyButton.RightShoulder);
        int now = PadRadar.Near.ToList().IndexOf(PadRadar.Chosen);
        Check("RB steps to the next thing, nearest first", PadRadar.Near.Count == 1 ? now == 0 : now == (at + 1) % PadRadar.Near.Count,
            $"{at} -> {now} of {PadRadar.Near.Count} ({PadRadar.NameOf(PadRadar.Chosen)})");
        await Shot(host, "radar_next");
        await Button(host, JoyButton.LeftShoulder);
        Check("LB steps back", PadRadar.Chosen == chosen, PadRadar.NameOf(PadRadar.Chosen));

        await Button(host, JoyButton.A);
        Check("A uses the choice (a double click)", PadRadar.LastAction == $"use 0x{chosen?.Serial ?? 0:X8}", PadRadar.LastAction);
        await InputProbe.Wait(host, 20);

        await Button(host, JoyButton.Y);
        Check("Y asks for the choice's context menu", PadRadar.LastAction == $"menu 0x{chosen?.Serial ?? 0:X8}", PadRadar.LastAction);
        await InputProbe.Wait(host, 40);
        await Shot(host, "radar_menu");
        Godot.Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, Pressed = true });
        Godot.Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, Pressed = false });
        UIManager.GetGump<PopupMenuGump>()?.Dispose();

        string last = PadRadar.LastAction;
        Axis(JoyAxis.TriggerRight, 0f);
        await InputProbe.Wait(host, 6);
        Check("letting RT go after a button acted does nothing more", !PadRadar.IsOpen && PadRadar.LastAction == last, PadRadar.LastAction);

        // Let go with a choice and no button: Use.
        Axis(JoyAxis.TriggerRight, 1f);
        await InputProbe.Wait(host, 6);
        await Button(host, JoyButton.RightShoulder);
        Entity first = PadRadar.Chosen;
        Axis(JoyAxis.TriggerRight, 0f);
        await InputProbe.Wait(host, 6);
        Check("letting RT go with a choice and no button uses it", PadRadar.LastAction == $"use 0x{first?.Serial ?? 0:X8}", PadRadar.LastAction);

        // Nothing chosen: letting go does nothing.
        Axis(JoyAxis.TriggerRight, 1f);
        await InputProbe.Wait(host, 6);
        last = PadRadar.LastAction;
        Axis(JoyAxis.TriggerRight, 0f);
        await InputProbe.Wait(host, 6);
        Check("letting RT go with nothing chosen does nothing", PadRadar.LastAction == last, PadRadar.LastAction);

        // A target cursor: the radar picks the target; A sends it, B cancels.
        GameObject taken = null;
        world.TargetManager.SetTargeting(o => taken = o, 0, TargetType.Neutral);
        Axis(JoyAxis.TriggerRight, 1f);
        await InputProbe.Wait(host, 6);
        PadRadarChoose(near);
        await InputProbe.Wait(host, 4);
        await Shot(host, "radar_target");
        await Button(host, JoyButton.A);
        Check("with a target cursor up, A sends the target to the choice", ReferenceEquals(taken, near) && !world.TargetManager.IsTargeting,
            $"taken {(taken as Entity)?.Serial ?? 0:X8}, targeting {world.TargetManager.IsTargeting}");
        world.TargetManager.SetTargeting(CursorTarget.Object, 0, TargetType.Neutral);
        await InputProbe.Wait(host, 4);
        await Button(host, JoyButton.B);
        Check("with a target cursor up, B cancels it", !world.TargetManager.IsTargeting && PadRadar.IsOpen);
        Axis(JoyAxis.TriggerRight, 0f);
        await InputProbe.Wait(host, 6);
        world.TargetManager.CancelTarget();
    }

    /// <summary>Step with RB until <paramref name="e"/> is the choice.</summary>
    private static void PadRadarChoose(Entity e)
    {
        for (int i = 0; i < PadRadar.Near.Count + 1 && PadRadar.Chosen != e; i++)
        {
            PadRadar.Step(1);
        }
    }

    private static Mobile Nearest(World world) =>
        world.Mobiles.Values.Where(m => m != world.Player && !m.IsDestroyed && m.Distance <= Math.Min(6, PadBindings.RadarRange))
            .OrderBy(m => m.Distance).FirstOrDefault();

    // --- the new buttons ---------------------------------------------------------------------------

    private static async System.Threading.Tasks.Task Buttons(Node host, World world, Profile profile)
    {
        bool war = world.Player.InWarMode;
        await Button(host, JoyButton.RightStick);
        bool flipped = await Until(host, () => world.Player.InWarMode != war, 120);
        await Shot(host, "war_mode");
        Check("R3 toggles war mode", flipped, $"{war} -> {world.Player.InWarMode}");
        await Button(host, JoyButton.RightStick);
        await Until(host, () => world.Player.InWarMode == war, 120);

        bool run = profile.AlwaysRun;
        await Button(host, JoyButton.LeftStick);
        await InputProbe.Wait(host, 4);
        Check("L3 toggles always run", profile.AlwaysRun != run, $"{run} -> {profile.AlwaysRun}");
        await Button(host, JoyButton.LeftStick);

        await Button(host, JoyButton.Start);
        OptionsGump options = null;
        await Until(host, () => (options = UIManager.GetGump<OptionsGump>()) != null, 60);
        Check("Start opens the options", options != null);

        // The Options entry: Controller buttons, "Set controls...".
        Game.UI.Controls.NiceButton entry = options == null ? null : Find(options);
        Check("the options have a \"Set controls...\" entry", entry != null);

        if (entry != null)
        {
            entry.InvokeMouseUp(new Compat.Point(entry.ScreenCoordinateX + 4, entry.ScreenCoordinateY + 4), GUO.Input.MouseButtonType.Left);
            await InputProbe.Wait(host, 6);
            Check("it opens \"Set controls\" at the first job", PadWizard.IsOpen && PadWizard.Asking == PadCommand.Use,
                $"open {PadWizard.IsOpen}, asking {PadWizard.Asking}");
            await Shot(host, "wizard_from_options");
        }

        options?.Dispose();
    }

    private static Game.UI.Controls.NiceButton Find(Game.UI.Controls.Control root)
    {
        foreach (Game.UI.Controls.Control c in root.Children)
        {
            if (c is Game.UI.Controls.NiceButton b && b.TextLabel?.Text == "Set controls...")
            {
                return b;
            }

            if (Find(c) is Game.UI.Controls.NiceButton found)
            {
                return found;
            }
        }

        return null;
    }

    // --- "Set controls" -------------------------------------------------------------------------------

    private static async System.Threading.Tasks.Task Wizard(Node host, World world)
    {
        if (!PadWizard.IsOpen)
        {
            PadWizard.Open();
            await InputProbe.Wait(host, 4);
        }

        // Three at once: cancelled, nothing changed.
        string fileBefore = File.Exists(PadBindings.FilePath) ? File.ReadAllText(PadBindings.FilePath) : "";
        Down(JoyButton.A);
        Down(JoyButton.X);
        await InputProbe.Wait(host, 4);
        Down(JoyButton.Y);
        await InputProbe.Wait(host, 4);
        await Shot(host, "wizard_cancelled");
        Up(JoyButton.A);
        Up(JoyButton.X);
        Up(JoyButton.Y);
        await Until(host, () => !PadWizard.IsOpen, 120);
        string fileAfter = File.Exists(PadBindings.FilePath) ? File.ReadAllText(PadBindings.FilePath) : "";
        Check("holding three inputs at once cancels the wizard, nothing changed",
            PadWizard.LastResult == "cancelled" && !PadWizard.IsOpen && fileAfter == fileBefore
            && PadBindings.For(PadCommand.WarMode) == PadInput.Button(JoyButton.RightStick),
            $"{PadWizard.LastResult}, open {PadWizard.IsOpen}, file same {fileAfter == fileBefore}");
        bool warBefore = world.Player.InWarMode;
        await Button(host, JoyButton.A);   // the swallowed releases are gone; a fresh press is a click
        Check("the inputs held at the cancel do nothing as they come up", world.Player.InWarMode == warBefore);

        // The whole way through: War mode on Y, Macro row on R3 (a swap), the rest as they are.
        PadWizard.Open();
        await InputProbe.Wait(host, 4);

        foreach (PadCommand c in PadBindings.Order)
        {
            PadInput give = c switch
            {
                PadCommand.WarMode => PadInput.Button(JoyButton.Y),
                PadCommand.MacroRow => PadInput.Button(JoyButton.RightStick),
                _ => PadBindings.For(c) ?? PadInput.Button(JoyButton.Guide),
            };

            Press(give, true);

            if (c == PadCommand.Use)
            {
                await InputProbe.Wait(host, 20);
                await Shot(host, "wizard_meter");
            }

            bool moved = await Until(host, () => PadWizard.Asking != c, 150);
            Press(give, false);
            await InputProbe.Wait(host, 3);

            if (!moved)
            {
                Check($"the wizard takes {give.Label} for {PadBindings.Name(c)}", false, $"still asking {PadWizard.Asking}, progress {PadWizard.Progress:0.00}");
                PadWizard.Cancel();
                return;
            }
        }

        Check("one held input per job, twelve jobs, then the wheel's slices", PadWizard.SlotAsking == 0, $"slot {PadWizard.SlotAsking}");

        // Slices: keep each, but the left one (Macros) becomes Status (two down).
        for (int slot = 0; slot < 8; slot++)
        {
            if (slot == 6)
            {
                await Button(host, JoyButton.DpadDown, 6);
                await Button(host, JoyButton.DpadDown, 6);
                await Shot(host, "wizard_slots");
            }

            await Button(host, JoyButton.A, 6);
        }

        await InputProbe.Wait(host, 4);
        await Shot(host, "wizard_done");
        await Until(host, () => !PadWizard.IsOpen, 120);
        string file = File.Exists(PadBindings.FilePath) ? File.ReadAllText(PadBindings.FilePath) : "";
        Check("the choices are applied together and saved",
            PadWizard.LastResult == "applied" && PadBindings.For(PadCommand.WarMode) == PadInput.Button(JoyButton.Y)
            && PadBindings.For(PadCommand.MacroRow) == PadInput.Button(JoyButton.RightStick)
            && PadBindings.Wheel[6] == WheelWindow.Status && file.Contains("\"WarMode\": \"b3\"") && file.Contains("\"Status\""),
            $"{PadWizard.LastResult}, war {PadBindings.For(PadCommand.WarMode)?.Label}, macro row {PadBindings.For(PadCommand.MacroRow)?.Label}, slice 7 {PadBindings.Wheel[6]}");

        // The saved map takes effect: Y is war mode now, R3 the macro row.
        PadBindings.Load();
        bool war = world.Player.InWarMode;
        await Button(host, JoyButton.Y);
        bool yWar = await Until(host, () => world.Player.InWarMode != war, 120);
        Check("the rebound Y toggles war mode (read back from the file)", yWar, $"{war} -> {world.Player.InWarMode}");
        war = world.Player.InWarMode;
        await Button(host, JoyButton.RightStick);
        await InputProbe.Wait(host, 60);
        Check("R3 no longer toggles war mode; it is the macro row now", world.Player.InWarMode == war
            && (UIManager.GetGump<OptionsGump>() != null || (GUO.Input.Touch.TouchInput.Bar?.HandleShown ?? false)),
            $"war {war} -> {world.Player.InWarMode}, options {(UIManager.GetGump<OptionsGump>() != null)}");
        await Shot(host, "rebound_macro_row");
        UIManager.GetGump<OptionsGump>()?.Dispose();

        await Button(host, JoyButton.Y);
        await Until(host, () => !world.Player.InWarMode, 120);

        // The new slice: the left one opens the status bar.
        Axis(JoyAxis.TriggerLeft, 1f);
        await InputProbe.Wait(host, 6);
        Axis(JoyAxis.LeftX, -1f);
        await InputProbe.Wait(host, 8);
        await Shot(host, "wheel_rebound");
        Axis(JoyAxis.TriggerLeft, 0f);
        await InputProbe.Wait(host, 2);
        Axis(JoyAxis.LeftX, 0f);
        await InputProbe.Wait(host, 20);
        Check("the wheel's changed slice opens its new window", PadWheel.LastResult == "opened Status", PadWheel.LastResult);

        PadBindings.ResetToDefaults();
    }

    // --- the Steam Deck trackpad: a mouse beside the pad ----------------------------------------------

    private static async System.Threading.Tasks.Task Trackpad(Node host, World world)
    {
        InputMode.PadBesideMouse = true;
        await Button(host, JoyButton.DpadLeft, 8);
        bool pad = InputMode.Current == InputKind.Gamepad;
        Vector2 at = new(GUO.Input.Mouse.Position.X, GUO.Input.Mouse.Position.Y);
        Godot.Input.ParseInputEvent(new InputEventMouseMotion { Position = at + new Vector2(-60, -30), Relative = new Vector2(-60, -30) });
        await InputProbe.Wait(host, 3);
        Vector2 moved = new(GUO.Input.Mouse.Position.X, GUO.Input.Mouse.Position.Y);
        bool still = InputMode.Current == InputKind.Gamepad && !InputMode.PointerHidden;
        Godot.Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = moved });
        await InputProbe.Wait(host, 2);
        Godot.Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = moved });
        await InputProbe.Wait(host, 3);
        Check("on a Deck the trackpad moves and clicks the pointer and the mode stays Gamepad",
            pad && still && InputMode.Current == InputKind.Gamepad && moved != at, $"{at} -> {moved}, {InputMode.Current}");

        // The right stick goes on from where the trackpad left the pointer.
        Axis(JoyAxis.RightX, 0.8f);
        await InputProbe.Wait(host, 6);
        Axis(JoyAxis.RightX, 0f);
        await InputProbe.Wait(host, 2);
        Vector2 stick = new(GUO.Input.Mouse.Position.X, GUO.Input.Mouse.Position.Y);
        Check("the right stick moves the same pointer on from there", stick.X > moved.X && Math.Abs(stick.Y - moved.Y) < 2, $"{moved} -> {stick}");

        // The trackpad while LT is held does not steer the wheel.
        Axis(JoyAxis.TriggerLeft, 1f);
        await InputProbe.Wait(host, 4);
        Axis(JoyAxis.LeftX, 0.9f);
        await InputProbe.Wait(host, 6);
        int focus = PadWheel.Focus;

        for (int i = 0; i < 6; i++)
        {
            Godot.Input.ParseInputEvent(new InputEventMouseMotion { Position = stick + new Vector2(-20 * i, -10 * i), Relative = new Vector2(-20, -10) });
            await InputProbe.Wait(host, 2);
        }

        bool kept = PadWheel.IsOpen && PadWheel.Focus == focus && InputMode.Current == InputKind.Gamepad;
        Axis(JoyAxis.TriggerLeft, 0f);
        await InputProbe.Wait(host, 2);
        Axis(JoyAxis.LeftX, 0f);
        await InputProbe.Wait(host, 20);
        Check("the trackpad moving while LT is held leaves the wheel's slice alone", kept && PadWheel.LastResult == "opened Journal",
            $"focus {focus} -> {PadWheel.Focus}, {PadWheel.LastResult}");
        UIManager.GetGump<JournalGump>()?.Dispose();
        UIManager.GetGump<ResizableJournal>()?.Dispose();

        // ...nor the radar's choice.
        Axis(JoyAxis.TriggerRight, 1f);
        await InputProbe.Wait(host, 6);
        await Button(host, JoyButton.RightShoulder);
        Entity chosen = PadRadar.Chosen;

        for (int i = 0; i < 6; i++)
        {
            Godot.Input.ParseInputEvent(new InputEventMouseMotion { Position = stick + new Vector2(-15 * i, 10 * i), Relative = new Vector2(-15, 10) });
            await InputProbe.Wait(host, 2);
        }

        Check("the trackpad moving while RT is held leaves the radar's choice alone",
            PadRadar.IsOpen && chosen != null && PadRadar.Chosen == chosen && ReferenceEquals(PadRadar.Highlight, chosen), PadRadar.NameOf(PadRadar.Chosen));
        await Button(host, JoyButton.B);
        Axis(JoyAxis.TriggerRight, 0f);
        await InputProbe.Wait(host, 4);

        // A real key still switches to the keyboard.
        Godot.Input.ParseInputEvent(new InputEventKey { Keycode = Key.Shift, PhysicalKeycode = Key.Shift, Pressed = true });
        Godot.Input.ParseInputEvent(new InputEventKey { Keycode = Key.Shift, PhysicalKeycode = Key.Shift, Pressed = false });
        await InputProbe.Wait(host, 2);
        Check("a key still switches to KeyboardMouse", InputMode.Current == InputKind.KeyboardMouse);

        // Off a Deck (no Steam Input), a moved mouse switches back, as before (ADR-0025).
        InputMode.PadBesideMouse = false;
        await Button(host, JoyButton.DpadLeft, 8);
        Godot.Input.ParseInputEvent(new InputEventMouseMotion { Position = stick, Relative = new Vector2(12, 0) });
        await InputProbe.Wait(host, 2);
        Check("elsewhere a moved mouse still switches to KeyboardMouse", InputMode.Current == InputKind.KeyboardMouse);
    }

    // --- injected input ---------------------------------------------------------------------------------

    private static void Axis(JoyAxis axis, float value) =>
        Godot.Input.ParseInputEvent(new InputEventJoypadMotion { Axis = axis, AxisValue = value });

    private static void Down(JoyButton b) => Godot.Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = b, Pressed = true });

    private static void Up(JoyButton b) => Godot.Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = b, Pressed = false });

    private static void Press(PadInput input, bool down)
    {
        if (input.IsAxis)
        {
            Axis((JoyAxis) input.Index, down ? input.Sign * 1f : 0f);
        }
        else if (down)
        {
            Down((JoyButton) input.Index);
        }
        else
        {
            Up((JoyButton) input.Index);
        }
    }

    private static async System.Threading.Tasks.Task Button(Node host, JoyButton button, int after = 20)
    {
        Down(button);
        await InputProbe.Wait(host, 2);
        Up(button);
        await InputProbe.Wait(host, after);
    }

    private static async System.Threading.Tasks.Task<bool> Until(Node host, Func<bool> done, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            if (done())
            {
                return true;
            }

            await InputProbe.Wait(host, 1);
        }

        return done();
    }
}
