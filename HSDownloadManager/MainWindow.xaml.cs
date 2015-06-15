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
using HSDownloadManager.Properties;
using System.ComponentModel;

namespace HSDownloadManager
{
    
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		public SerializableCollection<Show> ShowCollection = new SerializableCollection<Show>();
        List<Pack> PacksToDownload = new List<Pack>();

        // The Show we're currently downloading
        Show nextShow;

        // Whether we've already started downloading packs
        bool downloading;

        // Have we failed downloading the current show?
        bool downloadError = false;

        IrcDotNet.StandardIrcClient client;
        IrcDotNet.Ctcp.CtcpClient ctcp;

        Settings settings = Settings.Default;

        public MainWindow()
        {
            InitializeComponent();

            try {
                ShowCollection.LoadFromFile(@".\shows");
            }
            catch (Exception e)
            {
                // The file doesn't exist, so this is our first time running. That's fine.
            }

            // Connect the ListView to the list of shows we're tracking
            Shows_LV.Items.Clear();
            Shows_LV.ItemsSource = ShowCollection;

            // When the user selects a show in the list, bring up an edit dialog.
            Shows_LV.SelectionChanged += (sender, args) =>
            {
                if (args.AddedItems.Count > 0)
                {
                    EditShowWindow win = new EditShowWindow(args.AddedItems[0] as Show);
                    win.Owner = this;
                    win.Show();
                }
            };

            // Update the status of the shows if they've become available.
            foreach (Show s in ShowCollection ) {
                UpdateShowStatus(s);
            }

            // Instantiate the IRC clients
            client = new StandardIrcClient();
            ctcp = new IrcDotNet.Ctcp.CtcpClient(client);

            // Hookup error handlers
            client.ConnectFailed += (sender, args) => { MessageBox.Show("Connection failed. Check the server setting and your internet connection."); };
            client.Error += (sender, args) => { MessageBox.Show("Generic error thrown: " + args.Error.Message); };

		}

        // Save the show information to the file when the user closes the window.
        protected override void OnClosing(CancelEventArgs e)
        {
            ShowCollection.SaveToFile(@".\shows");
        }

       

		/// <summary>
		/// Called when the "Download" button is clicked.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		void Download_Button_Click(object sender, RoutedEventArgs e)
        { 
            if (downloading)
            {
                MessageBox.Show("Already in the process of downloading.");
                return;
            }

            // Refresh show statuses in case we had an error last time.
            foreach (Show s in ShowCollection)
                UpdateShowStatus(s);

            // If we're already connected, go ahead and start searching for packs
            if (client.IsConnected)
            {
                Task.Factory.StartNew(() =>
               {
                   DownloadAvailableShows(null, null);
               });
            }
            else
            {
                // Otherwise, hookup the connected event and connect to the server.
                Task.Factory.StartNew(() =>
                {
                    // When the client is connected, start downloading the packs
                    client.Connected -= DownloadAvailableShows;
                    client.Connected += DownloadAvailableShows;

                    // Connect to the server.
                    string nick = settings.Nick;
                    client.Connect(new Uri(settings.Server), new IrcUserRegistrationInfo() { NickName = nick, RealName = nick, UserName = nick, Password = settings.Pass });
                });
            }
            
		}

        private void DownloadAvailableShows(object sender, EventArgs args)
        {
            downloading = true;

            // Join the channel
            client.Channels.Join(settings.Channel);

            foreach (Show s in ShowCollection)
            {
                if (s.Status == "Available")
                {
                    downloadError = false;
                    s.Status = "Searching";

                    // Ask the channel for the pack number of the episode we're looking for.
                    nextShow = s;
                    RequestPackNumber(s);

                    // Wait for the show to finish downloading before starting the next one.
                    lock (nextShow)
                    {
                        Monitor.Wait(nextShow);
                        if (downloadError)
                        {
                            nextShow.Status = "Error";
                        }
                        else
                        {
                            nextShow.Status = "Downloaded";
                            nextShow.NextEpisode++;
                            nextShow.AirsOn.AddDays(7);
                        }
                    }

                }
            }

            downloading = false;
        }

