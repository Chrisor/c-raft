﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Chraft.Entity;
using Chraft.Net;
using Chraft.Interfaces;
using Chraft.Plugins.Events.Args;
using Chraft.World.Blocks.Interfaces;

namespace Chraft.World.Blocks
{
    class BlockChest : BlockBase, IBlockInteractive
    {
        public BlockChest()
        {
            Name = "Chest";
            Type = BlockData.Blocks.Chest;
            LootTable.Add(new ItemStack((short)Type, 1));
            BurnEfficiency = 300;
        }

        public override void Place(EntityBase entity, StructBlock block, StructBlock targetBlock, BlockFace face)
        {
            // Load the blocks surrounding the position (NSEW) not diagonals
            BlockData.Blocks[] nsewBlocks = new BlockData.Blocks[4];
            UniversalCoords[] nsewBlockPositions = new UniversalCoords[4];
            int nsewCount = 0;
            block.Chunk.ForNSEW(block.Coords, (uc) =>
            {
                nsewBlocks[nsewCount] = (BlockData.Blocks)block.World.GetBlockId(uc);
                nsewBlockPositions[nsewCount] = uc;
                nsewCount++;
            });

            // Count chests in list
            if (nsewBlocks.Where((b) => b == BlockData.Blocks.Chest).Count() > 1)
            {
                // Cannot place next to two chests
                return;
            }

            for (int i = 0; i < 4; i++)
            {
                UniversalCoords p = nsewBlockPositions[i];
                if (nsewBlocks[i] == BlockData.Blocks.Chest && block.Chunk.IsNSEWTo(p, (byte)BlockData.Blocks.Chest))
                {
                    // Cannot place next to a double chest
                    return;
                }
            }
            base.Place(entity, block, targetBlock, face);
        }

        protected override void DropItems(EntityBase entity, StructBlock block)
        {
            Player player = entity as Player;
            if (player != null)
            {
                SmallChestInterface sci = new SmallChestInterface(block.World, block.Coords);
                sci.Associate(player);
                sci.DropAll(block.Coords);
                sci.Save();
            }
            base.DropItems(entity, block);
        }

        public void Interact(EntityBase entity, StructBlock block)
        {
            Player player = entity as Player;
            if (player == null)
                return;
            if (player.CurrentInterface != null)
                return;

            if (!BlockHelper.Instance(block.World.GetBlockId(block.Coords)).IsAir)
            {
                // Cannot open a chest if no space is above it
                return;
            }

            Chunk chunk = player.World.GetBlockChunk(block.Coords);

            // Double chest?
            if (chunk.IsNSEWTo(block.Coords, block.Type))
            {
                // Is this chest the "North or East", or the "South or West"
                BlockData.Blocks[] nsewBlocks = new BlockData.Blocks[4];
                UniversalCoords[] nsewBlockPositions = new UniversalCoords[4];
                int nsewCount = 0;
                chunk.ForNSEW(block.Coords, (uc) =>
                {
                    nsewBlocks[nsewCount] = (BlockData.Blocks)block.World.GetBlockId(uc);
                    nsewBlockPositions[nsewCount] = uc;
                    nsewCount++;
                });

                if ((byte)nsewBlocks[0] == block.Type) // North
                {
                    player.CurrentInterface = new LargeChestInterface(block.World, nsewBlockPositions[0], block.Coords);
                }
                else if ((byte)nsewBlocks[2] == block.Type) // East
                {
                    player.CurrentInterface = new LargeChestInterface(block.World, nsewBlockPositions[2], block.Coords);
                }
                else if ((byte)nsewBlocks[1] == block.Type) // South
                {
                    player.CurrentInterface = new LargeChestInterface(block.World, block.Coords, nsewBlockPositions[1]);
                }
                else if ((byte)nsewBlocks[3] == block.Type) // West
                {
                    player.CurrentInterface = new LargeChestInterface(block.World, block.Coords, nsewBlockPositions[3]);
                }
            }
            else
            {
                player.CurrentInterface = new SmallChestInterface(block.World, block.Coords);
            }

            player.CurrentInterface.Associate(player);
            player.CurrentInterface.Open();
        }

    }
}
