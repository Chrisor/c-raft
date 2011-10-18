﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Chraft.Entity;
using Chraft.Interfaces;
using Chraft.Plugins.Events.Args;

namespace Chraft.World.Blocks
{
    class BlockSnow : BlockBase
    {
        public BlockSnow()
        {
            Name = "Snow";
            Type = BlockData.Blocks.Snow;
            IsAir = true;
            Opacity = 0x0;
            IsSolid = true;
            BlockBoundsOffset = new BoundingBox(0, 0, 0, 1, 0.125, 1);
        }

        protected override void  DropItems(EntityBase entity, StructBlock block)
        {
            LootTable = new List<ItemStack>();
            Player player = entity as Player;
            if (player != null)
            {
                if (player.Inventory.ActiveItem.Type == (short)BlockData.Items.Wooden_Spade ||
                    player.Inventory.ActiveItem.Type == (short)BlockData.Items.Stone_Spade ||
                    player.Inventory.ActiveItem.Type == (short)BlockData.Items.Iron_Spade ||
                    player.Inventory.ActiveItem.Type == (short)BlockData.Items.Gold_Spade ||
                    player.Inventory.ActiveItem.Type == (short)BlockData.Items.Diamond_Spade)
                {
                    LootTable.Add(new ItemStack((short)BlockData.Items.Snowball, 1));
                }
            }
            base.DropItems(entity, block);
        }
    }
}
