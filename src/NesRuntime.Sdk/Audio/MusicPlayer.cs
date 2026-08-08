namespace NesRuntime.Sdk.Audio;

/// <summary>
/// Plays background music while the game runs, sequencing notes across the channels each frame. A
/// track can come from high-level authored data or a native NSF import. Sound effects transiently
/// duck the relevant channel and the music resumes underneath.
/// </summary>
public interface IMusicPlayer
{
    MusicTrack? Current { get; }
    bool IsPlaying { get; }

    void Play(MusicTrack track, bool loop = true);
    void Pause();
    void Resume();
    void Stop();
}

/// <summary>
/// A song. Either authored through the SDK (patterns of notes per channel) or loaded from an NSF
/// file; in both cases it drives the five NES channels within their real capabilities.
/// </summary>
public sealed class MusicTrack
{
    public string Title { get; }

    /// <summary>Playback tempo in frames per row/step.</summary>
    public int FramesPerRow { get; }

    private MusicTrack(string title, int framesPerRow) => (Title, FramesPerRow) = (title, framesPerRow);
}
