using BaseLogic.DataHandler;
using SlamLogic.DataHandlers;
using SQLite.Net.Attributes;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.IsolatedStorage;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.Xaml;

namespace SlamLogic.Model
{
    public class Mix : DataObject
    {
        public string ShowName { get; set; }
        public string Date { get; set; }
        public string StartTime { get; set; }
        public bool Old { get; set; }
        public int fileSize { get; private set; }

        public string MP3URL { get; set; }
        public string MP3FileName { get; set; }
        private bool _Downloaded = false;
        public bool Downloaded
        {
            get
            {
                return _Downloaded;
            }
            set
            {
                _Downloaded = value;
                NotifyPropertyChanged();
                NotifyPropertyChanged("DownloadButtonVisibility");
                NotifyPropertyChanged("DeleteButtonVisibility");
            }
        }

        private int? _Rating;
        public int? Rating
        {
            get { return _Rating; }
            set
            {
                _Rating = value;
                NotifyPropertyChanged();
            }
        }

        private int _TimesPlayed;

        public int TimesPlayed
        {
            get { return _TimesPlayed; }
            set
            {
                if (value != 0)
                {

                }

                _TimesPlayed = value;
                NotifyPropertyChanged();
            }
        }



        public DateTime TimeInserted { get; set; }
        public DateTime TimeDownloaded { get; set; }

        private bool _Playing;
        [Ignore]
        public bool Playing
        {
            get { return _Playing; }
            set
            {
                _Playing = value;
                NotifyPropertyChanged();
                NotifyPropertyChanged("PlayButtonEnabled");
            }
        }

        [Ignore]
        public string FileSizeText
        {
            get
            {
                return string.Format("Bestandsgrootte: {0}MB", fileSize);
            }
        }

        [Ignore]
        public string TimesPlayedText
        {
            get
            {
                return string.Format("Aantal keren afgespeeld: {0}", TimesPlayed);
            }
        }


        private bool _IsDownloading = false;
        [Ignore]
        public bool IsDownloading
        {
            get
            {
                return _IsDownloading;
            }
            private set
            {
                _IsDownloading = value;
                NotifyPropertyChanged();
                NotifyPropertyChanged("DownloadButtonVisibility");
                NotifyPropertyChanged("CancelButtonVisibility");
                NotifyPropertyChanged("PlayButtonEnabled");
            }
        }

        private float _DownloadProgress;
        [Ignore]
        public float DownloadProgress
        {
            get { return _DownloadProgress; }
            set
            {
                _DownloadProgress = value;
                NotifyPropertyChanged();
            }
        }

        [Ignore]
        private Stream MP3Stream { get; set; }
        [Ignore]
        private StorageFile Mp3File { get; set; }
        [Ignore]
        private long MaxLenght { get; set; }
        [Ignore]
        private long CurrentLength { get; set; }
        [Ignore]
        private Task<bool> DownloadTask { get; set; }
        [Ignore]
        private Task DownloadUpdateTask { get; set; }
        [Ignore]
        private CancellationTokenSource DownloadCancellation { get; set; }
        [Ignore]
        private CancellationTokenSource DownloadUpdateCancellation { get; set; }


        //ButtonVisibility
        [Ignore]
        public bool PlayButtonEnabled
        {
            get
            {
                return !IsDownloading && !Playing;
            }
        }

        [Ignore]
        public Visibility DownloadButtonVisibility
        {
            get
            {
                return (!Downloaded && !IsDownloading) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        [Ignore]
        public Visibility CancelButtonVisibility
        {
            get
            {
                return (IsDownloading) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        [Ignore]
        public Visibility DeleteButtonVisibility
        {
            get
            {
                return (Downloaded) ? Visibility.Visible : Visibility.Collapsed;
            }
        }


        [Ignore]
        public string MixSubTitle
        {
            get
            {
                return string.Format("{0} {1}", Date, StartTime);
            }
        }
        [Ignore]
        public string DownloadedOn
        {
            get
            {
                return "Gedownload op: " + TimeDownloaded;
            }
        }

        public Mix() : base()
        {
            CurrentLength = 0;
        }

        public async Task<StorageFile> GetDownloadedFile()
        {
            if (Downloaded)
            {
                return await (await MixDataHandler.instance.GetFolder()).GetFileAsync(MP3FileName);
            }

            return null;
        }

        public async Task<bool> Download()
        {
            if (IsDownloading)
            {
                return true;
            }

            DownloadCancellation = new CancellationTokenSource();
            DownloadTask = Task.Run(async () =>
            {
                IsDownloading = true;

                try
                {
                    using (var response = await HttpWebRequest.CreateHttp(MP3URL).GetResponseAsync().ConfigureAwait(false))
                    {
                        MaxLenght = response.ContentLength;
                        using (var stream = response.GetResponseStream())
                        {
                            MP3FileName = string.Format("{0}_{1}_{2}.mp3", ShowName, Date, StartTime);
                            Mp3File = await (await MixDataHandler.instance.GetFolder()).CreateFileAsync(MP3FileName, CreationCollisionOption.OpenIfExists);

                            using (MP3Stream = await Mp3File.OpenStreamForWriteAsync())
                            {
                                DownloadUpdateCancellation = new CancellationTokenSource();
                                DownloadUpdateTask = Task.Run(() => CheckDownloadStatus(), DownloadUpdateCancellation.Token);

                                await stream.CopyToAsync(MP3Stream, 4096, DownloadUpdateCancellation.Token);
                            }
                        }
                    }

                    Downloaded = true;
                    fileSize = (int)MaxLenght / 1024 / 1024;
                    NotifyPropertyChanged("FileSizeText");
                }
                catch (Exception e)
                {
                    Downloaded = false;
                }
                finally
                {
                    IsDownloading = false;
                    DownloadCancellation = null;
                    DownloadUpdateCancellation.Cancel();
                    DownloadUpdateCancellation = null;
                    MP3Stream = null;
                    TimeDownloaded = DateTime.Now;
                    NotifyPropertyChanged("DownloadedOn");
                    MixDataHandler.instance.UpdateMix(this);
                }

                return Downloaded;
            }, DownloadCancellation.Token);

            return await DownloadTask;
        }

        public async Task CancelDownload()
        {
            if (DownloadCancellation != null && DownloadCancellation.Token.CanBeCanceled)
            {
                IsDownloading = false;
                Downloaded = false;
                MP3FileName = null;
                DownloadCancellation.Cancel();
                DownloadCancellation = null;

                GC.Collect();

                DownloadUpdateCancellation.Cancel();
                DownloadUpdateCancellation = null;
                await Mp3File.DeleteAsync();

                MixDataHandler.instance.UpdateMix(this);
            }
        }

        private async Task CheckDownloadStatus()
        {
            while (true)
            {
                await (Task.Delay(100));

                if (MP3Stream == null)
                {
                    break;
                }

                CurrentLength = MP3Stream.Position;

                await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    DownloadProgress = (((float)CurrentLength / (float)MaxLenght) * (float)100);
                });
            }
        }

        public async Task Delete()
        {
            if (Downloaded && MP3FileName != null)
            {
                await MixDataHandler.instance.DeleteFile(MP3FileName);
                MP3FileName = null;
                Downloaded = false;
                MixDataHandler.instance.UpdateMix(this);
            }
        }

        public void UpdateTimesPlayed()
        {
            TimesPlayed++;
            MixDataHandler.instance.UpdateMix(this);
            NotifyPropertyChanged("TimesPlayedText");
        }
    }
}
