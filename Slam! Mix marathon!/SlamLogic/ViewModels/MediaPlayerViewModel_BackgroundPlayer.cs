using SlamLogic.BackgroundAudioTaskSharing;
using SlamLogic.BackgroundAudioTaskSharing.Messages;
using SlamLogic.DataHandlers;
using SlamLogic.Model;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.Foundation;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace SlamLogic.ViewModels
{
    public partial class MediaPlayerViewModel
    {
        #region Foreground App Lifecycle Handlers
        /// <summary>
        /// Read persisted current track information from application settings
        /// </summary>
        private int GetCurrentTrackIdAfterAppResume()
        {
            object value = ApplicationSettingsHelper.ReadResetSettingsValue(ApplicationSettingsConstants.TrackId);
            if (value != null)
                return (int)value;
            else
                return 0;
        }

        /// <summary>
        /// Sends message to background informing app has resumed
        /// Subscribe to MediaPlayer events
        /// </summary>
        void ForegroundApp_Resuming(object sender, object e)
        {
            ApplicationSettingsHelper.SaveSettingsValue(ApplicationSettingsConstants.AppState, AppState.Active.ToString());

            // Verify the task is running
            if (IsMyBackgroundTaskRunning)
            {
                // If yes, it's safe to reconnect to media play handlers
                AddMediaPlayerEventHandlers();

                // Send message to background task that app is resumed so it can start sending notifications again
                MessageService.SendMessageToBackground(new AppResumedMessage());

                UpdateTransportControls(CurrentPlayer.CurrentState);

                int trackId = GetCurrentTrackIdAfterAppResume();

                CurrentTrack = trackId == 0 ? null : TrackQueue.Single(s => s.InternalID == trackId);
            }
            else
            {
                StopButtonVisibility = Visibility.Collapsed;
                PlayButtonVisibility = Visibility.Visible;
                PlayButtonIsEnabled = true;
            }

            RefreshBindings();
        }

        private void Current_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            BackgroundMediaPlayer.Shutdown();
            _isMyBackgroundTaskRunning = false;

            // save state
            ApplicationSettingsHelper.SaveSettingsValue(ApplicationSettingsConstants.TrackId, 0);
            ApplicationSettingsHelper.SaveSettingsValue(ApplicationSettingsConstants.Position, BackgroundMediaPlayer.Current.Position.ToString());
            ApplicationSettingsHelper.SaveSettingsValue(ApplicationSettingsConstants.BackgroundTaskState, BackgroundTaskState.Canceled.ToString());
            ApplicationSettingsHelper.SaveSettingsValue(ApplicationSettingsConstants.AppState, Enum.GetName(typeof(AppState), AppState.Unknown));
        }

        /// <summary>
        /// Send message to Background process that app is to be suspended
        /// Stop clock and slider when suspending
        /// Unsubscribe handlers for MediaPlayer events
        /// </summary>
        void ForegroundApp_Suspending(object sender, Windows.ApplicationModel.SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();

            // Only if the background task is already running would we do these, otherwise
            // it would trigger starting up the background task when trying to suspend.
            if (IsMyBackgroundTaskRunning)
            {
                // Stop handling player events immediately
                RemoveMediaPlayerEventHandlers();

                // Tell the background task the foreground is suspended
                MessageService.SendMessageToBackground(new AppSuspendedMessage());
            }

            // Persist that the foreground app is suspended
            ApplicationSettingsHelper.SaveSettingsValue(ApplicationSettingsConstants.AppState, AppState.Suspended.ToString());
            ApplicationSettingsHelper.SaveSettingsValue(ApplicationSettingsConstants.BackgroundTaskState, BackgroundTaskState.Running.ToString());

            deferral.Complete();
        }
        #endregion

        #region Background MediaPlayer Event handlers
        /// <summary>
        /// MediaPlayer state changed event handlers. 
        /// Note that we can subscribe to events even if Media Player is playing media in background
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        async void MediaPlayer_CurrentStateChanged(MediaPlayer sender, object args)
        {
            var currentState = sender.CurrentState; // cache outside of completion or you might get a different value
            await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                // Update controls
                UpdateTransportControls(currentState);
            });
        }

        /// <summary>
        /// This event is raised when a message is recieved from BackgroundAudioTask
        /// </summary>
        async void BackgroundMediaPlayer_MessageReceivedFromBackground(object sender, MediaPlayerDataReceivedEventArgs e)
        {
            TrackChangedMessage trackChangedMessage;
            if (MessageService.TryParseMessage(e.Data, out trackChangedMessage))
            {
                // When foreground app is active change track based on background message
                await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    // If playback stopped then clear the UI
                    if (trackChangedMessage.InternalMixID == 0)
                    {
                        Stop();
                        RefreshBindings();

                        return;
                    }

                    CurrentTrack = TrackQueue.Single(s => s.InternalID == trackChangedMessage.InternalMixID);

                    // Ensure track buttons are re-enabled since they are disabled when pressed
                    foreach (Mix m in TrackQueue.Where(m => m.Playing))
                    {
                        m.Playing = false;
                    }

                    CurrentTrack.Playing = true;

                    NextButtonIsEnabled = true;
                    PreviousButtonIsEnabled = true;
                    RefreshBindings();
                });

                return;
            }

            BackgroundAudioTaskStartedMessage backgroundAudioTaskStartedMessage;
            if (MessageService.TryParseMessage(e.Data, out backgroundAudioTaskStartedMessage))
            {
                // StartBackgroundAudioTask is waiting for this signal to know when the task is up and running
                // and ready to receive messages
                Debug.WriteLine("BackgroundAudioTask started");
                //backgroundAudioTaskStarted.Set();
                //RefreshBindings();
                //await Task.Run(async () =>
                //{
                //    await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                //    {
                //        UpdateTimer = new DispatcherTimer();
                //        UpdateTimer.Interval = TimeSpan.FromMilliseconds(500);
                //        UpdateTimer.Tick += delegate { UpdateTimerPosition(); };
                //        UpdateTimer.Start();
                //    });
                //});
                return;
            }
        }

        #endregion

        #region Media Playback Helper methods
        private void UpdateTransportControls(MediaPlayerState state)
        {
            if (state == MediaPlayerState.Opening || state == MediaPlayerState.Buffering)
            {
                return;
            }

            if (state == MediaPlayerState.Playing)
            {
                CurrentTrack.Playing = true;
                PlayButtonVisibility = Visibility.Collapsed;
                StopButtonVisibility = Visibility.Visible;
            }
            else
            {
                CurrentTrack.Playing = false;
                PlayButtonVisibility = Visibility.Visible;
                StopButtonVisibility = Visibility.Collapsed;
            }

            PlayButtonIsEnabled = true;
            RefreshBindings();
        }

        /// <summary>
        /// Unsubscribes to MediaPlayer events. Should run only on suspend
        /// </summary>
        private void RemoveMediaPlayerEventHandlers()
        {
            try
            {
                BackgroundMediaPlayer.Current.CurrentStateChanged -= this.MediaPlayer_CurrentStateChanged;
                BackgroundMediaPlayer.MessageReceivedFromBackground -= BackgroundMediaPlayer_MessageReceivedFromBackground;
            }
            catch (Exception ex)
            {
                if (ex.HResult == RPC_S_SERVER_UNAVAILABLE)
                {
                    // do nothing
                }
                else
                {
                    throw;
                }
            }
        }

        /// <summary>
        /// Subscribes to MediaPlayer events
        /// </summary>
        private void AddMediaPlayerEventHandlers()
        {
            CurrentPlayer.CurrentStateChanged += this.MediaPlayer_CurrentStateChanged;

            try
            {
                BackgroundMediaPlayer.MessageReceivedFromBackground += BackgroundMediaPlayer_MessageReceivedFromBackground;
            }
            catch (Exception ex)
            {
                if (ex.HResult == RPC_S_SERVER_UNAVAILABLE)
                {
                    // Internally MessageReceivedFromBackground calls Current which can throw RPC_S_SERVER_UNAVAILABLE
                    ResetAfterLostBackground();
                }
                else
                {
                    throw;
                }
            }
        }

        /// <summary>
        /// Initialize Background Media Player Handlers and starts playback
        /// </summary>
        private void StartBackgroundAudioTask()
        {
            AddMediaPlayerEventHandlers();

            var startResult = Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                bool result = backgroundAudioTaskStarted.WaitOne(10000);
                //Send message to initiate playback
                //if (result == true)
                //{
                MessageService.SendMessageToBackground(new UpdatePlaylistMessage());
                MessageService.SendMessageToBackground(new StartPlaybackMessage((CurrentTrack != null ? CurrentTrack.InternalID : 0)));
                //}
                //else
                //{
                //    //Asume the mediaplayer is allready running.
                //    //throw new Exception("Background Audio Task didn't start in expected time");
                //}
            });
            startResult.Completed = new AsyncActionCompletedHandler(BackgroundTaskInitializationCompleted);
        }

        private void BackgroundTaskInitializationCompleted(IAsyncAction action, AsyncStatus status)
        {
            if (status == AsyncStatus.Completed)
            {
                Debug.WriteLine("Background Audio Task initialized");
            }
            else if (status == AsyncStatus.Error)
            {
                Debug.WriteLine("Background Audio Task could not initialized due to an error ::" + action.ErrorCode.ToString());
            }
        }
        #endregion

        //protected override void OnNavigatedFrom(NavigationEventArgs e)
        //{
        //    if (_isMyBackgroundTaskRunning)
        //    {
        //        RemoveMediaPlayerEventHandlers();
        //        ApplicationSettingsHelper.SaveSettingsValue(ApplicationSettingsConstants.BackgroundTaskState, BackgroundTaskState.Running.ToString());
        //    }

        //    base.OnNavigatedFrom(e);
        //}

        /// <summary>
        /// Gets the information about background task is running or not by reading the setting saved by background task.
        /// This is used to determine when to start the task and also when to avoid sending messages.
        /// </summary>
        public bool IsMyBackgroundTaskRunning
        {
            get
            {
                if (_isMyBackgroundTaskRunning)
                    return true;

                string value = ApplicationSettingsHelper.ReadResetSettingsValue(ApplicationSettingsConstants.BackgroundTaskState) as string;
                if (value == null)
                {
                    return false;
                }
                else
                {
                    try
                    {
                        _isMyBackgroundTaskRunning = EnumHelper.Parse<BackgroundTaskState>(value) == BackgroundTaskState.Running;
                    }
                    catch (ArgumentException)
                    {
                        _isMyBackgroundTaskRunning = false;
                    }
                    return _isMyBackgroundTaskRunning;
                }
            }
        }

        /// You should never cache the MediaPlayer and always call Current. It is possible
        /// for the background task to go away for several different reasons. When it does
        /// an RPC_S_SERVER_UNAVAILABLE error is thrown. We need to reset the foreground state
        /// and restart the background task.
        /// </summary>
        private MediaPlayer CurrentPlayer
        {
            get
            {
                MediaPlayer mp = null;
                int retryCount = 2;

                while (mp == null && --retryCount >= 0)
                {
                    try
                    {
                        mp = BackgroundMediaPlayer.Current;
                    }
                    catch (Exception ex)
                    {
                        if (ex.HResult == RPC_S_SERVER_UNAVAILABLE)
                        {
                            // The foreground app uses RPC to communicate with the background process.
                            // If the background process crashes or is killed for any reason RPC_S_SERVER_UNAVAILABLE
                            // is returned when calling Current. We must restart the task, the while loop will retry to set mp.
                            ResetAfterLostBackground();
                            StartBackgroundAudioTask();
                        }
                        else
                        {
                            throw;
                        }
                    }
                }

                if (mp == null)
                {
                    throw new Exception("Failed to get a MediaPlayer instance.");
                }

                return mp;
            }
        }

        /// <summary>
        /// The background task did exist, but it has disappeared. Put the foreground back into an initial state. Unfortunately,
        /// any attempts to unregister things on BackgroundMediaPlayer.Current will fail with the RPC error once the background task has been lost.
        /// </summary>
        private void ResetAfterLostBackground()
        {
            BackgroundMediaPlayer.Shutdown();
            _isMyBackgroundTaskRunning = false;
            backgroundAudioTaskStarted.Reset();
            PreviousButtonIsEnabled = true;
            NextButtonIsEnabled = true;
            StopButtonVisibility = Visibility.Collapsed;
            PlayButtonVisibility = Visibility.Visible;
            PlayButtonIsEnabled = true;
            RefreshBindings();

            ApplicationSettingsHelper.SaveSettingsValue(ApplicationSettingsConstants.BackgroundTaskState, BackgroundTaskState.Unknown.ToString());

            try
            {
                BackgroundMediaPlayer.MessageReceivedFromBackground += BackgroundMediaPlayer_MessageReceivedFromBackground;
            }
            catch (Exception ex)
            {
                if (ex.HResult == RPC_S_SERVER_UNAVAILABLE)
                {
                    throw new Exception("Failed to get a MediaPlayer instance.");
                }
                else
                {
                    throw;
                }
            }
        }


    }
}
