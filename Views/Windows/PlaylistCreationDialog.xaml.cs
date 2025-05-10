using System.Windows;

namespace Harmony.Views.Windows
{
    public partial class PlaylistCreationDialog : Window
    {
        private string _playlistName = string.Empty;

        public string PlaylistName
        {
            get => _playlistName;
            set
            {
                _playlistName = value;
                // Update textbox if set from outside
                if (PlaylistNameTextBox != null)
                {
                    PlaylistNameTextBox.Text = value;
                }
            }
        }

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