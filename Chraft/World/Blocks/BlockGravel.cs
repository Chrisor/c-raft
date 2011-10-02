﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Chraft.Entity;
using Chraft.Interfaces;
using Chraft.Plugins.Events.Args;

namespace Chraft.World.Blocks
{
    class BlockGravel : BlockBase
    {
        public BlockGravel()
        {
            Name = "Gravel";
            Type = BlockData.Blocks.Gravel;
            IsSolid = true;
        }

        protected override void DropItems(EntityBase entity, StructBlock block)
        {
            Client client = entity as Client;
            if (client != null)
            {
                LootTable = new List<ItemStack>();
                if ((client.Inventory.ActiveItem.Type == (short)BlockData.Items.Wooden_Spade ||
                    client.Inventory.ActiveItem.Type == (short)BlockData.Items.Stone_Spade ||
                    client.Inventory.ActiveItem.Type == (short)BlockData.Items.Iron_Spade ||
                    client.Inventory.ActiveItem.Type == (short)BlockData.Items.Gold_Spade ||
                    client.Inventory.ActiveItem.Type == (short)BlockData.Items.Diamond_Spade) &&
                    block.World.Server.Rand.Next(10) == 0)
                {
                    LootTable.Add(new ItemStack((short)BlockData.Items.Flint, 1));
                }
                else
                {
                    LootTable.Add(new ItemStack((short)Type, 1));
                }
            }
            base.DropItems(entity, block);
        }
    }
}
