using NesRuntime.Core.Input;
using NesRuntime.Rendering;
#if MACCATALYST
using GameController;
#elif WINDOWS
using Windows.Gaming.Input;
#endif

namespace NesRuntime.Maui.Hosting;

/// <summary>
/// Supplies controller state to the runtime. Sources are merged (OR'd) each frame: the host's
/// on-screen buttons (<see cref="Press"/>/<see cref="Release"/>), any connected physical game
/// controllers (Apple GameController on Mac Catalyst, Windows.Gaming.Input on Windows), and the
/// keyboard for player 1. Supports up to four players; physical controller <c>N</c> maps to
/// player <c>N</c>.
/// </summary>
public sealed class MauiInputBackend : IInputBackend
{
    private readonly NesButtons[] _state = new NesButtons[4];

    /// <summary>Marks a button as held for a player (0-based). Used by the on-screen pad.</summary>
    public void Press(int playerIndex, NesButtons button) => _state[playerIndex] |= button;

    /// <summary>Marks a button as released for a player (0-based).</summary>
    public void Release(int playerIndex, NesButtons button) => _state[playerIndex] &= ~button;

    /// <summary>Number of physical controllers currently connected (for the status indicator).</summary>
    public int ConnectedControllers
    {
        get
        {
#if MACCATALYST
            return GCController.Controllers.Length;
#elif WINDOWS
            return Gamepad.Gamepads.Count;
#else
            return 0;
#endif
        }
    }

    public NesButtons PollButtons(int playerIndex)
    {
        if ((uint)playerIndex >= (uint)_state.Length)
            return NesButtons.None;

        NesButtons buttons = _state[playerIndex]; // on-screen buttons
#if MACCATALYST || WINDOWS
        buttons |= ReadGameController(playerIndex);
        if (playerIndex == 0)
            buttons |= ReadKeyboard();
#endif
        return buttons;
    }

    public bool TryPollPointer(int playerIndex, out int x, out int y, out bool trigger)
    {
        // No light-gun/pointer device wired in this host yet.
        x = y = 0;
        trigger = false;
        return false;
    }

    /// <summary>
    /// A human-readable snapshot of controller 0's raw inputs, for diagnosing mapping issues. Shows
    /// the vendor, which profile it exposes, and which physical inputs are currently active. Never
    /// throws — a status readout must not be able to crash the app.
    /// </summary>
    public string DiagnosticLine()
    {
#if MACCATALYST
        try
        {
            GCController[] cs = GCController.Controllers;
            if (cs.Length == 0)
                return string.Empty;

            GCController c = cs[0];
            string vendor = c.VendorName ?? "controller";
            static string B(GCControllerButtonInput? b) => (b?.IsPressed ?? false) ? "1" : "0";
            static string Ax(float v) => v.ToString("+0.0;-0.0");

            GCExtendedGamepad? g = c.ExtendedGamepad;
            if (g is not null)
            {
                return $"{vendor} [ext]  A{B(g.ButtonA)} B{B(g.ButtonB)} X{B(g.ButtonX)} Y{B(g.ButtonY)} "
                     + $"Menu{B(g.ButtonMenu)} Opt{B(g.ButtonOptions)}  "
                     + $"Dpad {B(g.DPad.Up)}{B(g.DPad.Down)}{B(g.DPad.Left)}{B(g.DPad.Right)} "
                     + $"Lstick ({Ax(g.LeftThumbstick.XAxis.Value)},{Ax(g.LeftThumbstick.YAxis.Value)})";
            }

#pragma warning disable CS0618, CA1422 // legacy GameController profile (older/generic USB pads)
            GCGamepad? leg = c.Gamepad;
            if (leg is not null)
                return $"{vendor} [legacy]  A{B(leg.ButtonA)} B{B(leg.ButtonB)} X{B(leg.ButtonX)} Y{B(leg.ButtonY)}  "
                     + $"Dpad {B(leg.DPad.Up)}{B(leg.DPad.Down)}{B(leg.DPad.Left)}{B(leg.DPad.Right)} "
                     + $"({Ax(leg.DPad.XAxis.Value)},{Ax(leg.DPad.YAxis.Value)})";
#pragma warning restore CS0618, CA1422

            return $"{vendor}: no readable profile";
        }
        catch (Exception ex)
        {
            return $"controller read error: {ex.GetType().Name}";
        }
#elif WINDOWS
        var pads = Gamepad.Gamepads;
        if (pads.Count == 0)
            return string.Empty;

        GamepadReading r = pads[0].GetCurrentReading();
        GamepadButtons b = r.Buttons;
        static string F(GamepadButtons all, GamepadButtons f) => (all & f) != 0 ? "1" : "0";
        static string Ax(double v) => v.ToString("+0.0;-0.0");
        return $"gamepad  A{F(b, GamepadButtons.A)} B{F(b, GamepadButtons.B)} X{F(b, GamepadButtons.X)} Y{F(b, GamepadButtons.Y)} "
             + $"Menu{F(b, GamepadButtons.Menu)} View{F(b, GamepadButtons.View)}  "
             + $"Dpad {F(b, GamepadButtons.DPadUp)}{F(b, GamepadButtons.DPadDown)}{F(b, GamepadButtons.DPadLeft)}{F(b, GamepadButtons.DPadRight)}  "
             + $"Lstick ({Ax(r.LeftThumbstickX)},{Ax(r.LeftThumbstickY)})";
#else
        return string.Empty;
#endif
    }

#if MACCATALYST
    private static NesButtons ReadGameController(int playerIndex)
    {
        try
        {
            GCController[] controllers = GCController.Controllers;
            if (playerIndex >= controllers.Length)
                return NesButtons.None;

            GCController controller = controllers[playerIndex];
            GCExtendedGamepad? gp = controller.ExtendedGamepad;
            if (gp is not null)
            {
                // Modern profile: A/B faces, Options/Menu = Select/Start, the discrete D-pad, and
                // the left stick (which rests at 0). The D-pad's own analog axes are deliberately
                // NOT read — some pads rest them off-center, which reads as a stuck direction.
                (bool su, bool sd, bool sl, bool sr) = GamepadMapping.StickToDirections(
                    gp.LeftThumbstick.XAxis.Value, gp.LeftThumbstick.YAxis.Value);

                return GamepadMapping.FromButtons(
                    a: gp.ButtonA.IsPressed,
                    b: gp.ButtonB.IsPressed,
                    select: gp.ButtonOptions?.IsPressed ?? false,
                    start: gp.ButtonMenu.IsPressed,
                    up: gp.DPad.Up.IsPressed || su,
                    down: gp.DPad.Down.IsPressed || sd,
                    left: gp.DPad.Left.IsPressed || sl,
                    right: gp.DPad.Right.IsPressed || sr);
            }

            return ReadLegacyGamepad(controller);
        }
        catch
        {
            return NesButtons.None;
        }
    }

