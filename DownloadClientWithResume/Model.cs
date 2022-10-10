using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection.Metadata;
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
            DispatchToUiContext(() => LogText += $"{Environment.NewLine}{Environment.NewLine}Commencing download{Environment.NewLine}");
            try
            {
                // UI stuff
                _isDownloading = true;
                _downloadCts = new CancellationTokenSource();
                CancelDownloadCommand.NotifyCanExecuteChanged();
                DownloadCommand.NotifyCanExecuteChanged();
                double lastProgress = 0;
                double currentProgress = 0;

                const int maxTries = 10; // TODO max tries or max timeout?
                int tries = 0;
                long contentLength = -1;
                long totalBytesProcessed = 0;
                bool acceptsRanges = false;
                FileInfo fi = new(DownloadFilePath);
                bool appendToExistingFile = false; // TODO
                bool useExistingFile = appendToExistingFile && fi.Exists;
                using var fileStream = useExistingFile
                    ? new FileStream(DownloadFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)
                    : new FileStream(DownloadFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                if (useExistingFile)
                {
                    totalBytesProcessed = fi.Length;
                }
                while (contentLength == -1 
                    || (acceptsRanges && totalBytesProcessed < contentLength))
                {
                    try
                    {
                        HttpRequestMessage requestMessage = new(HttpMethod.Get, DownloadUrl);

                        if (contentLength != -1)
                        {
                            requestMessage.Headers.Range = new(totalBytesProcessed, null);
                            // TODO if only specifying 'from' above doesn't work, then try setting 'to': requestMessage.Headers.Range = new(totalBytesProcessed, contentLength - 1);

                            // Debugging
                            DispatchToUiContext(() => LogText += $"Requesting range {totalBytesProcessed}-{contentLength-1}{Environment.NewLine}");
                        }

                        HttpResponseMessage responseMessage = await _httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, _downloadCts.Token)
                            .ConfigureAwait(false);
                        responseMessage.EnsureSuccessStatusCode();

                        if (contentLength == -1)
                        {
                            contentLength = responseMessage.Content.Headers.ContentLength ?? 0;
                            acceptsRanges = responseMessage.Headers.AcceptRanges.Any(x => string.Equals(x, "bytes", StringComparison.OrdinalIgnoreCase));

                            // Debugging
                            DispatchToUiContext(() => LogText += $"Accept Ranges: {string.Join(". ", responseMessage.Headers.AcceptRanges)}.{Environment.NewLine}");
                        }
                        else
                        {
                            if (responseMessage.Content.Headers.ContentRange == null)
                            {
                                contentLength = -1;
                                throw new IOException($"Server didn't send expected content range. Resetting content length for retry. Note: Server's Accepts Range: {string.Join(". ", responseMessage.Headers.AcceptRanges)}");
                            }

                            // Debugging
                            var contentRange = responseMessage.Content.Headers.ContentRange;
                            var chunkContentLength = responseMessage.Content.Headers.ContentLength ?? 0;
                            DispatchToUiContext(() => LogText += $"Processing range {contentRange.From}-{contentRange.To}. Chunk Length: {chunkContentLength}. (Total Response Length: {contentLength}{Environment.NewLine}");
                        }

                        if (contentLength == 0)
                            break; // TODO could warn about empty file...?

                        // process the response stream
                        using var responseStream = await responseMessage.Content.ReadAsStreamAsync();

                        var byteBuffer = new byte[1024];
                        while (totalBytesProcessed < contentLength
                            && !_downloadCts.IsCancellationRequested)
                        {
                            int bytesRead = await responseStream.ReadAsync(byteBuffer, 0, byteBuffer.Length)
                                .ConfigureAwait(false);

                            if ((bytesRead == 0 && totalBytesProcessed < contentLength)
                                || (bytesRead < byteBuffer.Length && (totalBytesProcessed + bytesRead) < contentLength))
                            {
                                throw new IOException($"Unexpected end of stream.");
                            }
                            else if (bytesRead + totalBytesProcessed > contentLength)
                            {
                                // TODO This should probably be a non-retry
                                throw new IOException($"Too many byte received. Expected: {contentLength}. Received: {bytesRead + totalBytesProcessed} ");
                            }

                            await fileStream.WriteAsync(byteBuffer, 0, bytesRead);
                            totalBytesProcessed += bytesRead;

                            // UI/debugging to be abstracted
                            currentProgress = totalBytesProcessed * 100.0 / contentLength;
                            if (currentProgress > 0 && currentProgress - lastProgress > 0.5)
                            {
                                DispatchToUiContext(() => LogText += $"{totalBytesProcessed}{Environment.NewLine}");
                                DispatchToUiContext(() => DownloadProgress = currentProgress);
                                lastProgress = currentProgress;
                            }
                        }
                    }
                    catch (IOException ex)
                    {
                        tries++;
                        if (tries >= maxTries)
                        {
                            throw new IOException($"Unable to complete download. {totalBytesProcessed} of {contentLength} ({totalBytesProcessed * 100.0 / contentLength}");
                        }
                        DispatchToUiContext(() => LogText += $"Retrying. Number of attempts: {tries}. Caught IOException {totalBytesProcessed} of {contentLength} ({totalBytesProcessed*100.0/contentLength}%) {Environment.NewLine}");

                    }
                }
                if (totalBytesProcessed < contentLength)
                {
                    DispatchToUiContext(() => LogText += $"Prematurely ended download. Only read {totalBytesProcessed} of {contentLength} total. Server AcceptsRanges: {acceptsRanges}. ");
                }
                else
                {
                    DispatchToUiContext(() => LogText += $"contentLength:{contentLength}, acceptsRanges: {acceptsRanges}, totalBytesProcessed: {totalBytesProcessed} ");
                }

            }
            catch (Exception ex)
            {
                DispatchToUiContext(() => LogText += ex.ToString());
            }
            finally
            {
                _isDownloading = false;
            }
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
            set
            {
                SetProperty(ref _logText, value);
            }
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
