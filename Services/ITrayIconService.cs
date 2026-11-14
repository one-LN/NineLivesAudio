using Microsoft.UI.Xaml;

namespace NineLivesAudio.Services;

/// <summary>
/// A single notification-area icon for the main window. The app has to hide itself to
/// the tray when the user minimizes (setting <c>MinimizeToTray</c>), and without an icon
/// there would be no way back.
/// </summary>
public interface ITrayIconService : IDisposable
{
    /// <summary>Hooks the window so minimize-to-tray is intercepted.</summary>
    void Attach(Window window);

    /// <summary>Adds the icon to the notification area (idempotent).</summary>
    void Show();

    /// <summary>Removes the icon from the notification area (idempotent).</summary>
    void Hide();

    bool IsShown { get; }
}
