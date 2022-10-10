using System.ComponentModel;
using System.Linq;
using System.Windows;

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
            DataContext = new ViewModel();
        }

        private void OutputTextBlock_TargetUpdated(object sender, System.Windows.Data.DataTransferEventArgs e)
        {
            OutputScroll.ScrollToBottom();
        }
    }

}
