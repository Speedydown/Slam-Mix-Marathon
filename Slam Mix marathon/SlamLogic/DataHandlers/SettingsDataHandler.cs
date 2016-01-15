using BaseLogic.DataHandler;
using SlamLogic.BackgroundAudioTaskSharing.Messages;
using SlamLogic.Model;
using SlamLogic.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SlamLogic.DataHandlers
{
    public sealed class SettingsDataHandler : DataHandler
    {
        public static string[] SortingStates = new string[] {
            "Datum (Aflopend)",
            "Datum (Oplopend)",
            "Rating",
            "Aantal keren afgespeeld",
            "Datum gedownload"};

        public static readonly SettingsDataHandler instance = new SettingsDataHandler();

        private SettingsDataHandler()
        {
            CreateItemTable<Settings>();
        }

        public Settings GetSettings()
        {
            Settings CurrentSettings = GetItem<Settings>(1);

            if (CurrentSettings == null)
            {
                CurrentSettings = new Settings();
                InsertItem(CurrentSettings);
            }

            return CurrentSettings;
        }

        public void UpdateSettings(Settings settings)
        {
            UpdateItem(settings);

            if (MediaPlayerViewModel.instance.IsMyBackgroundTaskRunning)
            {
                MessageService.SendMessageToBackground(new UpdatePlaylistMessage());
            }
        }
    }
}
