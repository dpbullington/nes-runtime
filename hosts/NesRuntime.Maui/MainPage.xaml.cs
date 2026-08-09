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

    public MainPage()
    {
        InitializeComponent();
        ScreenHost.Content = _screen;
        ControlsHost.Content = BuildControls();
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

        try
        {
            var video = new MauiVideoBackend(_screen);
            var audio = new MauiAudioBackend();
            _clock = new DispatcherFrameClock(Dispatcher, NtscFps);
            // Swap the type argument to run a different game (e.g. HelloNesGame).
            _host = GameRunner.Create<BreakoutGame>(video, audio, _input, _clock, NesRuntime.Core.Region.Ntsc);
            _host.Run();
            _running = true;
            StatusLabel.Text = "Running.";
        }
        catch (NotImplementedException)
        {
            StatusLabel.Text = "Host wired OK — Core/SDK runtime is still stubbed (NotImplementedException).";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Error: {ex.Message}";
        }
    }

    private View BuildControls()
    {
        var startButton = new Button { Text = "Start / Stop", HorizontalOptions = LayoutOptions.Center };
        startButton.Clicked += (_, _) => ToggleRun();

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

        return new VerticalStackLayout { Spacing = 12, Children = { startButton, pad } };
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
