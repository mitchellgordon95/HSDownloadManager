using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using System.Windows.Navigation;
using System.Windows.Shapes;
using IrcDotNet;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.IO;

namespace HSDownloadManager
{
    
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		ObservableCollection<Show> ShowCollection = new ObservableCollection<Show>();
        List<Pack> PacksToDownload = new List<Pack>();

        // The Show we're currently downloading
        Show nextShow;

        // Whether we've already started downloading packs
        bool downloading;

        IrcDotNet.StandardIrcClient client;
        IrcDotNet.Ctcp.CtcpClient ctcp;
        string server = "irc://irc.rizon.net";
        string channel = "#horriblesubs";
        string nick = "SomeTotallyInconspicuousNick";
        string pass = "";
        string resolution = "720";
        string targetBot = "hellokitty";
        string downloadsFolder = @"C:\Users\Megaflux\Documents\Downloads";

        public MainWindow()
        {
            InitializeComponent();

            // Connect the ListView to the list of shows we're tracking
            Shows_LV.Items.Clear();
            Shows_LV.ItemsSource = ShowCollection;
            ShowCollection.Add(new Show { Name = "Denpa Kyoushi", Status = "Unavailable", AirsOn = new DateTime(2015, 6, 13, 5, 0, 0), NextEpisode = 11 });

            // Update the status of the shows if they've become available.
            foreach (Show s in ShowCollection ) {
                UpdateShowStatus(s);
            }

            // Connect to the irc server
            client = new IrcDotNet.StandardIrcClient();
            client.Connect(new Uri(server), new IrcUserRegistrationInfo() { NickName = nick, RealName = nick, UserName = nick, Password = pass} );

            // Initialize CTCP client
            ctcp = new IrcDotNet.Ctcp.CtcpClient(client);

            // Set the listener for incoming downloads
            ctcp.RawMessageReceived += AcceptDownloadRequest; 
		}



        /// <summary>
        /// Checks if the show has become available and sets its status appropriately.
        /// </summary>
        /// <param name="s"></param>
        void UpdateShowStatus (Show s)
        {
            if (s.AirsOn < DateTime.Now)
                s.Status = "Available";
            else
                s.Status = "Unavailable";

        }

		/// <summary>
		/// Called when the "Download" button is clicked.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		void Download_Button_Click(object sender, RoutedEventArgs e) {

            if (!client.IsConnected)
            {
                MessageBox.Show("Error: Server not connected.");
                return;
            }
            if (downloading)
            {
                MessageBox.Show("Already in the process of downloading.");
                return;
            }

            downloading = true;

