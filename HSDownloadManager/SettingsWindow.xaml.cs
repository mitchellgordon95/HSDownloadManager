using HSDownloadManager.Properties;
using Microsoft.Win32;
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
            try {
                Settings set = Properties.Settings.Default;
                set.Server = ServerTB.Text;
                set.Channel = ChannelTB.Text;
                set.Nick = NickTB.Text;
                set.Pass = PassTB.Text;
                set.Resolution = ResolutionTB.Text;
                set.TargetBot = TargetBotTB.Text;
                set.DownloadsFolder = DownloadsFolderTB.Text;
                set.SearchTimeout = int.Parse(TimeoutTB.Text);
                set.RunOnWindowsStartup = RunOnWindowsStartupCB.IsChecked.Value;
                set.DownloadOnStartup = DownloadOnStartupCB.IsChecked.Value;

                set.Save();

                if (set.RunOnWindowsStartup)
                    StartUpManager.AddApplicationToCurrentUserStartup();
                else
                    StartUpManager.RemoveApplicationFromCurrentUserStartup();

                this.Close();
            }
            catch (FormatException ex)
            {
                MessageBox.Show("One of your settings is malformed. Please try again.");
            }
        }

        private void Downloads_Folder_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Forms.FolderBrowserDialog diag = new System.Windows.Forms.FolderBrowserDialog();
            if (diag.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                DownloadsFolderTB.Text = diag.SelectedPath;
        }

        private void Import_Shows_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog diag = new OpenFileDialog();
            diag.FileOk += (sender2, args) => {

                try {
                    (Owner as MainWindow).ShowCollection.LoadFromFile(diag.FileName);
                    MessageBox.Show("Loaded shows from " + diag.FileName);
                }
                catch (Exception err)
                {
                    MessageBox.Show("Unable to parse file.");
                }
            };
            diag.ShowDialog(this);
        }

        private void Export_Shows_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog diag = new SaveFileDialog();
            diag.FileOk += (sender2, args) =>
            {

                try
                {
                    (Owner as MainWindow).ShowCollection.SaveToFile(diag.FileName);
                    MessageBox.Show("Saved shows to " + diag.FileName);
                }
                catch (Exception err)
                {
                    MessageBox.Show("Unable to save file.");
                }
            };
            diag.ShowDialog(this);
        }
    }
}
