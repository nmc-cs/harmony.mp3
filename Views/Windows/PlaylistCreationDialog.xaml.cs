using System.Windows;

namespace Harmony.Views.Windows
{
    public partial class PlaylistCreationDialog : Window
    {
        public string PlaylistName { get; private set; } = string.Empty;

        public PlaylistCreationDialog()
        {
            InitializeComponent();
            PlaylistNameTextBox.Focus();
        }

        private void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(PlaylistNameTextBox.Text))
            {
                MessageBox.Show("Please enter a playlist name.", "Invalid Input",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            PlaylistName = PlaylistNameTextBox.Text.Trim();
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}