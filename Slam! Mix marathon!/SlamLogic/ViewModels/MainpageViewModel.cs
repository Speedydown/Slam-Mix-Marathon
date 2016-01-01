using BaseLogic;
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
            Mixes =  await MixDataHandler.instance.GetMixes(false);

            foreach (Mix m in Mixes)
            {
                System.Diagnostics.Debug.WriteLine(typeof(MainpageViewModel).Name + " - " +  m.Date + " " + m.StartTime + " " + m.MP3URL);
            }

            await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                IsLoading = false;
                CurrentMix = Mixes.FirstOrDefault();
                NotifyPropertyChanged("Mixes");
            });

            
        }

    }
}
