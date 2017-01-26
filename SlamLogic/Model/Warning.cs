using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SlamLogic.Model
{
    public sealed class Warning
    {
        public string WarningText { get; private set; }
        public Exception WarningCause { get; private set; }

        public Warning(string WarningText, Exception WarningCause)
        {
            this.WarningText = WarningText;
            this.WarningCause = WarningCause;
        }
    }
}
