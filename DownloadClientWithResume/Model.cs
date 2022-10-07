using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DownloadClientWithResume
{
    public class ViewModel : ObservableObject
    {
        public ViewModel()
        {
            DownloadCommand = new AsyncRelayCommand(CommenceDownload, CanDownload);
            CancelDownloadCommand = new RelayCommand(CancelDownload, CanCancelDownload);
            DeleteFileCommand = new RelayCommand(DeleteFile, CanDeleteFile);
        }

        private HttpClient _httpClient = new HttpClient();
        private bool _isDownloading;
        private string _downloadUrl = "http://localhost:40808";//"http://speedcheck.cdn.on.net/10meg.test";
        private string _downloadFilePath = System.IO.Path.Combine(Environment.CurrentDirectory, "_tempfile.data");
        private string _logText = "";
        private double _downloadProgress = 0;

        CancellationTokenSource _downloadCts = null!;

        private void CancelDownload()
        {
            _downloadCts?.Cancel();
        }

        private bool CanCancelDownload()
        {
            Debug.WriteLine("CanCancelDownload");
            return _isDownloading && _downloadCts != null && !_downloadCts.IsCancellationRequested;
        }

        private bool CanDeleteFile()
        {
            Debug.WriteLine("CanDeleteFile");
            return !string.IsNullOrWhiteSpace(DownloadFilePath) && File.Exists(DownloadFilePath);
        }

        private void DeleteFile()
        {
            try
            {
                if (File.Exists(DownloadFilePath))
                    File.Delete(DownloadFilePath);
                DeleteFileCommand.NotifyCanExecuteChanged();
            }
            catch (Exception ex)
            {

            }
        }


        public IAsyncRelayCommand DownloadCommand { get; }

        public IRelayCommand CancelDownloadCommand { get; }
        public IRelayCommand DeleteFileCommand { get; }

        private const int chunkSizeBytes = 1024 * 1024;

        private async Task CommenceDownload()
        {
            _isDownloading = true;
            
            // TODO how to do resume with range or just with file size...

            _downloadCts = new CancellationTokenSource();
            HttpResponseMessage responseMessage = await _httpClient.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, _downloadCts.Token)
                .ConfigureAwait(false);
            responseMessage.EnsureSuccessStatusCode();
            var contentLength = responseMessage.Content.Headers.ContentLength ?? 0;

            using var responseStream = await responseMessage.Content.ReadAsStreamAsync();
            using var fileStream = File.Create(DownloadFilePath);
            fileStream.Position = 0;

            var byteBuffer = new byte[1024];
            long totalBytesProcessed = 0;
            
            while (totalBytesProcessed < contentLength
                && !_downloadCts.IsCancellationRequested)
            {
                int bytesRead = await responseStream.ReadAsync(byteBuffer, 0, byteBuffer.Length)
                    .ConfigureAwait(false);
                await fileStream.WriteAsync(byteBuffer, 0, bytesRead);
                totalBytesProcessed += bytesRead;
                LogText += $"{totalBytesProcessed}{Environment.NewLine}";
                DispatchToUiContext(() => DownloadProgress = totalBytesProcessed * 100.0 / contentLength);
            }
            _isDownloading = false;
            DispatchToUiContext(() => DeleteFileCommand.NotifyCanExecuteChanged());
        }

        private void DispatchToUiContext(Action action)
        {
            if (Application.Current.Dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                // this is too slow for progress and button enabled updates
                //Application.Current.Dispatcher.BeginInvoke(
                //  DispatcherPriority.Background,
                //  action);
                Application.Current.Dispatcher.Invoke(action);
            }
        }

        private bool CanDownload()
        {
            Debug.WriteLine("CanDownload");
            return !_isDownloading
                && !string.IsNullOrWhiteSpace(DownloadUrl)
                && !string.IsNullOrWhiteSpace(DownloadFilePath)
                && Directory.Exists(System.IO.Path.GetDirectoryName(DownloadFilePath));
        }

        public string DownloadUrl
        {
            get => _downloadUrl;
            set
            {
                SetProperty(ref _downloadUrl, value);
                DownloadCommand.NotifyCanExecuteChanged();
            }
        }
        public string DownloadFilePath
        {
            get => _downloadFilePath;
            set => SetProperty(ref _downloadFilePath, value);
        }

        public string LogText
        {
            get => _logText;
            set => SetProperty(ref _logText, value);
        }

        public double DownloadProgress
        {
            get => _downloadProgress;
            set => SetProperty(ref _downloadProgress, value);
        }


    }

    //public class ObservableObject : INotifyPropertyChanged
    //{
    //    public event PropertyChangedEventHandler? PropertyChanged;

    //    protected void RaisePropertyChanged(string propertyName)
    //    {
    //        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    //    }

    //    protected virtual bool Set<T>(ref T field, T value, params string[] propertyNames)
    //    {
    //        if (field == null && value == null)
    //            return false;

    //        if (field == null || !field.Equals(value))
    //        {
    //            field = value;

    //            foreach (var propertyName in propertyNames)
    //                RaisePropertyChanged(propertyName);

    //            return true;
    //        }

    //        return false;
    //    }


    //}

    //public class MyCommand : ICommand
    //{
    //    public event EventHandler? CanExecuteChanged;
    //    private Action _action;
    //    private Func<bool> _canDo;
    //    public MyCommand(Action action, Func<bool> canDo)
    //    {
    //        _action = action;
    //        _canDo = canDo;
    //    }

    //    public bool CanExecute(object? parameter)
    //    {
    //        return _canDo();
    //    }

    //    public void Execute(object? parameter)
    //    {
    //        if (!CanExecute(parameter))
    //            return;

    //        _action();
    //    }
    //}
}
