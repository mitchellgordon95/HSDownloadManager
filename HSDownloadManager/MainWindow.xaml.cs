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
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace HSDownloadManager
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{

		public MainWindow()
		{
			InitializeComponent();

		}

		/// <summary>
		/// Called when the "Add Show" button is clicked.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		void Add_Button_Click(object sender, RoutedEventArgs e) {
			grid.RowDefinitions.Add(new RowDefinition());
			TextBlock txt1 = new TextBlock();
			txt1.Text = "test";
			Grid.SetRow(txt1, grid.RowDefinitions.Count - 1);
			grid.Children.Add(txt1);
		}

	}

}
