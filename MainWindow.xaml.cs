﻿using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Harmony.Models;
using Harmony.ViewModels;

namespace Harmony
{
    public partial class MainWindow : Window
    {
        private MainViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();

            // Initialize the ViewModel and set it as DataContext
            _viewModel = new MainViewModel();
            DataContext = _viewModel;
        }

        private void TimelineSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Get the slider control
            Slider slider = sender as Slider;
            if (slider != null)
            {
                // Get the position of the mouse click relative to the slider
                Point mousePosition = e.GetPosition(slider);

                // Calculate the proportion of the width
                double proportion = mousePosition.X / slider.ActualWidth;

                // Calculate the value based on the slider range
                double sliderValue = proportion * slider.Maximum;

                // Set the slider value and seek to that position
                slider.Value = sliderValue;
                _viewModel.SeekCommand.Execute(sliderValue);
            }
        }

        private void PlaylistListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Update the SelectedTracks collection in the ViewModel
            var listBox = sender as ListBox;
            if (listBox != null)
            {
                var selectedItems = new ObservableCollection<AudioFile>();
                foreach (AudioFile item in listBox.SelectedItems)
                {
                    selectedItems.Add(item);
                }
                _viewModel.SelectedTracks = selectedItems;
            }
        }
    }
}