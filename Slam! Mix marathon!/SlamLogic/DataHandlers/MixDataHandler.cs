using BaseLogic.DataHandler;
using SlamLogic.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Text;
using System.Threading.Tasks;
using WebCrawlerTools;
using Windows.ApplicationModel.Resources;

namespace SlamLogic.DataHandlers
{
    public class MixDataHandler : DataHandler
    {
        public static readonly MixDataHandler instance = new MixDataHandler();
        private const string MixTapeURL = "http://www.slam.nl/terugluisteren/";
        private const string DebugString = "[Mix] {0} mix {1} on {2} {3}";
        private static readonly string[] Whitelist = new string[]
        {
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

        public void UpdateMix(Mix mix)
        {
            lock (locker)
            {
                Update(mix);
            }
        }

        public Warning MixDataWarning { get; private set; }

        private MixDataHandler() : base()
        {
            lock (locker)
            {
                CreateTable<Mix>();
            }
        }

        public async Task<Mix[]> GetMixes(bool Offline)
        {
            if (!Offline)
            {
                MixDataWarning = null;
                System.Diagnostics.Debug.WriteLine(string.Format("[MIX] There are {0} mixes in the database", GetItems<Mix>().Count()));
                MarkMixesAsOld();
                Task InternetTask = Task.Run(() => GetMixesFromInternet());

                await InternetTask;
                ClearOldMixes();
            }

            System.Diagnostics.Debug.WriteLine(string.Format("[MIX] There are {0} mixes in the database", GetItems<Mix>().Count()));
            return GetItems<Mix>().OrderByDescending(m => m.Date).ThenByDescending(m => m.StartTime).ToArray();
        }

        private async Task GetMixesFromInternet()
        {
            string Source = await GetMixPageSource();
            Source = Source.Substring(HTMLParserUtil.GetPositionOfStringInHTMLSource("<div class=\"carousel-inner\">", Source, false));
            
            while (true)
            {
                try
                {
                    Source = Source.Substring(HTMLParserUtil.GetPositionOfStringInHTMLSource("class=\"time\">", Source, false));
                    string Date = HTMLParserUtil.GetContentAndSubstringInput("<br />", "</td>", Source, out Source).Trim();
                    string ShowName = HTMLParserUtil.GetContentAndSubstringInput("class=\"program\">", "</td>", Source, out Source).Trim();

                    if (!Whitelist.Contains(ShowName))
                    {
                        Source = Source.Substring(HTMLParserUtil.GetPositionOfStringInHTMLSource("<table style=\"width: 100%;\">", Source, false));
                        continue;
                    }

                    string Mp3URLSource = HTMLParserUtil.GetContentAndSubstringInput("class=\"uitzending\">", " </table>", Source, out Source);

                    while (true)
                    {
                        try
                        {
                            Mp3URLSource = Mp3URLSource.Substring(HTMLParserUtil.GetPositionOfStringInHTMLSource("data-source=\"", Mp3URLSource, false));
                            string MP3URL = HTMLParserUtil.GetContentAndSubstringInput("data-source=\"", "\">", Mp3URLSource, out Mp3URLSource, "", false);

                            string StartTime = HTMLParserUtil.GetContentAndSubstringInput("\">", "</span>", Mp3URLSource, out Mp3URLSource);

                            var MatchingMixes = GetItems<Mix>().Where(m => m.MP3URL.Trim() == MP3URL.Trim());
                            if (MatchingMixes.Count() > 0)
                            {
                                foreach (Mix m in MatchingMixes)
                                {
                                    m.Old = false;
                                    UpdateMix(m);
                                }

                                System.Diagnostics.Debug.WriteLine(string.Format(DebugString, "Skipping", ShowName, Date, StartTime));
                                continue;
                            }
                            else
                            {
                                Mix CurrentMix = new Mix() { StartTime = StartTime, Date = Date, ShowName = ShowName, MP3URL = MP3URL, TimeInserted = DateTime.Now };

                                lock (locker)
                                {
                                    Insert(CurrentMix);
                                    System.Diagnostics.Debug.WriteLine(String.Format(DebugString, "Added", CurrentMix.ShowName, CurrentMix.Date, CurrentMix.StartTime));
                                }
                            }
                        }
                        catch
                        {
                            break;
                        }
                    }

                    continue;
                }
                catch
                {
                    break;
                }
            }
        }

        private void MarkMixesAsOld()
        {
            foreach (Mix m in GetItems<Mix>().Where(m => !m.Downloaded))
            {
                m.Old = true;
                UpdateMix(m);
            }
        }

        private void ClearOldMixes()
        {
            Mix[] Mixes = GetItems<Mix>().Where(m => m.Old).ToArray();

            foreach (Mix m in Mixes)
            {
                lock (locker)
                {
                    Delete(m);
                    System.Diagnostics.Debug.WriteLine(String.Format(DebugString, "Deleted", m.ShowName, m.Date, m.StartTime));
                }
            }
        }

        private async Task<string> GetMixPageSource()
        {
            string PageSource = string.Empty;

            try
            {
                PageSource = await HTTPGetUtil.GetDataAsStringFromURL(MixTapeURL);
            }
            catch (Exception e)
            {
                MixDataWarning = new Warning("Kon Slam! niet bereiken! :(", e);
            }

            return PageSource;
        }
    }
}
