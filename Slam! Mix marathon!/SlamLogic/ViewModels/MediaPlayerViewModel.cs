using BaseLogic;
using SlamLogic.BackgroundAudioTaskSharing;
using SlamLogic.BackgroundAudioTaskSharing.Messages;
using SlamLogic.DataHandlers;
using SlamLogic.Model;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.Foundation.Collections;
using Windows.Media;
using Windows.Media.Playback;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace SlamLogic.ViewModels
{
    public partial class MediaPlayerViewModel : ViewModel
    {
        public static readonly MediaPlayerViewModel instance = new MediaPlayerViewModel();
        private static DispatcherTimer UpdateTimer = null;

        public Visibility StopButtonVisibility { get; private set; }
        public Visibility PlayButtonVisibility { get; private set; }

        public bool PlayButtonIsEnabled { get; private set; }
        public bool PreviousButtonIsEnabled { get; private set; }
        public bool NextButtonIsEnabled { get; private set; }

        public Mix[] TrackQueue { get; private set; }
        public Mix CurrentTrack { get; private set; }
        public string Position { get; private set; }

        public bool UpdateBindings { get; private set; }

        //BackgroundMediaPlayer:
        private AutoResetEvent backgroundAudioTaskStarted;
        private bool _isMyBackgroundTaskRunning = false;
        private Dictionary<string, BitmapImage> albumArtCache = new Dictionary<string, BitmapImage>();
        const int RPC_S_SERVER_UNAVAILABLE = -2147023174; // 0x800706BA

        private MediaPlayerViewModel() : base()
        {
            AddMediaPlayerEventHandlers();
            PlayButtonIsEnabled = false;
            PreviousButtonIsEnabled = false;
            NextButtonIsEnabled = false;

            StopButtonVisibility = Visibility.Collapsed;
            PlayButtonVisibility = Visibility.Visible;

            // Setup the initialization lock
            backgroundAudioTaskStarted = new AutoResetEvent(false);

            // Adding App suspension handlers here so that we can unsubscribe handlers 
            // that access BackgroundMediaPlayer events
            Application.Current.Suspending += ForegroundApp_Suspending;
            Application.Current.Resuming += ForegroundApp_Resuming;
            Application.Current.UnhandledException += Current_UnhandledException;
            ApplicationSettingsHelper.SaveSettingsValue(ApplicationSettingsConstants.AppState, AppState.Active.ToString());


            Task.Run(async () =>
            {
                if (MainpageViewModel.instance.GetMixesTask != null)
                {
                    await MainpageViewModel.instance.GetMixesTask;
                }

                StartBackgroundAudioTask();

                backgroundAudioTaskStarted.Set();
                RefreshBindings();

                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                        UpdateTimer = new DispatcherTimer();
                        UpdateTimer.Interval = TimeSpan.FromMilliseconds(500);
                        UpdateTimer.Tick += delegate { UpdateTimerPosition(); };
                        UpdateTimer.Start();
                });

                MessageService.SendMessageToBackground(new UpdatePlaylistMessage(true));

            });
        }

        public async Task UpdateTrackQueue()
        {
            TrackQueue = await MixDataHandler.instance.GetMixes(true);

            if (CurrentTrack != null && CurrentTrack.Playing)
            {
                CurrentTrack = TrackQueue.Where(x => x.InternalID == CurrentTrack.InternalID).FirstOrDefault();

                if (CurrentTrack != null)
                {
                    CurrentTrack.Playing = true;
                }
            }
        }

        private void UpdateTimerPosition()
        {
            try
            {
                Position = CurrentPlayer.Position.ToString(@"mm\:ss");
                NotifyPropertyChanged("Position");
            }
            catch
            {

            }
        }

        private void RefreshBindings()
        {
            NotifyPropertyChanged("TrackQueue");
            NotifyPropertyChanged("CurrentTrack");
            NotifyPropertyChanged("PlayButtonIsEnabled");
            NotifyPropertyChanged("PreviousButtonIsEnabled");
            NotifyPropertyChanged("NextButtonIsEnabled");
            NotifyPropertyChanged("StopButtonVisibility");
            NotifyPropertyChanged("PlayButtonVisibility");
        }

        public void CheckIfBackgroundTaskIsRunning()
        {
            if (CurrentTrack != null)
            {
                MessageService.SendMessageToBackground(new UpdatePlaylistMessage());
            }
        }

        public async Task PlayMix(Mix CurrentTrack)
        {
            if (this.CurrentTrack != null)
            {
                this.CurrentTrack.Playing = false;
            }

            this.CurrentTrack = CurrentTrack;
            TrackQueue = MainpageViewModel.instance.Mixes;
            this.CurrentTrack.Playing = true;

            await Task.Run(() =>
            {
                MessageService.SendMessageToBackground(new TrackChangedMessage(CurrentTrack.InternalID));
            });

            SetNavigationButtonsState();
        }

        public void PlayPause()
        {
            Debug.WriteLine("Play button pressed from App");
            if (IsMyBackgroundTaskRunning)
            {
                if (MediaPlayerState.Playing == CurrentPlayer.CurrentState)
                {
                    CurrentPlayer.Pause();
                }
                else if (MediaPlayerState.Paused == CurrentPlayer.CurrentState)
                {
                    CurrentPlayer.Play();
                }
                else if (MediaPlayerState.Closed == CurrentPlayer.CurrentState)
                {
                    StartBackgroundAudioTask();
                }
            }
            else
            {
                StartBackgroundAudioTask();
            }

            SetNavigationButtonsState();
        }

        public void Stop()
        {
            if (CurrentTrack != null)
            {
                CurrentPlayer.Pause();
                CurrentTrack.Playing = false;
                BackgroundMediaPlayer.Current.Pause();
                PlayButtonVisibility = Visibility.Visible;
                StopButtonVisibility = Visibility.Collapsed;
                PlayButtonIsEnabled = false;
                RefreshBindings();
            }
        }

        public void Next()
        {
            MessageService.SendMessageToBackground(new SkipNextMessage());

            NextButtonIsEnabled = false;
            RefreshBindings();
        }

        public void Previous()
        {
            MessageService.SendMessageToBackground(new SkipPreviousMessage());

            PreviousButtonIsEnabled = false;
            RefreshBindings();
        }

        private void SetNavigationButtonsState()
        {
            int CurrentTrackIndex = GetIndexOfCurrentTrack();

            PreviousButtonIsEnabled = (CurrentTrackIndex != 0);
            NextButtonIsEnabled = (CurrentTrackIndex != TrackQueue.Length - 1);

            PlayButtonVisibility = Visibility.Collapsed;
            StopButtonVisibility = Visibility.Visible;
            PlayButtonIsEnabled = CurrentTrack != null;
            RefreshBindings();

        }

        private int GetIndexOfCurrentTrack()
        {
            if (CurrentTrack == null)
            {
                return 0;
            }

            return TrackQueue.ToList().FindIndex(m => m.InternalID == CurrentTrack.InternalID);
        }
    }
}
