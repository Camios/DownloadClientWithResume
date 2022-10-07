using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using static System.Collections.Specialized.BitVector32;

namespace DownloadClientWithResume
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        public MainWindow()
        {
            InitializeComponent();
            DataContext = new Model();
        }


    }

    public class Model : ObservableObject
    {
        public Model()
        {
            DownloadCommand = new MyCommand(CommenceDownload, CanDownload);
            CancelDownloadCommand = new MyCommand(CancelDownload, CanCancelDownload);
            DeleteFileCommand = new MyCommand(DeleteFile, CanDeleteFile);
        }

        private bool CanDeleteFile()
        {
            return !string.IsNullOrWhiteSpace(DownloadFilePath);
        }

        private void DeleteFile()
        {
            try
            {
                if (File.Exists(_downloadFilePath))
                    File.Delete(_downloadFilePath);
            }
            catch (Exception ex)
            {

            }
        }

        private HttpClient _httpClient = new HttpClient();
        private bool _isDownloading;
        private string _downloadUrl = null!;
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
            return _isDownloading && _downloadCts != null && !_downloadCts.IsCancellationRequested;
        }

        public ICommand DownloadCommand { get; set; }

        public ICommand CancelDownloadCommand { get; set; }
        public ICommand DeleteFileCommand { get; set; }

        private const int chunkSizeBytes = 1024 * 1024;

        private async void CommenceDownload()
        {
            _isDownloading = true;
            
            _downloadCts = new CancellationTokenSource();
            HttpResponseMessage responseMessage = await _httpClient.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, _downloadCts.Token);
            responseMessage.EnsureSuccessStatusCode();
            var contentLengthHeader = responseMessage.Headers.GetValues("Content-Length").First();
            var contentLength = long.Parse(contentLengthHeader);

            using var responseStream = await responseMessage.Content.ReadAsStreamAsync();
            using var fileStream = File.Create(DownloadFilePath);
            fileStream.Position = 0;
            responseStream.Position = 0;

            var byteBuffer = new byte[1024];
            long totalBytesProcessed = 0;
            
            while (contentLength > 0
                && !_downloadCts.IsCancellationRequested)
            {
                int bytesRead = await responseStream.ReadAsync(byteBuffer, 0, byteBuffer.Length);
                await fileStream.WriteAsync(byteBuffer, 0, bytesRead);
                totalBytesProcessed += bytesRead;
                DownloadProgress = totalBytesProcessed / contentLength;
            }
        }

        private bool CanDownload()
        {
            return !_isDownloading
                && !string.IsNullOrWhiteSpace(DownloadUrl)
                && !string.IsNullOrWhiteSpace(DownloadFilePath)
                && Directory.Exists(System.IO.Path.GetDirectoryName(DownloadFilePath));
        }

        public string DownloadUrl
        {
            get => _downloadUrl;
            set => Set(ref _downloadUrl, value);
        }
        public string DownloadFilePath
        {
            get => _downloadFilePath;
            set => Set(ref _downloadFilePath, value);
        }

        public string LogText
        {
            get => _logText;
            set => Set(ref _logText, value);
        }

        public double DownloadProgress
        {
            get => _downloadProgress;
            set => Set(ref _downloadProgress, value);
        }


    }

    public class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void RaisePropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected virtual bool Set<T>(ref T field, T value, params string[] propertyNames)
        {
            if (field == null && value == null)
                return false;

            if (field == null || !field.Equals(value))
            {
                field = value;

                foreach (var propertyName in propertyNames)
                    RaisePropertyChanged(propertyName);

                return true;
            }

            return false;
        }


    }

    public class MyCommand : ICommand
    {
        public event EventHandler? CanExecuteChanged;
        private Action _action;
        private Func<bool> _canDo;
        public MyCommand(Action action, Func<bool> canDo)
        {
            _action = action;
            _canDo = canDo;
        }

        public bool CanExecute(object? parameter)
        {
            return _canDo();
        }

        public void Execute(object? parameter)
        {
            if (!CanExecute(parameter))
                return;

            _action();
        }
    }
}
