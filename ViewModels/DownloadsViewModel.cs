using NineLivesAudio.Data;
using NineLivesAudio.Messages;
using NineLivesAudio.Models;
using NineLivesAudio.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Dispatching;
using System.Collections.ObjectModel;

namespace NineLivesAudio.ViewModels;

public partial class DownloadsViewModel : ObservableObject, IDisposable
{
    private bool _disposed;
    private readonly IDownloadService _downloadService;
    private readonly ILocalDatabase _database;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly ILocalizationService _localization;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    public ObservableCollection<DownloadItem> ActiveDownloads { get; } = new();
    public ObservableCollection<DownloadItem> CompletedDownloads { get; } = new();

    public DownloadsViewModel(
        IDownloadService downloadService,
        ILocalDatabase database,
        ILocalizationService localization)
    {
        _downloadService = downloadService;
        _database = database;
        _localization = localization;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        WeakReferenceMessenger.Default.Register<DownloadProgressChangedMessage>(this, (r, m) =>
            ((DownloadsViewModel)r).OnDownloadProgressChanged(m.Value));
        WeakReferenceMessenger.Default.Register<DownloadCompletedMessage>(this, (r, m) =>
            ((DownloadsViewModel)r).OnDownloadCompleted(m.Value));
        WeakReferenceMessenger.Default.Register<DownloadFailedMessage>(this, (r, m) =>
            ((DownloadsViewModel)r).OnDownloadFailed(m.Value));
    }

    public async Task InitializeAsync()
    {
        await LoadDownloadsAsync();
    }

    [RelayCommand]
    private async Task LoadDownloadsAsync()
    {
        try
        {
            IsLoading = true;
            HasError = false;

            // Load from database
            var allDownloads = await _database.GetAllDownloadItemsAsync();

            // Also get active downloads from service (in-memory)
            var activeFromService = await _downloadService.GetActiveDownloadsAsync();

            ActiveDownloads.Clear();
            CompletedDownloads.Clear();

            // Merge both sources, preferring service data for active downloads
            var activeIds = new HashSet<string>(activeFromService.Select(d => d.Id));

            foreach (var download in activeFromService)
            {
                if (download.Status == DownloadStatus.Completed)
                {
                    CompletedDownloads.Add(download);
                }
                else
                {
                    ActiveDownloads.Add(download);
                }
            }

            // Add completed downloads from database that aren't in service
            foreach (var download in allDownloads.Where(d => !activeIds.Contains(d.Id)))
            {
                if (download.Status == DownloadStatus.Completed)
                {
                    CompletedDownloads.Add(download);
                }
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = _localization.Format("FailedLoadDownloads", ex.Message);
            HasError = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task PauseDownloadAsync(DownloadItem? download)
    {
        if (download == null) return;

        try
        {
            await _downloadService.PauseDownloadAsync(download.Id);
            download.Status = DownloadStatus.Paused;
        }
        catch (Exception ex)
        {
            ErrorMessage = _localization.Format("FailedPause", ex.Message);
            HasError = true;
        }
    }

    [RelayCommand]
    private async Task ResumeDownloadAsync(DownloadItem? download)
    {
        if (download == null) return;

        try
        {
            await _downloadService.ResumeDownloadAsync(download.Id);
        }
        catch (Exception ex)
        {
            ErrorMessage = _localization.Format("FailedResume", ex.Message);
            HasError = true;
        }
    }

    [RelayCommand]
    private async Task CancelDownloadAsync(DownloadItem? download)
    {
        if (download == null) return;

        try
        {
            await _downloadService.CancelDownloadAsync(download.Id);
            ActiveDownloads.Remove(download);
        }
        catch (Exception ex)
        {
            ErrorMessage = _localization.Format("FailedCancel", ex.Message);
            HasError = true;
        }
    }

    [RelayCommand]
    private async Task DeleteDownloadAsync(DownloadItem? download)
    {
        if (download == null) return;

        try
        {
            await _downloadService.DeleteDownloadAsync(download.AudioBookId);
            await _database.DeleteDownloadItemAsync(download.Id);
            CompletedDownloads.Remove(download);
        }
        catch (Exception ex)
        {
            ErrorMessage = _localization.Format("FailedDelete", ex.Message);
            HasError = true;
        }
    }

    [RelayCommand]
    private async Task ClearCompletedAsync()
    {
        try
        {
            var completedIds = CompletedDownloads.Select(d => d.Id).ToList();
            foreach (var id in completedIds)
            {
                await _database.DeleteDownloadItemAsync(id);
            }
            CompletedDownloads.Clear();
        }
        catch (Exception ex)
        {
            ErrorMessage = _localization.Format("FailedClearCompleted", ex.Message);
            HasError = true;
        }
    }

    private void OnDownloadProgressChanged(DownloadProgressEventArgs e)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            var download = ActiveDownloads.FirstOrDefault(d => d.Id == e.DownloadId);
            if (download != null)
            {
                download.DownloadedBytes = e.DownloadedBytes;
                download.TotalBytes = e.TotalBytes;
            }
        });
    }

    private void OnDownloadCompleted(DownloadItem download)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            var active = ActiveDownloads.FirstOrDefault(d => d.Id == download.Id);
            if (active != null)
                ActiveDownloads.Remove(active);
            CompletedDownloads.Insert(0, download);
        });
    }

    private void OnDownloadFailed(DownloadItem download)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            var active = ActiveDownloads.FirstOrDefault(d => d.Id == download.Id);
            if (active != null)
            {
                active.Status = DownloadStatus.Failed;
                active.ErrorMessage = download.ErrorMessage;
            }
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }
}
