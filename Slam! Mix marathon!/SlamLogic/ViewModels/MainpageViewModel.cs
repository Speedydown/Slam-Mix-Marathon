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
        public Mix CurrentMix { get; set; }
        public int CurrentSortingState
        {
            get
            {
                return SettingsDataHandler.instance.GetSettings().SortingIndex;
            }
        }

        public string[] SortingOptions
        {
            get
            {
                return SettingsDataHandler.SortingStates;
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
            });

        }

        public void OrderMixes(int Ordering)
        {
            if (Mixes == null)
            {
                return;
            }

            Settings settings = SettingsDataHandler.instance.GetSettings();
            settings.SortingIndex = Ordering;
            SettingsDataHandler.instance.UpdateSettings(settings);

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

    }
}
