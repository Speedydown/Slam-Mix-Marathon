﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace SlamLogic.BackgroundAudioTaskSharing.Messages
{
    [DataContract]
    public class UpdateMediaPlayerInfoMessage
    {
        public UpdateMediaPlayerInfoMessage()
        {
        }

        public UpdateMediaPlayerInfoMessage(int InternalMixID)
        {
            this.InternalMixID = InternalMixID;
        }

        [DataMember]
        public int InternalMixID;
    }
}
