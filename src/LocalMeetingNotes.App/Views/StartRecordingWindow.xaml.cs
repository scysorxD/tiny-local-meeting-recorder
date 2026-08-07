using System.Windows;
using LocalMeetingNotes.App.ViewModels;
using LocalMeetingNotes.Core.Models;

namespace LocalMeetingNotes.App.Views;

public partial class StartRecordingWindow : Window
{
    public StartRecordingWindow(StartRecordingViewModel viewModel)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    public StartRecordingViewModel ViewModel { get; }

    public MeetingMetadata? Metadata { get; private set; }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Metadata = ViewModel.CreateMetadata();
            DialogResult = true;
        }
        catch (InvalidOperationException exception)
        {
            System.Windows.MessageBox.Show(this, exception.Message, "Start recording", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
