//*********************************************************
//
// Copyright (c) Microsoft. All rights reserved.
// This code is licensed under the MIT License (MIT).
// THIS CODE IS PROVIDED *AS IS* WITHOUT WARRANTY OF
// ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING ANY
// IMPLIED WARRANTIES OF FITNESS FOR A PARTICULAR
// PURPOSE, MERCHANTABILITY, OR NON-INFRINGEMENT.
//
//*********************************************************

using System;
using System.Diagnostics;
using System.Threading;
using System.Linq;
using Windows.ApplicationModel.Background;
using Windows.Media;
using Windows.Media.Playback;

using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.Media.Core;
using System.Collections.Generic;

using SlamLogic.Model;
using SlamLogic.DataHandlers;
using System.Threading.Tasks;
using BaseLogic.DataHandler;
using SlamLogic.BackgroundAudioTaskSharing;
using SlamLogic.BackgroundAudioTaskSharing.Messages;

/* This background task will start running the first time the
 * MediaPlayer singleton instance is accessed from foreground. When a new audio 
 * or video app comes into picture the task is expected to recieve the cancelled 
 * event. User can save state and shutdown MediaPlayer at that time. When foreground 
 * app is resumed or restarted check if your music is still playing or continue from
 * previous state.
 * 
 * This task also implements SystemMediaTransportControl APIs for windows phone universal 
 * volume control. Unlike Windows 8.1 where there are different views in phone context, 
 * SystemMediaTransportControl is singleton in nature bound to the process in which it is 
 * initialized. If you want to hook up volume controls for the background task, do not 
 * implement SystemMediaTransportControls in foreground app process.
 */

namespace BackgroundAudioTask
{
    /// <summary>
    /// Impletements IBackgroundTask to provide an entry point for app code to be run in background. 
    /// Also takes care of handling UVC and communication channel with foreground
    /// </summary>
    public sealed class MyBackgroundAudioTask : IBackgroundTask
    {
        #region Private fields, properties
        private SystemMediaTransportControls smtc;
        private Mix CurrentMix = null;
        private Mix[] Playlist = null;
        private Settings CurrentSettings = null;
        private BackgroundTaskDeferral deferral; // Used to keep task alive
        private AppState foregroundAppState = AppState.Unknown;
        private ManualResetEvent backgroundTaskStarted = new ManualResetEvent(false);
        private bool playbackStartedPreviously = false;
        #endregion

        #region IBackgroundTask and IBackgroundTaskInstance Interface Members and handlers
        /// <summary>
        /// The Run method is the entry point of a background task. 
        /// </summary>
        /// <param name="taskInstance"></param>
        public void Run(IBackgroundTaskInstance taskInstance)
        {
            Debug.WriteLine("Background Audio Task " + taskInstance.Task.Name + " starting...");

            // Initialize SystemMediaTransportControls (SMTC) for integration with
            // the Universal Volume Control (UVC).
            //
            // The UI for the UVC must update even when the foreground process has been terminated
            // and therefore the SMTC is configured and updated from the background task.
            smtc = BackgroundMediaPlayer.Current.SystemMediaTransportControls;
            smtc.ButtonPressed += smtc_ButtonPressed;
            smtc.PropertyChanged += smtc_PropertyChanged;
            smtc.IsEnabled = true;
            smtc.IsPauseEnabled = true;
            smtc.IsPlayEnabled = true;
            smtc.IsNextEnabled = true;
            smtc.IsPreviousEnabled = true;
            DataObject.DisableDispatcher = true;

            // Read persisted state of foreground app
            var value = ApplicationSettingsHelper.ReadResetSettingsValue(ApplicationSettingsConstants.AppState);
            if (value == null)
                foregroundAppState = AppState.Unknown;
            else
                foregroundAppState = EnumHelper.Parse<AppState>(value.ToString());

            // Add handlers for MediaPlayer
            BackgroundMediaPlayer.Current.CurrentStateChanged += Current_CurrentStateChanged;
            BackgroundMediaPlayer.Current.MediaEnded += Current_MediaEnded;

            // Initialize message channel 
            BackgroundMediaPlayer.MessageReceivedFromForeground += BackgroundMediaPlayer_MessageReceivedFromForeground;

            // Send information to foreground that background task has been started if app is active
            if (foregroundAppState != AppState.Suspended)
            {
                MessageService.SendMessageToForeground(new BackgroundAudioTaskStartedMessage());
            }

            ApplicationSettingsHelper.SaveSettingsValue(ApplicationSettingsConstants.BackgroundTaskState, BackgroundTaskState.Running.ToString());

            deferral = taskInstance.GetDeferral(); // This must be retrieved prior to subscribing to events below which use it

            // Mark the background task as started to unblock SMTC Play operation (see related WaitOne on this signal)
            backgroundTaskStarted.Set();

            // Associate a cancellation and completed handlers with the background task.
            taskInstance.Task.Completed += TaskCompleted;
            taskInstance.Canceled += new BackgroundTaskCanceledEventHandler(OnCanceled); // event may raise immediately before continung thread excecution so must be at the end
        }