            // Do the downloading in a background thread so we don't block the UI.
            Task t = Task.Factory.StartNew(() =>
            {
               // Join the channel and set the listener for incoming pack numbers
               client.Channels.Join(channel);
               client.LocalUser.NoticeReceived += AcceptPackNumber;

               // Ask the channel for the pack numbers 
               foreach (Show s in ShowCollection)
               {
                   if (s.Status == "Available")
                   {
                       s.Status = "Searching";

                       // Ask the channel for the pack number of the episode we're looking for.
                       nextShow = s;                     
                       RequestPackNumber(s);

                        // Wait for the show to finish downloading before starting the next one.
                        lock (nextShow)
                        {
                            Monitor.Wait(nextShow);
                        }

                        s.Status = "Downloaded";
                        s.NextEpisode++;
                        s.AirsOn.AddDays(7);
                   }
               }

                downloading = false;
           });

            
		}

        /// <summary>
        /// Broadcasts in the channel asking for the pack number of the episode we're downloading.
        /// </summary>
        /// <param name="s"></param>
        void RequestPackNumber(Show s)
        {
            string episodeNumber = (s.NextEpisode > 9) ? s.NextEpisode.ToString() : "0" + s.NextEpisode.ToString();

            client.LocalUser.SendMessage(channel, "@find " + s.Name + " " + episodeNumber + " " + resolution);
        }

        /// <summary>
        /// Accepts a response with the pack number we're looking for
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AcceptPackNumber(object sender, IrcMessageEventArgs e)
        {
            Pack nextPack = new Pack();
            Show s = nextPack.Show = nextShow;

            string text = e.Text.ToLower();

            Console.WriteLine(text);

            // If the response is for the show we're looking for, and we haven't already started downloading the show
            if (s.Status == "Searching" && text.Contains(targetBot.ToLower()) && text.Contains(s.Name.ToLower()))
            {
                int packStart = text.IndexOf('#');
                int packEnd = packStart + 1;
                while (char.IsDigit(text.ElementAt(packEnd + 1)))
                    ++packEnd;

                nextPack.Number = text.Substring(packStart + 1, packEnd - packStart);

                nextPack.Target = targetBot;

                RequestDownloadPack(nextPack);
            }
        }

        private void RequestDownloadPack(Pack p)
        {
            p.Show.Status = "Downloading";

            Console.WriteLine("Requesting pack #" + p.Number + " from " + p.Target);

            client.LocalUser.SendMessage(p.Target, "xdcc send " + p.Number);
        }

        private void AcceptDownloadRequest(object sender, IrcDotNet.Ctcp.CtcpRawMessageEventArgs e)
        {
            string msg = e.Message.Data;
            if (msg != null && msg.StartsWith("SEND"))
            {
                // It's a DCC SEND request. The format is "SEND [Filename] [IP Integer] [Port number] [File Size]"
                int spaceAfterFilename = msg.Length;
                for (int i = 0; i < 3; ++i)
                    spaceAfterFilename = msg.LastIndexOf(' ', spaceAfterFilename - 1);

                string[] parts = msg.Substring(spaceAfterFilename + 1).Split(' ');

                UInt32 ipInt = UInt32.Parse(parts[0]);
                int port = int.Parse(parts[1]);
                int fileSize = int.Parse(parts[2]);
                string filename = msg.Substring(5, spaceAfterFilename - 5);

                // Strip quotes from the file name
                if (filename.ElementAt(0) == '"')
                    filename = filename.Substring(1, filename.Length - 2);

                // Convert the ip address integer to an actual ip address.
                byte[] bytes = BitConverter.GetBytes(ipInt);

                // The integer value must be in big endian format to be properly converted.
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(bytes);

                IPAddress ipAdd = new IPAddress(bytes);

                Console.WriteLine("Received DCC SEND request for file " + filename + " at " + ipAdd.ToString() + ":" + port);

                // Open a file for writing
                FileStream file = System.IO.File.Open(downloadsFolder + @"\" + filename, System.IO.FileMode.OpenOrCreate, FileAccess.Write, FileShare.);

                // Connect to the XDCC server on the specified ip and port
                IPEndPoint endPt = new IPEndPoint(ipAdd, port);
                Socket sock = new Socket(SocketType.Stream, ProtocolType.Tcp);
                sock.Connect(endPt);

                // Read the data into the file
                byte[] buffer = new byte[1024];
                int bytesRead, totalBytesRead = 0;
                while ( (bytesRead = sock.Receive(buffer)) > 0 && totalBytesRead < fileSize)
                {
                    file.Write(buffer, 0, bytesRead);
                    totalBytesRead += bytesRead;

                    // Flush the file stream every ~5 MB
                    if (totalBytesRead > 0 && totalBytesRead % 5000 == 0)
                        file.Flush();

                    nextShow.Status = "Downloading (" + totalBytesRead / (1024 * 1024) + " MB)";
                }

                file.Close();

                // Signal the main loop that the show has finished downloading.
                lock (nextShow)
                {
                    Monitor.Pulse(nextShow);
                }

            }
        }

		/// <summary>
		/// Called when the "Settings" button is clicked.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		void Settings_Button_Click(object sender, RoutedEventArgs e)
		{
		}
	}

}
