using HSDownloadManager.Properties;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace HSDownloadManager
{
    /// <summary>
    /// Interaction logic for SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
        }

        private void Save_Button_Click(object sender, RoutedEventArgs e)
        {
            Settings set = Properties.Settings.Default;
            set.Server = ServerTB.Text;
            set.Channel = ChannelTB.Text;
            set.Nick = NickTB.Text;
            set.Pass = PassTB.Text;
            set.Resolution = ResolutionTB.Text;
            set.TargetBot = TargetBotTB.Text;
            set.DownloadsFolder = DownloadsFolderTB.Text;
            set.SearchTimeout = int.Parse(TimeoutTB.Text);

            set.Save();
            this.Close();
        }
    }
}
