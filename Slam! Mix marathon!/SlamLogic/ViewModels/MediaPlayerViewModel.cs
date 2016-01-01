using BaseLogic;
using SlamLogic.DataHandlers;
using SlamLogic.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.Foundation.Collections;
using Windows.Media;
using Windows.Media.Playback;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace SlamLogic.ViewModels
{
    public class MediaPlayerViewModel : ViewModel
    {
        public static readonly MediaPlayerViewModel instance = new MediaPlayerViewModel();
        private static DispatcherTimer UpdateTimer = null;

        private SystemMediaTransportControls systemMediaControls;

        public Visibility StopButtonVisibility { get; private set; }
        public Visibility PlayButtonVisibility { get; private set; }

        public bool PlayButtonIsEnabled { get; private set; }
        public bool PreviousButtonIsEnabled { get; private set; }
        public bool NextButtonIsEnabled { get; private set; }

        public Mix[] TrackQueue { get; private set; }
        public Mix CurrentTrack { get; private set; }
        public string Position { get; private set; }

        public bool UpdateBindings { get; private set; }

        private MediaPlayerViewModel() : base()
        {
            PlayButtonIsEnabled = false;
            PreviousButtonIsEnabled = false;
            PreviousButtonIsEnabled = false;

            StopButtonVisibility = Visibility.Collapsed;
            PlayButtonVisibility = Visibility.Visible;

            // Hook up app to system transport controls.
            systemMediaControls = SystemMediaTransportControls.GetForCurrentView();
            systemMediaControls.ButtonPressed += SystemControls_ButtonPressed;

            // Register to handle the following system transpot control buttons.
            systemMediaControls.IsPlayEnabled = true;
            systemMediaControls.IsPauseEnabled = true;
            systemMediaControls.IsStopEnabled = true;
            systemMediaControls.IsNextEnabled = true;
            systemMediaControls.IsPreviousEnabled = true;

            BackgroundMediaPlayer.Current.CurrentStateChanged += BackgroundMediaPlayer_CurrentStateChanged;
            BackgroundMediaPlayer.MessageReceivedFromBackground += BackgroundMediaPlayer_MessageReceivedFromBackground;
            BackgroundMediaPlayer.Current.MediaOpened += Current_MediaOpened;
            BackgroundMediaPlayer.Current.MediaEnded += Current_MediaEnded;

            Task.Run(async () =>
            {
                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    UpdateTimer = new DispatcherTimer();
                    UpdateTimer.Interval = TimeSpan.FromMilliseconds(500);
                    UpdateTimer.Tick += delegate { UpdateTimerPosition(); };
                    UpdateTimer.Start();
                });
            });
        }

        private void UpdateTimerPosition()
        {
            Position = BackgroundMediaPlayer.Current.Position.ToString(@"mm\:ss");
            NotifyPropertyChanged("Position");
        }

        private void Current_MediaOpened(MediaPlayer sender, object args)
        {
            DisplayUpdater();
        }

        private async void Current_MediaEnded(MediaPlayer sender, object args)
        {
            await Next();
        }

        private void BackgroundMediaPlayer_MessageReceivedFromBackground(object sender, MediaPlayerDataReceivedEventArgs e)
        {
            if (e.Data.First().Key == "ResetBindings")
            {
                RefreshBindings();
            }
        }

        private async void SystemControls_ButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
        {
            switch (args.Button)
            {
                case SystemMediaTransportControlsButton.Play:
                    Play();
                    break;
                case SystemMediaTransportControlsButton.Pause:
                    Pause();
                    break;
                case SystemMediaTransportControlsButton.Next:
                    await Next();
                    break;
                case SystemMediaTransportControlsButton.Previous:
                    Previous();
                    break;
                case SystemMediaTransportControlsButton.Stop:
                    Stop();
                    break;
                default:
                    break;                    
            }

            ValueSet vs = new ValueSet();
            vs.Add("ResetBindings", "true");

            BackgroundMediaPlayer.SendMessageToForeground(vs);

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

        public async Task PlayMix(Mix CurrentTrack)
        {
            if (this.CurrentTrack != null)
            {
                this.CurrentTrack.Playing = false;
            }

            this.CurrentTrack = CurrentTrack;

            TrackQueue = MainpageViewModel.instance.Mixes;

            if (this.CurrentTrack.Downloaded)
            {
                BackgroundMediaPlayer.Current.SetStreamSource(await (await this.CurrentTrack.GetDownloadedFile()).OpenReadAsync());
            }
            else
            {
                try
                {
                    BackgroundMediaPlayer.Current.SetUriSource(new Uri(this.CurrentTrack.MP3URL));
                }
                catch (FileNotFoundException)
                {

                }
            }
            Play();
        }

        public void Play()
        {
            CurrentTrack.UpdateTimesPlayed();
            CurrentTrack.Playing = true;
            BackgroundMediaPlayer.Current.Play();
            PlayButtonIsEnabled = true;
            SetNavigationButtonsState();
            PlayButtonVisibility = Visibility.Collapsed;
            StopButtonVisibility = Visibility.Visible;
            DisplayUpdater();
            RefreshBindings();
        }

        public void Pause()
        {
            BackgroundMediaPlayer.Current.Pause();
            PlayButtonVisibility = Visibility.Visible;
            StopButtonVisibility = Visibility.Collapsed;
            DisplayUpdater();
            RefreshBindings();
        }

        public void Stop()
        {
            CurrentTrack.Playing = false;
            BackgroundMediaPlayer.Current.Pause();
            PlayButtonVisibility = Visibility.Collapsed;
            StopButtonVisibility = Visibility.Visible;
            PlayButtonIsEnabled = false;
            DisplayUpdater();
            RefreshBindings();
        }

        public async Task Next()
        {
            int CurrentTrackIndex = GetIndexOfCurrentTrack();

            if (CurrentTrackIndex == TrackQueue.Length - 1)
            {
                return;
            }

            if (CurrentTrack != null)
            {
                CurrentTrack.Playing = false;
            }

            CurrentTrack = TrackQueue[CurrentTrackIndex + 1];

            SetNavigationButtonsState();
            DisplayUpdater();
            RefreshBindings();

            if (this.CurrentTrack.Downloaded)
            {
                BackgroundMediaPlayer.Current.SetStreamSource(await(await this.CurrentTrack.GetDownloadedFile()).OpenReadAsync());
            }
            else
            {
                BackgroundMediaPlayer.Current.SetUriSource(new Uri(this.CurrentTrack.MP3URL));
            }

            Play();
        }

        public async Task Previous()
        {
            int CurrentTrackIndex = GetIndexOfCurrentTrack();

            if (CurrentTrackIndex == 0)
            {
                return;
            }

            if (CurrentTrack != null)
            {
                CurrentTrack.Playing = false;
            }

            CurrentTrack = TrackQueue[CurrentTrackIndex - 1];

            SetNavigationButtonsState();
            DisplayUpdater();
            RefreshBindings();

            if (this.CurrentTrack.Downloaded)
            {
                BackgroundMediaPlayer.Current.SetStreamSource(await(await this.CurrentTrack.GetDownloadedFile()).OpenReadAsync());
            }
            else
            {
                BackgroundMediaPlayer.Current.SetUriSource(new Uri(this.CurrentTrack.MP3URL));
            }

            Play();
        }

        private void SetNavigationButtonsState()
        {
            int CurrentTrackIndex = GetIndexOfCurrentTrack();

            PreviousButtonIsEnabled = (CurrentTrackIndex != 0);
            NextButtonIsEnabled = (CurrentTrackIndex != TrackQueue.Length - 1);
        }

        private int GetIndexOfCurrentTrack()
        {
            return TrackQueue.ToList().IndexOf(TrackQueue.Where(t => t.MP3URL == CurrentTrack.MP3URL).First());
        }

        private void DisplayUpdater()
        {
            SystemMediaTransportControlsDisplayUpdater updater = systemMediaControls.DisplayUpdater;
            updater.Type = MediaPlaybackType.Music;
            updater.MusicProperties.Title = CurrentTrack.ShowName;
            updater.MusicProperties.Artist = CurrentTrack.MixSubTitle;

            updater.Update();
        }

        public void BackgroundMediaPlayer_CurrentStateChanged(MediaPlayer sender, object args)
        {
            switch (BackgroundMediaPlayer.Current.CurrentState)
            {
                case MediaPlayerState.Playing:
                    systemMediaControls.PlaybackStatus = MediaPlaybackStatus.Playing;
                    break;
                case MediaPlayerState.Paused:
                    systemMediaControls.PlaybackStatus = MediaPlaybackStatus.Paused;
                    break;
                case MediaPlayerState.Stopped:
                    systemMediaControls.PlaybackStatus = MediaPlaybackStatus.Stopped;
                    break;
                case MediaPlayerState.Closed:
                    systemMediaControls.PlaybackStatus = MediaPlaybackStatus.Closed;
                    break;
                default:
                    break;
            }
        }
    }
}
