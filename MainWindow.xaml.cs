using System.Windows;
using System.Windows.Input;
using Harmony.ViewModels;

namespace Harmony
{
    public partial class MainWindow : Window
    {
        private MainViewModel _viewModel;
        private bool _isDraggingSlider = false;

        public MainWindow()
        {
            InitializeComponent();

            // Initialize the ViewModel and set it as DataContext
            _viewModel = new MainViewModel();
            DataContext = _viewModel;
        }

        private void TimelineSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _isDraggingSlider = true;
        }

        private void TimelineSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingSlider)
            {
                // Get the new slider value and pass it to the SeekCommand
                double sliderValue = timelineSlider.Value;
                _viewModel.SeekCommand.Execute(sliderValue);
                _isDraggingSlider = false;
            }
        }
    }
}