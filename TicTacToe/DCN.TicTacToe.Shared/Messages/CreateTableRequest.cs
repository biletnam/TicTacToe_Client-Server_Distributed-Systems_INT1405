﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DCN.TicTacToe.Shared.Messages
{
    [Serializable]
    public class CreateTableRequest :RequestMessageBase
    {
        public bool IsCreate { get; set; }
        public int TableNumber { get; set; }
    }
}
