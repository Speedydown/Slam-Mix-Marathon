using BaseLogic.DataHandler;
using System;

namespace SlamLogic.Model
{
    public sealed class Settings : DataObject
    {
        private bool _OfflineMode = false;
        public bool OfflineMode
        {
            get
            {
                return _OfflineMode;
            }
            set
            {
                _OfflineMode = value;
            }
        }

        public DateTime LastRetrievedFromInternet { get; set; }
        public int SortingIndex { get; set; }


        public Settings()
        {
            LastRetrievedFromInternet = DateTime.Now.AddDays(-3);
        }
    }
}
