using BaseLogic;
using SlamLogic.BackgroundAudioTaskSharing.Messages;
using SlamLogic.DataHandlers;
using SlamLogic.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.UI.Core;

namespace SlamLogic.ViewModels
{
    public class MainpageViewModel : ViewModel
    {
        public static readonly MainpageViewModel instance = new MainpageViewModel();

        public Mix[] Mixes { get; private set; }

        public Task GetMixesTask { get; private set; }

        private Mix _CurrentMix = null;
        public Mix CurrentMix
        {
            get
            {
                return _CurrentMix;
            }
            set
            {
                _CurrentMix = value;
                NotifyPropertyChanged("HasSelectedMix");
                NotifyPropertyChanged("ShowFillerImage");
            }
        }

        public int CurrentSortingState
        {
            get
            {
                return CurrentSettings.SortingIndex;
            }
        }

        public string[] SortingOptions
        {
            get
            {
                return SettingsDataHandler.SortingStates;
            }
        }

        public Settings CurrentSettings
        {
            get
            {
                return SettingsDataHandler.instance.GetSettings();
            }
        }

        public bool NoMixes
        {
            get
            {
                return Mixes != null && Mixes.Count() == 0 && !IsLoading;
            }
        }

        public bool HasSelectedMix
        {
            get
            {
                return CurrentMix != null;
            }
        }

        public bool ShowFillerImage
        {
            get
            {
                return CurrentMix == null && !IsLoading && Mixes.Count() != 0;
            }
        }


        private MainpageViewModel() : base()
        {
            IsLoading = true;

            GetMixesTask = Task.Run(async () =>
            {
                await LoadMixes();
            });
        }

        private async Task LoadMixes()
        {
            Mixes = await MixDataHandler.instance.GetMixes(false);
            OrderMixes(CurrentSortingState);

            await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                IsLoading = false;
                CurrentMix = Mixes.FirstOrDefault();
                NotifyPropertyChanged("NoMixes");
            });

        }

        public void OrderMixes(int Ordering)
        {
            Settings CurrentSettings = this.CurrentSettings;

            if (Mixes == null || Mixes.Count() == 0 || CurrentSettings.SortingIndex == Ordering)
            {
                NotifyPropertyChanged("Mixes");
                return;
            }

            CurrentSettings.SortingIndex = Ordering;
            SettingsDataHandler.instance.UpdateSettings(CurrentSettings);

            switch (Ordering)
            {
                case 0:
                    Mixes = Mixes.OrderByDescending(m => m.RealDate).ThenByDescending(m => m.StartTime).ToArray();
                    break;
                case 1:
                    Mixes = Mixes.OrderBy(m => m.RealDate).ThenBy(m => m.StartTime).ToArray();
                    break;
                case 2:
                    Mixes = Mixes.OrderByDescending(m => m.Rating).ThenBy(m => m.InternalID).ToArray();
                    break;
                case 3:
                    Mixes = Mixes.OrderByDescending(m => m.TimesPlayed).ThenBy(m => m.InternalID).ToArray();
                    break;
                case 4:
                    Mixes = Mixes.OrderByDescending(m => m.TimeDownloaded).ThenBy(m => m.InternalID).ToArray();
                    break;
            }

            NotifyPropertyChanged("Mixes");
            MessageService.SendMessageToBackground(new UpdatePlaylistMessage(true));
        }

        public async Task ToggleOfflineMode(bool OfflineMode)
        {
            if (GetMixesTask != null && !GetMixesTask.IsCompleted)
            {
                await GetMixesTask;
            }

            Settings NewSettings = CurrentSettings;

            NewSettings.OfflineMode = OfflineMode;
            SettingsDataHandler.instance.UpdateSettings(NewSettings);

            await MediaPlayerViewModel.instance.UpdateTrackQueue();

            Mixes = MediaPlayerViewModel.instance.TrackQueue;

            OrderMixes(CurrentSortingState);
            CurrentMix = null;
            UpdateBindings();
        }

        public void UpdateBindings()
        {
            NotifyPropertyChanged("Mixes");
            NotifyPropertyChanged("ShowFillerImage");
            NotifyPropertyChanged("NoMixes");
        }

    }
}
