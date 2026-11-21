using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace NineLivesAudio.Helpers;

/// <summary>
/// Centralizes the pointer press/move/release "tap vs scroll" discrimination
/// pattern used across LibraryPage and HomePage. Tracks whether the pointer moved
/// beyond a threshold and stores the tapped item for retrieval on release.
/// </summary>
public class TapHelper
{
    private Windows.Foundation.Point? _pressedPosition;
    private object? _pressedTag;
    private const double TapDistanceThreshold = 12.0;

    /// <summary>
    /// Call from PointerPressed. Records the position and tag if left-clicked.
    /// Returns true if this was a left-button press (tap candidate).
    /// </summary>
    public bool OnPointerPressed(PointerRoutedEventArgs e, FrameworkElement element, object tag)
    {
        var point = e.GetCurrentPoint(element);
        if (point.Properties.IsLeftButtonPressed)
        {
            _pressedPosition = point.Position;
            _pressedTag = tag;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Call from PointerMoved. Cancels the tap if the pointer moved too far.
    /// </summary>
    public void OnPointerMoved(PointerRoutedEventArgs e, FrameworkElement element)
    {
        if (_pressedPosition == null) return;

        var point = e.GetCurrentPoint(element);
        var dx = point.Position.X - _pressedPosition.Value.X;
        var dy = point.Position.Y - _pressedPosition.Value.Y;
        if (Math.Sqrt(dx * dx + dy * dy) > TapDistanceThreshold)
        {
            Reset();
        }
    }

    /// <summary>
    /// Call from PointerReleased. Returns the tag if this was a valid tap, otherwise null.
    /// Always resets state.
    /// </summary>
    public T? OnPointerReleased<T>() where T : class
    {
        var result = _pressedPosition != null ? _pressedTag as T : null;
        Reset();
        return result;
    }

    /// <summary>
    /// Returns true if a press is currently tracked (not yet cancelled by movement).
    /// </summary>
    public bool IsTracking => _pressedPosition != null;

    private void Reset()
    {
        _pressedPosition = null;
        _pressedTag = null;
    }
}
