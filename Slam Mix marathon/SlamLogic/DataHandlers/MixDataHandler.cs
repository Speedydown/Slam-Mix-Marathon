using BaseLogic.ClientIDHandler;
using BaseLogic.DataHandler;
using BaseLogic.ExceptionHandler;
using BaseLogic.HtmlUtil;
using BaseLogic.Utils;
using SlamLogic.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.System;
using Windows.UI.Core;

namespace SlamLogic.DataHandlers
{
    public class MixDataHandler : DataHandler
    {
        private static readonly object MixListLocker = new object();
        internal static readonly object DatabaseLocker = new object();
        public static readonly MixDataHandler instance = new MixDataHandler();
        private const string MixTapeURL = "http://www.slam.nl/terugluisteren/";
        private const string DebugString = "[Mix] {0} mix {1} on {2} {3}";
        private static readonly string[] Whitelist = new string[]
        {
            "MixMarathon",
            "Mix Marathon",
            "The Partysquad",
            "FLOW",
            "Blasterjaxx",
            "Firebeatz",
            "Oliver Heldens",
            "I Need R3hab",
            "Glowinthedark",
            "Flow",
            "Identity",
            "Sunnery & Ryan",
            "Mainstage",
            "SLAM! MixMarathon XXL"
        };

        public Warning MixDataWarning { get; private set; }

        private MixDataHandler() : base()
        {
            CreateItemTable<Mix>();

            try
            {
                if (Windows.UI.Xaml.Application.Current != null)
                {
                    Windows.UI.Xaml.Application.Current.UnhandledException += (async (sender, e) =>
                    {
                        AppException ae = new AppException(e.Exception);
                        await ExceptionHandler.instance.PostException(ae, (int)ClientIDHandler.AppName.SlamMix);
                    });
                }
            }
            catch
            {

            }
        }

        public Mix GetMixByID(int ID)
        {
            return GetItem<Mix>(ID);
        }

        public void UpdateMix(Mix mix)
        {
            UpdateItem(mix);
        }

        public async Task<Mix[]> GetMixes(bool Offline)
        {
            Settings CurrentSettings = null;

            lock (DatabaseLocker)
            {
                CurrentSettings = SettingsDataHandler.instance.GetSettings();
            }

            //Check if app is in offline mode
            if (CurrentSettings.OfflineMode)
            {
                Offline = true;
            }

            Logger.Set("GetMixes");
            if (!Offline && DateTime.Now.Subtract(CurrentSettings.LastRetrievedFromInternet).TotalMinutes > 29)
            {
                MixDataWarning = null;
                MarkMixesAsOld();
                Task InternetTask = Task.Run(() => GetMixesFromInternet());

                await InternetTask;
                ClearOldMixes();
            }

            Logger.Set("GetMixes");

            Mix[] Mixes = null;

            lock (DatabaseLocker)
            {
                Mixes = GetItems<Mix>()
                .OrderByDescending(m => m.RealDate)
                .ThenByDescending(m => m.StartTime)
                .ThenByDescending(m => m.InternalID)
                .ToArray();

                if (!Offline && Mixes.Count() > 0)
                {
                    CurrentSettings.LastRetrievedFromInternet = DateTime.Now;
                    SettingsDataHandler.instance.UpdateSettings(CurrentSettings);

                    //Post appstats
                    Task.Run(async () =>
                    {
                        await ClientIDHandler.instance.PostAppStats(ClientIDHandler.AppName.SlamMix);

                        if (ClientIDHandler.instance.NumberOfRequests == 15)
                        {
                            await AskForReview();
                        }
                    });
                }
            }

            if (CurrentSettings.OfflineMode)
            {
                Mixes = Mixes.Where(m => m.Downloaded).ToArray();
            }

            return Mixes;
        }

        private async Task AskForReview()
        {
            var dialog = new Windows.UI.Popups.MessageDialog(
                "Goede feedback is belangrijk en zorgt ervoor dat onze app meer opvalt in de store. Geef ons daarom uw mening over deze app!",
                "Review");

            dialog.Commands.Add(new Windows.UI.Popups.UICommand("Review") { Id = 0 });
            dialog.Commands.Add(new Windows.UI.Popups.UICommand("Nee bedankt") { Id = 1 });

            dialog.DefaultCommandIndex = 0;
            dialog.CancelCommandIndex = 1;

            try
            {
                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
                {
                    try
                    {
                        var result = await dialog.ShowAsync();

                        if (result.Label == "Review")
                        {
                            await Launcher.LaunchUriAsync(new Uri(string.Format("ms-windows-store:REVIEW?PFN={0}", Windows.ApplicationModel.Package.Current.Id.FamilyName)));
                        }
                    }
                    catch
                    {

                    }
                });
                
            }
            catch
            {

            }
        }