        private async void Current_MediaEnded(MediaPlayer sender, object args)
        {
            await SkipToNext();
        }

        /// <summary>
        /// Indicate that the background task is completed.
        /// </summary>       
        void TaskCompleted(BackgroundTaskRegistration sender, BackgroundTaskCompletedEventArgs args)
        {
            Debug.WriteLine("MyBackgroundAudioTask " + sender.TaskId + " Completed...");
            deferral.Complete();
        }

        /// <summary>
        /// Handles background task cancellation. Task cancellation happens due to:
        /// 1. Another Media app comes into foreground and starts playing music 
        /// 2. Resource pressure. Your task is consuming more CPU and memory than allowed.
        /// In either case, save state so that if foreground app resumes it can know where to start.
        /// </summary>
        private void OnCanceled(IBackgroundTaskInstance sender, BackgroundTaskCancellationReason reason)
        {
            // You get some time here to save your state before process and resources are reclaimed
            Debug.WriteLine("MyBackgroundAudioTask " + sender.Task.TaskId + " Cancel Requested...");
            try
            {
                // immediately set not running
                backgroundTaskStarted.Reset();

                // save state
                ApplicationSettingsHelper.SaveSettingsValue(ApplicationSettingsConstants.TrackId,CurrentMix == null ? 0 : CurrentMix.InternalID);
                ApplicationSettingsHelper.SaveSettingsValue(ApplicationSettingsConstants.Position, BackgroundMediaPlayer.Current.Position.ToString());
                ApplicationSettingsHelper.SaveSettingsValue(ApplicationSettingsConstants.BackgroundTaskState, BackgroundTaskState.Canceled.ToString());
                ApplicationSettingsHelper.SaveSettingsValue(ApplicationSettingsConstants.AppState, Enum.GetName(typeof(AppState), foregroundAppState));

                // unsubscribe event handlers
                BackgroundMediaPlayer.MessageReceivedFromForeground -= BackgroundMediaPlayer_MessageReceivedFromForeground;
                smtc.ButtonPressed -= smtc_ButtonPressed;
                smtc.PropertyChanged -= smtc_PropertyChanged;

                BackgroundMediaPlayer.Shutdown(); // shutdown media pipeline
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }
            deferral.Complete(); // signals task completion. 
            Debug.WriteLine("MyBackgroundAudioTask Cancel complete...");
        }
        #endregion

        #region SysteMediaTransportControls related functions and handlers
        /// <summary>
        /// Update Universal Volume Control (UVC) using SystemMediaTransPortControl APIs
        /// </summary>
        private void UpdateUVCOnNewTrack(Mix item)
        {


            if (item == null)
            {
                smtc.PlaybackStatus = MediaPlaybackStatus.Stopped;
                smtc.DisplayUpdater.Update();
                return;
            }

            smtc.PlaybackStatus = MediaPlaybackStatus.Playing;
            smtc.DisplayUpdater.Type = MediaPlaybackType.Music;
            smtc.DisplayUpdater.MusicProperties.Title = item.ShowName;
            smtc.DisplayUpdater.MusicProperties.Artist = item.MixSubTitle;

            // var albumArtUri = item.Source.CustomProperties[AlbumArtKey] as Uri;
            //if (albumArtUri != null)
            ////    smtc.DisplayUpdater.Thumbnail = RandomAccessStreamReference.CreateFromUri(albumArtUri);
            //else
            //    smtc.DisplayUpdater.Thumbnail = null;

            smtc.DisplayUpdater.Update();
        }

        /// <summary>
        /// Fires when any SystemMediaTransportControl property is changed by system or user
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        void smtc_PropertyChanged(SystemMediaTransportControls sender, SystemMediaTransportControlsPropertyChangedEventArgs args)
        {
            // If soundlevel turns to muted, app can choose to pause the music
        }

