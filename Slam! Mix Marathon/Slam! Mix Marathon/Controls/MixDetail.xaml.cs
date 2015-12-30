using SlamLogic.DataHandlers;
using SlamLogic.Model;
using SlamLogic.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Media;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The User Control item template is documented at http://go.microsoft.com/fwlink/?LinkId=234236

namespace Slam__Mix_Marathon.Controls
{
    public sealed partial class MixDetail : UserControl
    {
        public MixDetail()
        {
            this.InitializeComponent();
        }

        private async void UserControl_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
        {

        }

        private void AdaptiveStates_CurrentStateChanged(object sender, VisualStateChangedEventArgs e)
        {

        }

        private void ADControl_AdMediatorFilled(object sender, Microsoft.AdMediator.Core.Events.AdSdkEventArgs e)
        {

        }

        private async void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext != null && DataContext is Mix)
            {
                await MediaPlayerViewModel.instance.PlayMix(DataContext as Mix);
            }
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext != null && DataContext is Mix)
            {
                Mix CurrentMix = DataContext as Mix;

                try
                {
                    await CurrentMix.Download();
                }
                catch (NullReferenceException)
                {

                }
            }
        }

        private void HideButton_Click(object sender, RoutedEventArgs e)
        {

        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext != null && DataContext is Mix)
            {
                Mix CurrentMix = DataContext as Mix;

                try
                {
                    await CurrentMix.Delete();
                }
                catch (NullReferenceException)
                {

                }
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (DataContext != null && DataContext is Mix)
                {
                    Mix CurrentMix = DataContext as Mix;
                    CurrentMix.CancelDownload();
                }
            }
            catch
            {

            }
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext != null && DataContext is Mix)
                MixDataHandler.instance.UpdateMix(DataContext as Mix);
        }
    }
}
