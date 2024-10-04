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

namespace b2a.db_tula
{
    /// <summary>
    /// Interaction logic for SyncPreviewWindow.xaml
    /// </summary>
    public partial class SyncPreviewWindow : Window
    {
        public SyncPreviewWindow(List<string> syncCommands, ComparisonResult comparisonResult)
        {
            InitializeComponent();
            SyncCommandsTextBox.Text = string.Join(Environment.NewLine, syncCommands); // Show the commands in the TextBox
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true; // Confirm the sync
            this.Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false; // Cancel the sync
            this.Close();
        }
    }
}
