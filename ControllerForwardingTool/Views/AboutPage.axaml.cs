using Avalonia.Controls;
using Avalonia.Interactivity;
using ControllerForwardingTool.ViewModels;

namespace ControllerForwardingTool.Views;

public partial class AboutPage : UserControl
{
    public AboutPage()
    {
        InitializeComponent();
        DataContext = new AboutPageModel();
    }

    private async void OpenProject(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Uri uri } || DataContext is not AboutPageModel model) return;
        try
        {
            var launcher = TopLevel.GetTopLevel(this)?.Launcher;
            bool opened = launcher is not null && await launcher.LaunchUriAsync(uri);
            model.LinkStatus = opened ? "" : $"无法打开浏览器，请手动访问：{uri}";
        }
        catch (Exception)
        {
            model.LinkStatus = $"无法打开浏览器，请手动访问：{uri}";
        }
    }
}
