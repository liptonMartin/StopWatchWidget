using System.ComponentModel;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using StopwatchWidget.ViewModels;
using Windows.Graphics;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace StopwatchWidget;

/// <summary>
/// The application window. It hosts the stopwatch UI directly and binds it to a
/// <see cref="StopwatchViewModel"/> supplied as the content's DataContext.
/// </summary>
public sealed partial class MainWindow : Window
{
    /// <summary>
    /// The view model backing this window's UI.
    /// </summary>
    public StopwatchViewModel ViewModel { get; }

    public MainWindow()
    {
        ViewModel = new StopwatchViewModel();

        InitializeComponent();

        // Let the content fill the whole window, including the title bar area,
        // then use the top strip as the drag region.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");

        // A compact size that suits a widget. Adjust or remove as you like.
        AppWindow.Resize(new SizeInt32(360, 250));

        // Window is not a FrameworkElement and has no DataContext of its own,
        // so the bindings resolve against the root content element instead.
        RootLayout.DataContext = ViewModel;

        // Keep the real window in sync with the toggle.
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ApplyAlwaysOnTop();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StopwatchViewModel.IsAlwaysOnTop))
        {
            ApplyAlwaysOnTop();
        }
    }

    /// <summary>
    /// Pushes <see cref="StopwatchViewModel.IsAlwaysOnTop"/> to the window presenter.
    /// </summary>
    private void ApplyAlwaysOnTop()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = ViewModel.IsAlwaysOnTop;
        }
    }
}
