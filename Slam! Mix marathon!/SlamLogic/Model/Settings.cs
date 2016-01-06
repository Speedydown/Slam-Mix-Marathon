using BaseLogic.DataHandler;
using System;

namespace SlamLogic.Model
{
    public sealed class Settings : DataObject
    {
        public bool OfflineMode { get; set; }
        public DateTime LastRetrievedFromInternet { get; set; }
        public int SortingIndex { get; set; }


        public Settings()
        {
            LastRetrievedFromInternet = DateTime.Now.AddDays(-3);
        }
    }
}
