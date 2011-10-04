﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Chraft.Entity;
using Chraft.Interfaces;
using Chraft.Plugins.Events.Args;

namespace Chraft.World.Blocks
{
    class BlockBrownMushroom : BlockBase
    {
        public BlockBrownMushroom()
        {
            Name = "BrownMushroom";
            Type = BlockData.Blocks.Brown_Mushroom;
            IsAir = true;
            IsSingleHit = true;
            LootTable.Add(new ItemStack((short)Type, 1));
            Opacity = 0x0;
        }
    }
}
