using SlamLogic.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace Slam__Mix_Marathon.Controls
{
    public sealed partial class MediaPlayer : UserControl
    {
        public MediaPlayerViewModel ViewModel { get; private set; }

        public MediaPlayer()
        {
            InitializeComponent();
            ViewModel = MediaPlayerViewModel.instance;
            DataContext = ViewModel;
        }

        private void PreviousButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.Previous();
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.Stop();
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.PlayPause();
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.Next();
        }
    }
}