        /// <summary>
        /// This function controls the button events from UVC.
        /// This code if not run in background process, will not be able to handle button pressed events when app is suspended.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private async void smtc_ButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
        {
            switch (args.Button)
            {
                case SystemMediaTransportControlsButton.Play:
                    Debug.WriteLine("UVC play button pressed");

                    // When the background task has been suspended and the SMTC
                    // starts it again asynchronously, some time is needed to let
                    // the task startup process in Run() complete.

                    // Wait for task to start. 
                    // Once started, this stays signaled until shutdown so it won't wait
                    // again unless it needs to.
                    bool result = backgroundTaskStarted.WaitOne(5000);
                    if (!result)
                        throw new Exception("Background Task didnt initialize in time");

                    await StartPlayback();
                    break;
                case SystemMediaTransportControlsButton.Pause:
                    Debug.WriteLine("UVC pause button pressed");
                    try
                    {
                        BackgroundMediaPlayer.Current.Pause();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex.ToString());
                    }
                    break;
                case SystemMediaTransportControlsButton.Next:
                    Debug.WriteLine("UVC next button pressed");
                    await SkipToNext();
                    break;
                case SystemMediaTransportControlsButton.Previous:
                    Debug.WriteLine("UVC previous button pressed");
                    await SkipToPrevious();
                    break;     
            }

            UpdateUVCOnNewTrack(CurrentMix);
        }



        #endregion

        #region Playlist management functions and handlers
        /// <summary>
        /// Start playlist and change UVC state
        /// </summary>
        private async Task StartPlayback(int InternalID = 0)
        {
            if (InternalID != 0)
            {
                CurrentMix = Playlist.Single(m => m.InternalID == InternalID);

                if (CurrentMix != null)
                {
                    playbackStartedPreviously = true;
                    await PlayCurrentMix();
                    return;
                }
            }


            // If playback was already started once we can just resume playing.
            if (!playbackStartedPreviously)
            {
                playbackStartedPreviously = true;

                // If the task was cancelled we would have saved the current track and its position. We will try playback from there.
                var currentTrackId = ApplicationSettingsHelper.ReadResetSettingsValue(ApplicationSettingsConstants.TrackId);
                if (currentTrackId != null)
                {
                    CurrentMix = Playlist.Single(m => m.InternalID == (int)currentTrackId);

                    if (CurrentMix == null)
                    {
                        CurrentMix = Playlist.FirstOrDefault();
                    }

                    await PlayCurrentMix();
                }
            }
            else
            {
                BackgroundMediaPlayer.Current.Play();
            }
        }

        private async Task PlayCurrentMix()
        {
            if (CurrentMix.Downloaded)
            {
                StorageFile sf = await(await MixDataHandler.instance.GetFolder()).GetFileAsync(CurrentMix.MP3FileName);
                BackgroundMediaPlayer.Current.Source = MediaSource.CreateFromStorageFile(sf);
            }
            else
            {
                BackgroundMediaPlayer.Current.Source = MediaSource.CreateFromUri(new Uri(CurrentMix.MP3URL));
            }

            // Begin playing
            BackgroundMediaPlayer.Current.Play();
        }

        /// <summary>
        /// Skip track and update UVC via SMTC
        /// </summary>
        private async Task SkipToPrevious()
        {
            int CurrentIndex = Playlist.ToList().FindIndex(m => m.InternalID == CurrentMix.InternalID);

            if (CurrentIndex == -1)
            {
                BackgroundMediaPlayer.Current.Pause();
                return;
            }

            if (CurrentIndex == 0)
            {
                CurrentIndex = Playlist.Count() - 1;
            }
            else
            {
                CurrentIndex--;
            }

            ApplicationSettingsHelper.SaveSettingsValue(ApplicationSettingsConstants.TrackId, CurrentMix.InternalID);
            smtc.PlaybackStatus = MediaPlaybackStatus.Changing;
            CurrentMix = Playlist[CurrentIndex];
            await PlayCurrentMix();
            MessageService.SendMessageToForeground(new TrackChangedMessage(CurrentMix.InternalID));
        }