        /// <summary>
        /// Broadcasts in the channel asking for the pack number of the episode we're downloading.
        /// </summary>
        /// <param name="s"></param>
        void RequestPackNumber(Show s)
        {
            string episodeNumber = (s.NextEpisode > 9) ? s.NextEpisode.ToString() : "0" + s.NextEpisode.ToString();
            client.LocalUser.NoticeReceived += AcceptPackNumber;
            client.LocalUser.SendMessage(settings.Channel, "@find " + s.Name + " " + episodeNumber + " " + settings.Resolution);

            // If we don't find the pack number in less than 10 seconds, throw an error
            Task t = Task.Factory.StartNew(() =>
           {
               Thread.Sleep(settings.SearchTimeout * 1000);
               lock (nextShow)
               {
                   if (!nextShow.Status.Contains("Download")) // Downloading or Downloaded
                   {
                       if (nextShow.Status == "Searching")
                           Task.Factory.StartNew(() => MessageBox.Show("Unable to find pack number for " + nextShow.Name + " episode " + nextShow.NextEpisode));
                       if (nextShow.Status == "Requesting")
                           Task.Factory.StartNew(() => MessageBox.Show("Bot did not respond to XDCC SEND request for " + nextShow.Name + " episode " + nextShow.NextEpisode));
                       downloadError = true;
                       Monitor.Pulse(nextShow);
                   }
               }
           });
        }

        /// <summary>
        /// Accepts a response with the pack number we're looking for
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AcceptPackNumber(object sender, IrcMessageEventArgs e)
        {
            if (nextShow == null)
                return;

            lock (nextShow)
            {
                Pack nextPack = new Pack();
                Show s = nextPack.Show = nextShow;

                string text = e.Text.ToLower();

                // If the response is for the show we're looking for, and we haven't already started downloading the show
                if (s.Status == "Searching" && text.Contains(settings.TargetBot.ToLower()) && text.Contains(s.Name.ToLower()))
                {
                    int packStart = text.IndexOf('#');
                    int packEnd = packStart + 1;
                    while (packEnd + 1 < text.Length && char.IsDigit(text.ElementAt(packEnd + 1)))
                        ++packEnd;

                    nextPack.Number = text.Substring(packStart + 1, packEnd - packStart);

                    nextPack.Target = settings.TargetBot;

                    RequestDownloadPack(nextPack);

                    client.LocalUser.NoticeReceived -= AcceptPackNumber;
                }
            }
        }

        /// <summary>
        /// Given a target bot and a pack number, message that bot to send the pack
        /// </summary>
        /// <param name="p"></param>
        private void RequestDownloadPack(Pack p)
        {
            p.Show.Status = "Requesting";

            Console.WriteLine("Requesting pack #" + p.Number + " from " + p.Target);

            ctcp.RawMessageReceived += AcceptDownloadRequest;

            client.LocalUser.SendMessage(p.Target, "xdcc send " + p.Number);
        }

        /// <summary>
        /// Given a CTCP DCC SEND request, connect to the sender and download the file.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AcceptDownloadRequest(object sender, IrcDotNet.Ctcp.CtcpRawMessageEventArgs e)
        {
            if (nextShow == null)
                return;

            string msg = e.Message.Data;
            if (msg != null && msg.StartsWith("SEND"))
            {
                ctcp.RawMessageReceived -= AcceptDownloadRequest;

                lock (nextShow)
                {
                    nextShow.Status = "Downloading";
                }

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

                try {
                    // Open a file for writing
                    string fullFilename = settings.DownloadsFolder + @"\" + filename;
                    while (File.Exists(fullFilename))
                        fullFilename += ".1";
                    FileStream file = System.IO.File.Open(fullFilename, System.IO.FileMode.CreateNew, FileAccess.Write, FileShare.Read);

                    // Connect to the XDCC server on the specified ip and port
                    IPEndPoint endPt = new IPEndPoint(ipAdd, port);
                    Socket sock = new Socket(SocketType.Stream, ProtocolType.Tcp);
                    sock.Connect(endPt);

                    // Read the data into the file
                    byte[] buffer = new byte[1024];
                    int bytesRead, totalBytesRead = 0;
                    while ((bytesRead = sock.Receive(buffer)) > 0 && totalBytesRead < fileSize)
                    {
                        file.Write(buffer, 0, bytesRead);
                        totalBytesRead += bytesRead;

                        nextShow.Status = "Downloading (" + totalBytesRead / (1024 * 1024) + " MB)";
                    }

                    file.Close();
                }
                catch (Exception err)
                {
                    MessageBox.Show("Exception thrown: " + err.Message);
                    lock (nextShow)
                    {
                        downloadError = true;
                    }
                }

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
            new SettingsWindow().Show();
		}

        /// <summary>
        /// Called when the "Add Show" button is clicked
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void Add_Button_Click(object sender, RoutedEventArgs e)
        {
            new EditShowWindow(null).Show();
        }

        /// <summary>
        /// Checks if the show has become available and sets its status appropriately.
        /// </summary>
        /// <param name="s"></param>
        void UpdateShowStatus(Show s)
        {
            if (s.AirsOn < DateTime.Now)
                s.Status = "Available";
            else
                s.Status = "Unavailable";
        }
    }

}