    // Older/generic USB pads expose only the deprecated GCGamepad profile (no Menu/Options/sticks):
    // A/B faces, X/Y stand in for Select/Start, and the D-pad.
#pragma warning disable CS0618, CA1422
    private static NesButtons ReadLegacyGamepad(GCController controller)
    {
        GCGamepad? gp = controller.Gamepad;
        if (gp is null)
            return NesButtons.None;

        // Discrete D-pad only — the analog D-pad axes are not read (they can rest off-center on
        // these pads, which reads as a permanently held direction).
        return GamepadMapping.FromButtons(
            a: gp.ButtonA.IsPressed,
            b: gp.ButtonB.IsPressed,
            select: gp.ButtonX.IsPressed,
            start: gp.ButtonY.IsPressed,
            up: gp.DPad.Up.IsPressed,
            down: gp.DPad.Down.IsPressed,
            left: gp.DPad.Left.IsPressed,
            right: gp.DPad.Right.IsPressed);
    }
#pragma warning restore CS0618, CA1422

    // Arrows -> D-pad, Z -> B, X -> A, Enter -> Start, Right Shift -> Select.
    private static NesButtons ReadKeyboard()
    {
        GCKeyboardInput? kb = GCKeyboard.CoalescedKeyboard?.KeyboardInput;
        if (kb is null)
            return NesButtons.None;

        bool Pressed(nint code) => kb.GetButton(code)?.IsPressed ?? false;

        return GamepadMapping.FromButtons(
            a: Pressed(GCKeyCode.KeyX),
            b: Pressed(GCKeyCode.KeyZ),
            select: Pressed(GCKeyCode.RightShift),
            start: Pressed(GCKeyCode.ReturnOrEnter),
            up: Pressed(GCKeyCode.UpArrow),
            down: Pressed(GCKeyCode.DownArrow),
            left: Pressed(GCKeyCode.LeftArrow),
            right: Pressed(GCKeyCode.RightArrow));
    }
#elif WINDOWS
    // NES A/B <- face buttons A/B; Select/Start <- View/Menu; D-pad <- dpad + left stick.
    private static NesButtons ReadGameController(int playerIndex)
    {
        var pads = Gamepad.Gamepads;
        if (playerIndex >= pads.Count)
            return NesButtons.None;

        GamepadReading r = pads[playerIndex].GetCurrentReading();
        GamepadButtons b = r.Buttons;
        bool Down(GamepadButtons f) => (b & f) != 0;

        (bool su, bool sd, bool sl, bool sr) = GamepadMapping.StickToDirections(
            (float)r.LeftThumbstickX, (float)r.LeftThumbstickY);

        return GamepadMapping.FromButtons(
            a: Down(GamepadButtons.A),
            b: Down(GamepadButtons.B),
            select: Down(GamepadButtons.View),
            start: Down(GamepadButtons.Menu),
            up: Down(GamepadButtons.DPadUp) || su,
            down: Down(GamepadButtons.DPadDown) || sd,
            left: Down(GamepadButtons.DPadLeft) || sl,
            right: Down(GamepadButtons.DPadRight) || sr);
    }

    // Arrows -> D-pad, Z -> B, X -> A, Enter -> Start, Right Shift -> Select.
    private static NesButtons ReadKeyboard()
    {
        static bool Down(Windows.System.VirtualKey key)
            => (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key)
                & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;

        return GamepadMapping.FromButtons(
            a: Down(Windows.System.VirtualKey.X),
            b: Down(Windows.System.VirtualKey.Z),
            select: Down(Windows.System.VirtualKey.RightShift),
            start: Down(Windows.System.VirtualKey.Enter),
            up: Down(Windows.System.VirtualKey.Up),
            down: Down(Windows.System.VirtualKey.Down),
            left: Down(Windows.System.VirtualKey.Left),
            right: Down(Windows.System.VirtualKey.Right));
    }
#endif
}
