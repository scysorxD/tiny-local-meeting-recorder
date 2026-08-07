using System.Windows;
using LocalMeetingNotes.App.ViewModels;
using LocalMeetingNotes.App.Views;
using Microsoft.Extensions.DependencyInjection;

namespace LocalMeetingNotes.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly IServiceProvider _services;

    public MainWindow(MainViewModel viewModel, IServiceProvider services)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _services = services;
        DataContext = viewModel;
        _viewModel.StartRequested += OnStartRequested;
        Closed += OnClosed;
    }

    private async void OnStartRequested(object? sender, EventArgs e)
    {
        var dialog = new StartRecordingWindow(_services.GetRequiredService<StartRecordingViewModel>())
        {
            Owner = this,
        };

        if (dialog.ShowDialog() != true || dialog.Metadata is null)
        {
            return;
        }

        try
        {
            await _viewModel.StartRecordingAsync(dialog.Metadata);
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(this, exception.Message, "Unable to start recording", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.StartRequested -= OnStartRequested;
        _viewModel.Dispose();
    }
}