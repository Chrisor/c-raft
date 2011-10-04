﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Chraft.Entity;
using Chraft.Interfaces;
using Chraft.Plugins.Events.Args;

namespace Chraft.World.Blocks
{
    class BlockClay : BlockBase
    {
        public BlockClay()
        {
            Name = "Clay";
            Type = BlockData.Blocks.Clay;
            IsSolid = true;
            DropItem = BlockData.Items.Clay_Balls;
            DropItemAmount = 4;
        }
    }
}