        /// <summary>
        /// Skip track and update UVC via SMTC
        /// </summary>
        private async Task SkipToNext()
        {
            int CurrentIndex = Playlist.ToList().FindIndex(m => m.InternalID == CurrentMix.InternalID);

            if (CurrentIndex == -1)
            {
                BackgroundMediaPlayer.Current.Pause();
                return;
            }

            if (CurrentIndex == Playlist.Count() - 1)
            {
                CurrentIndex = 0;
            }
            else
            {
                CurrentIndex++;
            }

            ApplicationSettingsHelper.SaveSettingsValue(ApplicationSettingsConstants.TrackId, CurrentMix.InternalID);
            smtc.PlaybackStatus = MediaPlaybackStatus.Changing;
            CurrentMix = Playlist[CurrentIndex];
            await PlayCurrentMix();
            MessageService.SendMessageToForeground(new TrackChangedMessage(CurrentMix.InternalID));
        }
        #endregion

        #region Background Media Player Handlers
        void Current_CurrentStateChanged(MediaPlayer sender, object args)
        {
            if (sender.CurrentState == MediaPlayerState.Playing)
            {
                smtc.PlaybackStatus = MediaPlaybackStatus.Playing;
            }
            else if (sender.CurrentState == MediaPlayerState.Paused)
            {
                smtc.PlaybackStatus = MediaPlaybackStatus.Paused;
            }
            else if (sender.CurrentState == MediaPlayerState.Closed)
            {
                smtc.PlaybackStatus = MediaPlaybackStatus.Closed;
            }
        }

        private async Task UpdateOptions()
        {
            CurrentSettings = SettingsDataHandler.instance.GetSettings();
            await CreatePlaybackList();
        }

        /// <summary>
        /// Raised when a message is recieved from the foreground app
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void BackgroundMediaPlayer_MessageReceivedFromForeground(object sender, MediaPlayerDataReceivedEventArgs e)
        {
            AppSuspendedMessage appSuspendedMessage;
            if (MessageService.TryParseMessage(e.Data, out appSuspendedMessage))
            {
                Debug.WriteLine("App suspending"); // App is suspended, you can save your task state at this point
                foregroundAppState = AppState.Suspended;
                ApplicationSettingsHelper.SaveSettingsValue(ApplicationSettingsConstants.TrackId, CurrentMix == null ? 0 : CurrentMix.InternalID);
                return;
            }

            AppResumedMessage appResumedMessage;
            if (MessageService.TryParseMessage(e.Data, out appResumedMessage))
            {
                Debug.WriteLine("App resuming"); // App is resumed, now subscribe to message channel
                foregroundAppState = AppState.Active;
                return;
            }

            StartPlaybackMessage startPlaybackMessage;
            if (MessageService.TryParseMessage(e.Data, out startPlaybackMessage))
            {
                //Foreground App process has signalled that it is ready for playback
                Debug.WriteLine("Starting Playback");
                await StartPlayback();
            }

            SkipNextMessage skipNextMessage;
            if (MessageService.TryParseMessage(e.Data, out skipNextMessage))
            {
                // User has chosen to skip track from app context.
                Debug.WriteLine("Skipping to next");
                await SkipToNext();
            }

            SkipPreviousMessage skipPreviousMessage;
            if (MessageService.TryParseMessage(e.Data, out skipPreviousMessage))
            {
                // User has chosen to skip track from app context.
                Debug.WriteLine("Skipping to previous");
                await SkipToPrevious();
            }

            TrackChangedMessage trackChangedMessage;
            if (MessageService.TryParseMessage(e.Data, out trackChangedMessage))
            {
                if (Playlist == null)
                {
                    await UpdateOptions();
                }

                CurrentMix = Playlist.Single(m => m.InternalID == trackChangedMessage.InternalMixID);
                Debug.WriteLine("Skipping to track " + trackChangedMessage.InternalMixID);
                smtc.PlaybackStatus = MediaPlaybackStatus.Changing;
                await PlayCurrentMix();
            }

            UpdatePlaylistMessage updatePlaylistMessage;
            if (MessageService.TryParseMessage(e.Data, out updatePlaylistMessage))
            {
                try
                {
                    while (true)
                    {
                        try
                        {
                            await UpdateOptions();
                            break;
                        }
                        catch
                        {

                        }
                    }

                    if (updatePlaylistMessage.SendUpdateMessage)
                    {
                        MessageService.SendMessageToForeground(new UpdateMediaPlayerInfoMessage(CurrentMix == null ? 0 : CurrentMix.InternalID));
                    }
                }
                catch
                {

                }
            }

            UpdateUVCOnNewTrack(CurrentMix);
        }

        /// <summary>
        /// Create a playback list from the list of songs received from the foreground app.
        /// </summary>
        /// <param name="songs"></param>
        private async Task CreatePlaybackList()
        {
            Playlist = await MixDataHandler.instance.GetMixes(true);
        }
        #endregion
    }
}
