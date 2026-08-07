using LocalMeetingNotes.App.ViewModels;

namespace LocalMeetingNotes.App.Views;

public partial class SettingsWindow : System.Windows.Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        DataContext = viewModel;
    }

    private void Close_Click(object sender, System.Windows.RoutedEventArgs e) => Close();
}
