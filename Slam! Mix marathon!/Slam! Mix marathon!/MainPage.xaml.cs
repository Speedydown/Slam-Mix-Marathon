using Slam__Mix_marathon_;
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
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Navigation;

//https://github.com/Microsoft/Windows-universal-samples/blob/master/Samples/XamlMasterDetail/cs/MasterDetailPage.xaml.cs

namespace Slam__Mix_Marathon
{
    public sealed partial class MainPage : Page
    {
        public MainpageViewModel ViewModel { get; private set; }

        public MainPage()
        {
            this.InitializeComponent();

            NavigationCacheMode = NavigationCacheMode.Required;
            SystemNavigationManager.GetForCurrentView().AppViewBackButtonVisibility = AppViewBackButtonVisibility.Collapsed;

            SystemNavigationManager.GetForCurrentView().BackRequested += (s, e) =>
            {
                Frame.Navigate(typeof(MainPage));
                e.Handled = true;
            };
        }

        protected async override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            UpdateForVisualState(AdaptiveStates.CurrentState);
            DisableContentTransitions();

            if (DataContext == null)
            {
                ViewModel = MainpageViewModel.instance;
                DataContext = ViewModel;

                if (ViewModel.GetMixesTask != null && !ViewModel.GetMixesTask.IsCompleted)
                {
                    await ViewModel.GetMixesTask;
                }
            }

            MasterListView.SelectedItem = ViewModel.CurrentMix;
            SortingComboBox.SelectedIndex = ViewModel.CurrentSortingState;
        }

        private void AdaptiveStates_CurrentStateChanged(object sender, VisualStateChangedEventArgs e)
        {
            UpdateForVisualState(e.NewState, e.OldState);
        }

        private void UpdateForVisualState(VisualState newState, VisualState oldState = null)
        {
            var isNarrow = newState == NarrowState;

            if (isNarrow && oldState == DefaultState && ViewModel.CurrentMix != null)
            {
                // Resize down to the detail item. Don't play a transition.
                Frame.Navigate(typeof(MixDetailPage), ViewModel.CurrentMix.InternalID, new SuppressNavigationTransitionInfo());
            }

            if (!isNarrow && oldState == NarrowState && ViewModel.CurrentMix != null)
            {
                MasterListView.SelectedItem = ViewModel.CurrentMix;
            }

            EntranceNavigationTransitionInfo.SetIsTargetElement(MasterListView, isNarrow);
            if (DetailContentPresenter != null)
            {
                EntranceNavigationTransitionInfo.SetIsTargetElement(DetailContentPresenter, !isNarrow);
            }
        }

        private void MasterListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            ViewModel.CurrentMix = (Mix)e.ClickedItem;
            ViewModel.UpdateBindings();

            if (AdaptiveStates.CurrentState == NarrowState)
            {
                // Use "drill in" transition for navigating from master list to detail view
                Frame.Navigate(typeof(MixDetailPage), ViewModel.CurrentMix.InternalID, new DrillInNavigationTransitionInfo());
            }
            else
            {
                // Play a refresh animation when the user switches detail items.
                EnableContentTransitions();
            }
        }

        private void LayoutRoot_Loaded(object sender, RoutedEventArgs e)
        {
            //Assure we are displaying the correct item. This is necessary in certain adaptive cases.
            MasterListView.SelectedItem = ViewModel.CurrentMix;
        }

        private void EnableContentTransitions()
        {
            DetailContentPresenter.ContentTransitions.Clear();
            DetailContentPresenter.ContentTransitions.Add(new EntranceThemeTransition());
        }

        private void DisableContentTransitions()
        {
            if (DetailContentPresenter != null)
            {
                DetailContentPresenter.ContentTransitions.Clear();
            }
        }

        private void SortingComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int SelectedIndex = (sender as ComboBox).SelectedIndex;

            ViewModel.OrderMixes(SelectedIndex);
        }

        private async void Grid_Holding(object sender, HoldingRoutedEventArgs e)
        {
            if (e.HoldingState == Windows.UI.Input.HoldingState.Started)
            {
                Mix m = (sender as Grid).DataContext as Mix;

                if (m.Playing)
                {
                    return;
                }

                await MediaPlayerViewModel.instance.PlayMix(m);
            }
        }

        private void PrivacyButton_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(PrivacyPolicy));
        }

        private async void OfflineButton_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.ToggleOfflineMode((sender as ToggleButton).IsChecked.GetValueOrDefault());
        }
    }
}
