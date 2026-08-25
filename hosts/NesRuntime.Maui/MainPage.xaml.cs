using NesRuntime.Core.Input;
using NesRuntime.Maui.Controls;
using NesRuntime.Maui.Games;
using NesRuntime.Maui.Hosting;
using NesRuntime.Rendering;
using NesRuntime.Sdk.Game;

namespace NesRuntime.Maui;

/// <summary>
/// The host shell: a <see cref="NesGameView"/> screen, a status line, and on-screen controls.
/// "Start" constructs the four backends, wires them to a game through <see cref="GameRunner"/>,
/// and runs the frame loop. Because Core/SDK are still stubs, starting surfaces the
/// <see cref="NotImplementedException"/> as a status message — proving the host is fully wired
/// without crashing.
/// </summary>
public partial class MainPage : ContentPage
{
    // NTSC frame rate (~60.10 Hz).
    private const double NtscFps = 60.0988;

    private readonly NesGameView _screen = new();
    private readonly MauiInputBackend _input = new();

    private DispatcherFrameClock? _clock;
    private RuntimeHost? _host;
    private bool _running;
    private IDispatcherTimer? _statusTimer;

    public MainPage()
    {
        InitializeComponent();
        ScreenHost.Content = _screen;
        ControlsHost.Content = BuildControls();

        UpdateControllerStatus();
        _statusTimer = Dispatcher.CreateTimer();
        _statusTimer.Interval = TimeSpan.FromMilliseconds(150); // fast enough to watch button presses live
        _statusTimer.IsRepeating = true;
        _statusTimer.Tick += (_, _) => UpdateControllerStatus();
        _statusTimer.Start();
    }

    private void UpdateControllerStatus()
    {
        int n = _input.ConnectedControllers;
        if (n == 0)
        {
            ControllerStatusLabel.Text = "No controller — use the on-screen buttons or keyboard";
            return;
        }

        // Show the raw controller inputs live so a mismatch is visible when you press buttons.
        string diag = _input.DiagnosticLine();
        ControllerStatusLabel.Text = $"🎮 {n} connected  |  {diag}";
    }

    private void ToggleRun()
    {
        if (_running)
        {
            _clock?.Stop();
            _running = false;
            StatusLabel.Text = "Stopped.";
            return;
        }

        // The sample game. (Swap for HelloNesGame; use "Load ROM…" to run a real cartridge.)
        StartHost((v, a, i, c) => GameRunner.Create<BreakoutGame>(v, a, i, c, NesRuntime.Core.Region.Ntsc), "Running.");
    }

    // Builds fresh backends + clock, runs the game the factory creates, and reports status.
    private bool StartHost(
        Func<IVideoBackend, IAudioBackend, IInputBackend, IFrameClock, RuntimeHost> create, string runningText)
    {
        if (_running)
        {
            _clock?.Stop();
            _running = false;
        }

        try
        {
            var video = new MauiVideoBackend(_screen);
            var audio = new MauiAudioBackend();
            _clock = new DispatcherFrameClock(Dispatcher, NtscFps);
            _host = create(video, audio, _input, _clock);
            _host.Run();
            _running = true;
            StatusLabel.Text = runningText;
            return true;
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Error: {ex.Message}";
            return false;
        }
    }

    // Picks an iNES/NES 2.0 file, validates it, and runs it through the emulator.
    private async void OnLoadRom()
    {
        try
        {
            FileResult? file = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Select an iNES / NES 2.0 ROM (.nes)",
            });
            if (file is null)
                return; // cancelled

            using Stream stream = await file.OpenReadAsync();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            byte[] rom = buffer.ToArray();

            if (rom.Length < 16 || rom[0] != 'N' || rom[1] != 'E' || rom[2] != 'S' || rom[3] != 0x1A)
            {
                StatusLabel.Text = $"Not an iNES ROM: {file.FileName}";
                return;
            }

            EmulatorGame.RomImage = rom;
            StartHost(
                (v, a, i, c) => GameRunner.Create<EmulatorGame>(v, a, i, c, NesRuntime.Core.Region.Ntsc),
                $"Running {file.FileName}");
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Load failed: {ex.Message}";
        }
    }

    private View BuildControls()
    {
        var startButton = new Button { Text = "Start / Stop" };
        startButton.Clicked += (_, _) => ToggleRun();

        var loadButton = new Button { Text = "Load ROM…" };
        loadButton.Clicked += (_, _) => OnLoadRom();

        var topRow = new HorizontalStackLayout
        {
            Spacing = 12,
            HorizontalOptions = LayoutOptions.Center,
            Children = { startButton, loadButton },
        };

        var dpad = new Grid
        {
            RowDefinitions = { new(), new(), new() },
            ColumnDefinitions = { new(), new(), new() },
            ColumnSpacing = 4,
            RowSpacing = 4,
        };
        AddHoldButton(dpad, "▲", NesButtons.Up, row: 0, col: 1);
        AddHoldButton(dpad, "◀", NesButtons.Left, row: 1, col: 0);
        AddHoldButton(dpad, "▶", NesButtons.Right, row: 1, col: 2);
        AddHoldButton(dpad, "▼", NesButtons.Down, row: 2, col: 1);

        var actions = new HorizontalStackLayout { Spacing = 8, VerticalOptions = LayoutOptions.Center };
        AddHoldButton(actions, "SELECT", NesButtons.Select);
        AddHoldButton(actions, "START", NesButtons.Start);
        AddHoldButton(actions, "B", NesButtons.B);
        AddHoldButton(actions, "A", NesButtons.A);

        var pad = new HorizontalStackLayout
        {
            Spacing = 24,
            HorizontalOptions = LayoutOptions.Center,
            Children = { dpad, actions },
        };

        return new VerticalStackLayout { Spacing = 12, Children = { topRow, pad } };
    }

    private void AddHoldButton(Layout parent, string text, NesButtons button, int row = -1, int col = -1)
    {
        var b = new Button { Text = text, WidthRequest = 56, HeightRequest = 44, Padding = 0 };
        b.Pressed += (_, _) => _input.Press(0, button);
        b.Released += (_, _) => _input.Release(0, button);

        if (parent is Grid grid && row >= 0)
        {
            grid.Add(b, col, row);
        }
        else
        {
            parent.Children.Add(b);
        }
    }
}
