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
    /// Interaction logic for AddShowWindow.xaml
    /// </summary>
    public partial class EditShowWindow : Window
    {
        Show selectedShow;

        public EditShowWindow(Show s)
        {
            InitializeComponent();

            selectedShow = s;

            if (s != null)
            {
                Name.Text = s.Name;
                NextEpisode.Text = s.NextEpisode.ToString();
                AirsOn.Text = s.AirsOn.ToString();
                Hour.Text = s.AirsOn.Hour.ToString();
            }
        }

        private void Save_Button_Click(object sender, RoutedEventArgs e)
        {
            try {
                Show s = (selectedShow == null) ? new HSDownloadManager.Show() : selectedShow;
                s.Name = Name.Text;
                s.NextEpisode = int.Parse(NextEpisode.Text);
                s.AirsOn = DateTime.Parse(AirsOn.Text);
                s.AirsOn = s.AirsOn.AddHours(int.Parse(Hour.Text));
                s.Status = (s.AirsOn < DateTime.Now) ? "Available" : "Unavailable";

                if (selectedShow == null)
                    Application.Current.Windows.OfType<MainWindow>().ElementAt(0).ShowCollection.Add(s);
                this.Close();
            }
            catch (FormatException ex)
            {
                MessageBox.Show("One of your fields is malformed. You might want to fix it.");
            }
        }

        private void Delete_Button_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Windows.OfType<MainWindow>().ElementAt(0).ShowCollection.Remove(selectedShow);

            this.Close();
        }
    }
}
