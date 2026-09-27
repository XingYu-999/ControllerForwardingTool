using Avalonia.Controls;
using Avalonia.Interactivity;
using ControllerForwardingTool.ViewModels;
namespace ControllerForwardingTool.Views;
public partial class TesterPage : UserControl
{
    public TesterPage() => InitializeComponent();

    private void StartKeyboardMouseTest(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not MainViewModel vm || !vm.StartKeyboardMouseTestCommand.CanExecute(null)) return;
        vm.StartKeyboardMouseTestCommand.Execute(null);
        // Once the cursor is captured, put the actual receiver and the Escape hint in view.
        if (vm.IsKeyboardMouseCaptured) TesterInputView.BringIntoView();
    }
}
