﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Chraft.Entity;
using Chraft.Interfaces;
using Chraft.Plugins.Events.Args;

namespace Chraft.World.Blocks
{
    class BlockWoodenPressurePlate : BlockBase
    {
        public BlockWoodenPressurePlate()
        {
            Name = "WoodenPressurePlate";
            Type = BlockData.Blocks.Wooden_Pressure_Plate;
            IsAir = true;
            LootTable.Add(new ItemStack((short)Type, 1));
            Opacity = 0x0;
        }
    }
}
