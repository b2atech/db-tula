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
    /// Interaction logic for AddNewComparer.xaml
    /// </summary>
    public partial class AddNewComparer : Window
    {
        public string ComparisonName { get; private set; }
        public string SourceConnectionString { get; private set; }
        public string TargetConnectionString { get; private set; }

        public AddNewComparer()
        {
            InitializeComponent();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            ComparisonName = ComparisonNameTextBox.Text;
            SourceConnectionString = SourceConnectionStringTextBox.Text;
            TargetConnectionString = TargetConnectionStringTextBox.Text;
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
