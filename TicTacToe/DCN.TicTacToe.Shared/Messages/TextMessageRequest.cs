﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DCN.TicTacToe.Shared.Messages
{
    [Serializable]
    public class TextMessageRequest : RequestMessageBase
    {
        public String Message { get; set; }
    }
}
