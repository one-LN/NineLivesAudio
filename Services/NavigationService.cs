using System;
using System.Linq;
using NineLivesAudio.Views;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;

namespace NineLivesAudio.Services;

public class NavigationService : INavigationService
{
    private Frame? _frame;
    private NavigationView? _navView;

    public bool CanGoBack => _frame?.CanGoBack ?? false;

    public void Initialize(Frame frame, NavigationView navView)
    {
        _frame = frame;
        _navView = navView;

        // The frame can change without going through the rail — the back arrow on the player,
        // Frame.GoBack() from a detail page. Without this the content showed the library while
        // the rail still highlighted "Now Playing".
        _frame.Navigated -= OnFrameNavigated;
        _frame.Navigated += OnFrameNavigated;
    }

    private void OnFrameNavigated(object sender, NavigationEventArgs e)
    {
        if (_navView == null) return;

        var tag = TagFor(e.SourcePageType);
        if (tag == null)
        {
            // Detail pages are not on the rail; leave whatever is highlighted.
            return;
        }

        if (tag == "Settings")
        {
            _navView.SelectedItem = _navView.SettingsItem;
            return;
        }

        foreach (var item in _navView.MenuItems.OfType<NavigationViewItem>())
        {
            if (string.Equals(item.Tag as string, tag, StringComparison.Ordinal))
            {
                _navView.SelectedItem = item;
                return;
            }
        }
    }

    private static string? TagFor(Type? pageType) => pageType switch
    {
        var t when t == typeof(HomePage) => "Home",
        var t when t == typeof(LibraryPage) => "Library",
        var t when t == typeof(PlayerPage) => "Player",
        var t when t == typeof(DownloadsPage) => "Downloads",
        var t when t == typeof(StatsPage) => "Stats",
        var t when t == typeof(SettingsPage) => "Settings",
        _ => null
    };

    public void NavigateTo(Type pageType, object? parameter = null)
    {
        if (_frame == null) return;
        if (_frame.CurrentSourcePageType == pageType) return;

        _frame.Navigate(pageType, parameter, new SlideNavigationTransitionInfo
        {
            Effect = SlideNavigationTransitionEffect.FromRight
        });
    }

    public void GoBack()
    {
        if (_frame?.CanGoBack == true)
            _frame.GoBack();
    }

    public void GoHome()
    {
        if (_frame == null) return;
        NavigateTo(typeof(HomePage));

        // Sync nav selection
        if (_navView?.MenuItems.Count > 0)
            _navView.SelectedItem = _navView.MenuItems[0];
    }
}
