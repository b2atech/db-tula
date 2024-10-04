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
    public partial class SecretDialog : Window
    {
        public SecretDialog()
        {
            InitializeComponent();
        }

        // Encrypt button click handler
        private void EncryptButton_Click(object sender, RoutedEventArgs e)
        {
            string inputText = InputTextBox.Text;

            if (!string.IsNullOrEmpty(inputText))
            {
                string encryptedText = Encrypt(inputText);
                EncryptedTextBox.Text = encryptedText;
            }
            else
            {
                MessageBox.Show("Please enter text to encrypt.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // Simple encryption method using Base64 encoding for demonstration
        private string Encrypt(string plainText)
        {
            return EncryptionHelper.EncryptString(plainText);
        }
    }
}