        private async Task GetMixesFromInternet()
        {
            List<Mix> UpdatedMixes = new List<Mix>();
            List<Task> URLTasks = new List<Task>();
            string Source = await GetMixPageSource();

            if (Source.Contains("<div class=\"carousel-inner\">"))
            {
                Source = Source.Substring(HTMLParserUtil.GetPositionOfStringInHTMLSource("<div class=\"carousel-inner\">", Source, false));
            }

            while (true)
            {
                if (!Source.Contains("class=\"time\">"))
                {
                    break;
                }

                try
                {
                    Source = Source.Substring(HTMLParserUtil.GetPositionOfStringInHTMLSource("class=\"time\">", Source, false));
                    string Date = HTMLParserUtil.GetContentAndSubstringInput("<br />", "</td>", Source, out Source).Trim();
                    string ShowName = HTMLParserUtil.GetContentAndSubstringInput("class=\"program\">", "</td>", Source, out Source).Trim();

                    if (!Whitelist.Contains(ShowName))
                    {
                        if (!Source.Contains("<table"))
                        {
                            break;
                        }

                        Source = Source.Substring(HTMLParserUtil.GetPositionOfStringInHTMLSource("<table style=\"width: 100%;\">", Source, false));
                        continue;
                    }

                    DateTime ConvertedDateTime = ParseDate(Date);

                    string Mp3URLSource = HTMLParserUtil.GetContentAndSubstringInput("class=\"uitzending\">", " </table>", Source, out Source);

                    URLTasks.Add(Task.Run(() => GetMixesFromMP3URLSource(Mp3URLSource, ShowName, Date, ConvertedDateTime, UpdatedMixes)));
                }
                catch
                {
                    break;
                }
            }

            Task.WaitAll(URLTasks.ToArray());

            SaveItems(UpdatedMixes);
        }

        private DateTime ParseDate(string InputDate)
        {
            bool ResultValid = true;

            try
            {
                string[] SplittedDate = InputDate.Split(' ');

                int DayNumber = 0;
                ResultValid = int.TryParse(SplittedDate[0], out DayNumber);
                int MonthNumber = GetMonthNumberFromString(SplittedDate[1]);
                int Year = DateTime.Now.Month == 1 && MonthNumber == 12 ? DateTime.Now.AddYears(-1).Year : DateTime.Now.Year;

                return new DateTime(Year, MonthNumber, DayNumber);
            }
            catch
            {
                //Invalid Date
                return DateTime.Now;
            }
        }

        private int GetMonthNumberFromString(string Input)
        {
            switch (Input)
            {
                case "Jan":
                    return 1;
                case "Feb":
                    return 2;
                case "Mar":
                    return 3;
                case "Apr":
                    return 4;
                case "Mei":
                    return 5;
                case "Jun":
                    return 6;
                case "Jul":
                    return 7;
                case "Aug":
                    return 8;
                case "Sep":
                    return 9;
                case "Okt":
                    return 10;
                case "Nov":
                    return 11;
                case "Dec":
                    return 12;
                default:
                    return DateTime.Now.Month;
            }
        }

        private void GetMixesFromMP3URLSource(string Mp3URLSource, string ShowName, string Date, DateTime RealDate, List<Mix> UpdatedMixes)
        {
            while (true)
            {
                if (!Mp3URLSource.Contains("data-source=\""))
                {
                    break;
                }

                try
                {
                    Mp3URLSource = Mp3URLSource.Substring(HTMLParserUtil.GetPositionOfStringInHTMLSource("data-source=\"", Mp3URLSource, false));
                    string MP3URL = HTMLParserUtil.GetContentAndSubstringInput("data-source=\"", "\">", Mp3URLSource, out Mp3URLSource, "", false);

                    string StartTime = HTMLParserUtil.GetContentAndSubstringInput("\">", "</span>", Mp3URLSource, out Mp3URLSource);

                    var MatchingMix = GetItems<Mix>().Where(m => m.MP3URL.Trim() == MP3URL.Trim()).FirstOrDefault();

                    if (MatchingMix != null)
                    {
                        if (MatchingMix.Old)
                        {
                            MatchingMix.Old = false;

                            lock (MixListLocker)
                            {
                                UpdatedMixes.Add(MatchingMix);

                            }
                        }
                    }
                    else
                    {
                        Mix CurrentMix = new Mix() { StartTime = StartTime, Date = Date, RealDate = RealDate, ShowName = ShowName, MP3URL = MP3URL, TimeInserted = DateTime.Now };

                        lock (MixListLocker)
                        {
                            UpdatedMixes.Add(CurrentMix);
                        }
                    }
                }
                catch
                {
                    break;
                }
            }
        }

        private void MarkMixesAsOld()
        {
            Mix[] Mixes = null;

            lock (DatabaseLocker)
            {
                Mixes = GetItems<Mix>().Where(m => !m.Downloaded && DateTime.Now.Subtract(m.RealDate).Days > 5).ToArray();
            }

            foreach (Mix m in Mixes)
            {
                m.Old = true;
            }

            lock (DatabaseLocker)
            {
                SaveItems(Mixes);
            }
        }

        private void ClearOldMixes()
        {
            Mix[] Mixes = null;

            lock (DatabaseLocker)
            {
                Mixes = GetItems<Mix>().Where(m => m.Old).ToArray();

                DeleteItems(Mixes);
                System.Diagnostics.Debug.WriteLine(string.Format("[Mix] Deleted {0} old mixes.", Mixes.Count()));
            }
        }

        private async Task<string> GetMixPageSource()
        {
            Logger.Set("GetMixPageSource");
            string PageSource = string.Empty;

            try
            {
                PageSource = await HTTPGetUtil.GetDataAsStringFromURL(MixTapeURL);
            }
            catch (Exception e)
            {
                MixDataWarning = new Warning("Kon Slam! niet bereiken! :(", e);
            }

            Logger.Set("GetMixPageSource");

            return PageSource;
        }
    }
}
