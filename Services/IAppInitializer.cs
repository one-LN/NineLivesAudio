namespace NineLivesAudio.Services;

/// <summary>
/// Handles async app initialization without blocking the UI thread.
/// </summary>
public interface IAppInitializer
{
    /// <summary>Current initialization state.</summary>
    InitState State { get; }

    /// <summary>Error message if initialization failed.</summary>
    string? ErrorMessage { get; }

    /// <summary>Fires when State changes.</summary>
    event EventHandler<InitState>? StateChanged;

    /// <summary>
    /// Run all startup work (DB, settings, token validation, sync start).
    /// Safe to call multiple times — only the first call does work.
    /// </summary>
    Task InitializeAsync();
}

public enum InitState
{
    NotStarted,
    Initializing,
    Ready,
    Failed
}
