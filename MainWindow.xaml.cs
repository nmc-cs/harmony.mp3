using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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

        private void TimelineSlider_DragStarted(object sender, DragStartedEventArgs e)
        {
            _isDraggingSlider = true;
        }

        private void TimelineSlider_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (_isDraggingSlider)
            {
                // Get the new slider value and seek to that position
                var slider = sender as Slider;
                if (slider != null)
                {
                    double sliderValue = slider.Value;
                    _viewModel.SeekCommand.Execute(sliderValue);
                }
                _isDraggingSlider = false;
            }
        }
    }
}