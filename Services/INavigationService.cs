using Microsoft.UI.Xaml.Controls;

namespace NineLivesAudio.Services;

public interface INavigationService
{
    bool CanGoBack { get; }
    void Initialize(Frame frame, NavigationView navView);
    void NavigateTo(Type pageType, object? parameter = null);
    void GoBack();
    void GoHome();
}
