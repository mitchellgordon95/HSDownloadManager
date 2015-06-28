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
using System.Reflection;

namespace HSDownloadManager
{
    
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		public SerializableCollection<Show> ShowCollection = new SerializableCollection<Show>();

        // The list of packs that didn't come from our preferred bot
        List<Pack> NonPreferredPacks = new List<Pack>();

        // The Show we're currently downloading
        Show nextShow;

        // Whether we've already started downloading packs
        bool downloading;

        // Have we failed downloading the current show?
        bool downloadError = false;

        // Did the user skip the current show?
        bool skipped = false;

        // The various tasks (and associated cancellation tokens) that will be running while we're in the process of downloading.
        Task mainTask;
        CancellationTokenSource mainTaskTokenSource;
        Task searchTimeoutTask;
        CancellationTokenSource searchTimeoutTokenSource;
        Task downloadTimeoutTask;
        CancellationTokenSource downloadTimeoutTokenSource;
        Task downloadTask;
        CancellationTokenSource downloadTaskTokenSource;

        IrcDotNet.StandardIrcClient client;
        IrcDotNet.Ctcp.CtcpClient ctcp;

        Settings settings = Settings.Default;

        public MainWindow()
        {
            // Set the current directory to wherever the executable is located. 
            // The current directory is sometimes C:\Windows\System32 when the application starts on windows startup
            string executableDir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            Directory.SetCurrentDirectory(executableDir);
            
            InitializeComponent();

            try {
                ShowCollection.LoadFromFile(Directory.GetCurrentDirectory() + @"\shows");
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

            // If the "Download On Startup" option is selected, go ahead and do it.
            if (settings.DownloadOnStartup)
                Download_Button_Click(null, null);

		}

        // Save the show information to the file when the user closes the window.
        protected override void OnClosing(CancelEventArgs e)
        {
            try {
                ShowCollection.SaveToFile(Directory.GetCurrentDirectory() + @"\shows");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Exception: " + ex.Message);
            }
        }
		
        private void DownloadAvailableShows(object sender, EventArgs args)
        {
            mainTaskTokenSource = new CancellationTokenSource();
            var token = mainTaskTokenSource.Token;
            mainTask = Task.Factory.StartNew(() =>
           {
                downloading = true;
                
                // Join the channel
                client.Channels.Join(settings.Channel);

               foreach (Show s in ShowCollection)
               {
                   if (s.Status == "Available")
                   {
                       downloadError = false;
                       skipped = false;
                       s.Status = "Searching";

                        // Ask the channel for the pack number of the episode we're looking for.
                        nextShow = s;
                       RequestPackNumber(s);

                        // Wait for the show to finish downloading before starting the next one.
                        lock (nextShow)
                       {
                           Monitor.Wait(nextShow);
                           if (skipped)
                           {
                               nextShow.Status = "Skipped";
                               skipped = false;
                           }
                           else if (token.IsCancellationRequested)
                           {
                               nextShow.Status = "Canceled";
                               break;
                           }

                           else if (downloadError)
                           {
                               nextShow.Status = "Error";
                           }
                           else
                           {
                               nextShow.Status = "Downloaded";
                               nextShow.NextEpisode++;
                               nextShow.AirsOn = nextShow.AirsOn.AddDays(7);
                           }
                       }

                   }
               }

               downloading = false;
           }, token);

        }

        /// <summary>
        /// Broadcasts in the channel asking for the pack number of the episode we're downloading.
        /// </summary>
        /// <param name="s"></param>
        void RequestPackNumber(Show s)
        {
            client.LocalUser.NoticeReceived += AcceptPackNumber;
            client.LocalUser.SendMessage(settings.Channel, "@find " + s.Name + " " + GetSearchableString(s.NextEpisode) + " " + settings.Resolution);

            // If we don't find the pack number from our preferred bot in less than a certain amount of time, either download a non preferred pack or throw an error
            searchTimeoutTokenSource = new CancellationTokenSource();
            var token = searchTimeoutTokenSource.Token;
            searchTimeoutTask = Task.Factory.StartNew(() =>
           {
               Thread.Sleep(settings.SearchTimeout * 1000);

               // If we got canceled, don't worry about setting errors.
               if (token.IsCancellationRequested)
                   return;

               lock (nextShow)
               {
                   if (nextShow.Status.Equals("Searching")) // If we're still searching.
                   {
                       // Remove the handler
                       client.LocalUser.NoticeReceived -= AcceptPackNumber;
        
                       // We didn't find a pack from our preferred bot. Check if there are any alternatives.
                       if (NonPreferredPacks.Count > 0)
                       {
                           // If there are, start downloading the first one we found.
                           RequestDownloadPack(NonPreferredPacks[0]);
                           NonPreferredPacks.Clear();
                           return;
                       }

                       // Otherwise, show a popup describing the problem 
                       Task.Factory.StartNew(() => MessageBox.Show("Unable to find pack number for " + nextShow.Name + " episode " + nextShow.NextEpisode));
                       downloadError = true;
                       Monitor.Pulse(nextShow);
                   }
               }
           }, searchTimeoutTokenSource.Token);
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
                string text = e.Text.ToLower();

                // If the response is for the show we're looking for, and we haven't already started downloading the show
                if (nextShow.Status == "Searching" && text.Contains(nextShow.Name.ToLower()) && text.Contains(" " + GetSearchableString(nextShow.NextEpisode) + " "))
                {

                    Pack nextPack = new Pack();
                    nextPack.Show = nextShow;
                    int packStart = text.IndexOf('#');
                    int packEnd = packStart + 1;
                    while (packEnd + 1 < text.Length && char.IsDigit(text.ElementAt(packEnd + 1)))
                        ++packEnd;

                    nextPack.Number = text.Substring(packStart + 1, packEnd - packStart);

                    // Extract the target bots name. Almost all messages contain "/msg [botname]"
                    int tagStart = text.IndexOf("/msg ");
                    int botStart = tagStart + 5;
                    int botEnd = text.IndexOf(' ', botStart);
                    if (tagStart != -1)
                        nextPack.Target = text.Substring(botStart, botEnd - botStart);
                    else // If the message doesn't contain /msg, use the bot's nickname
                        nextPack.Target = e.Source.ToString();

                    // If the target bot of this pack is our preferred bot, start the download immediately.
                    if (nextPack.Target.Equals(settings.PreferredBot.ToLower())) {
                        // Clear the list of packs we might have accumulated
                        NonPreferredPacks.Clear();
                        RequestDownloadPack(nextPack);
                        client.LocalUser.NoticeReceived -= AcceptPackNumber;
                    }
                    else
                    {
                        // Otherwise, add the pack to the list of packs we know about
                        NonPreferredPacks.Add(nextPack);
                    }

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

            // If we don't get a download request in less than a certain amount of time, throw an error
            downloadTimeoutTokenSource = new CancellationTokenSource();
            var token = downloadTimeoutTokenSource.Token;
            downloadTimeoutTask = Task.Factory.StartNew(() =>
           {
               Thread.Sleep(settings.SearchTimeout * 1000);

               // If we got canceled, don't worry about setting errors.
               if (token.IsCancellationRequested)
                   return;

               lock (nextShow)
               {
                   if (nextShow.Status.Equals("Requesting")) // If we're still waiting for the download to start
                   {
                       // Remove the handler
                       ctcp.RawMessageReceived -= AcceptDownloadRequest;
        
                       // Otherwise, show a popup describing the problem 
                       Task.Factory.StartNew(() => MessageBox.Show("Bot did not respond to XDCC SEND request for " + nextShow.Name + " episode " + nextShow.NextEpisode));
                       downloadError = true;
                       Monitor.Pulse(nextShow);
                   }
               }
           }, downloadTimeoutTokenSource.Token);

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
                downloadTaskTokenSource = new CancellationTokenSource();
                var token = downloadTaskTokenSource.Token;
                downloadTask = Task.Factory.StartNew(() =>
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

                   try
                   {
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

                           // If we get cancelled, close the sock and quit.
                           if (token.IsCancellationRequested)
                           {
                               sock.Close();
                               break;
                           }

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
               });

            }
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

            // Check that the necessary settings have been filled out
            if (ShowCollection.Count == 0)
            {
                MessageBox.Show("Add some shows first!");
                return; 
            }
            if (settings.Nick == "")
            {
                MessageBox.Show("Please enter a nickname in the settings menu.");
                return;
            }
            if (!Directory.Exists(settings.DownloadsFolder))
            {
                MessageBox.Show("Please enter a valid folder to download to in the settings menu.");
                return;
            }

            // Refresh show statuses in case we had an error last time.
            foreach (Show s in ShowCollection)
                UpdateShowStatus(s);

            // If we're already connected, go ahead and start searching for packs
            if (client.IsConnected)
            {
                DownloadAvailableShows(null, null);
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

        /// <summary>
        /// Called when the "Settings" button is clicked.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void Settings_Button_Click(object sender, RoutedEventArgs e)
		{
            SettingsWindow win = new SettingsWindow();
            win.Owner = this;
            win.Show();
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
        /// Called when the "Cancel" button is clicked
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void Cancel_Button_Click(object sender, RoutedEventArgs e)
        {
            if (!downloading)
            {
                MessageBox.Show("You can't cancel because I'm not downloading anything.");
                return;
            }

            StopCurrentDownload(CancelOrSkip.Cancel);
        }

        /// <summary>
        /// Called when the "Skip" button is clicked. Skips downloading the current episode
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void Skip_Button_Click(object sender, RoutedEventArgs e)
        {
            if (!downloading)
            {
                MessageBox.Show("You can't skip because I'm not downloading anything.");
                return;
            }

            StopCurrentDownload(CancelOrSkip.Skip);
        }

        enum CancelOrSkip
        {
            Cancel,
            Skip
        }
        private void StopCurrentDownload(CancelOrSkip cos)
        {
            // If we're canceling, cancel the main task. 
            if (cos == CancelOrSkip.Cancel && mainTaskTokenSource != null)
                mainTaskTokenSource.Cancel();
            // Set the skip flag
            else if (cos == CancelOrSkip.Skip)
                skipped = true;

            // Remove the message and download handlers
            client.LocalUser.NoticeReceived -= AcceptPackNumber;
            ctcp.RawMessageReceived -= AcceptDownloadRequest;
            // Cancel the search timeout task
            if (searchTimeoutTokenSource != null)
                searchTimeoutTokenSource.Cancel();
            // Cancel the download timeout task
            if (downloadTimeoutTokenSource != null)
                downloadTimeoutTokenSource.Cancel();
            // Cancel the download handler
            if (downloadTaskTokenSource != null)
                downloadTaskTokenSource.Cancel();

            // Bump the main thread to move on.
            lock (nextShow)
            {
                Monitor.Pulse(nextShow);
            }
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

        string GetSearchableString(int episodeNumber)
        {
            return (episodeNumber > 9) ? episodeNumber.ToString() : "0" + episodeNumber.ToString();
        }
    }

}
