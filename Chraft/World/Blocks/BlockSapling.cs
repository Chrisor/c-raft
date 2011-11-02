﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Chraft.Entity;
using Chraft.Interfaces;
using Chraft.Plugins.Events.Args;
using Chraft.World.Blocks.Interfaces;

namespace Chraft.World.Blocks
{
    class BlockSapling : BlockBase, IBlockGrowable
    {
        public BlockSapling()
        {
            Name = "Sapling";
            Type = BlockData.Blocks.Sapling;
            IsAir = true;
            IsSingleHit = true;
            BurnEfficiency = 100;
            Opacity = 0x0;
            BlockBoundsOffset = new BoundingBox(0.1, 0, 0.1, 0.9, 0.8, 0.9);
        }


        protected override bool CanBePlacedOn(EntityBase entity, StructBlock block, StructBlock targetBlock, BlockFace targetSide)
        {
            if (!BlockHelper.Instance(targetBlock.Type).IsFertile || targetSide != BlockFace.Up)
                return false;
            return base.CanBePlacedOn(entity, block, targetBlock, targetSide);
        }

        protected override void DropItems(EntityBase entity, StructBlock block)
        {
            LootTable = new List<ItemStack>();
            LootTable.Add(new ItemStack((short)Type, 1, block.MetaData));
            base.DropItems(entity, block);
        }

        public bool CanGrow(StructBlock block, Chunk chunk)
        {
            if (chunk == null || block.Coords.WorldY > 120)
                return false;
            /*UniversalCoords oneUp = UniversalCoords.FromWorld(block.Coords.WorldX, block.Coords.WorldY + 1,
                                                              block.Coords.WorldZ);
            byte lightUp = block.World.GetBlockData(oneUp);
            if (lightUp < 9)
                return false;*/
            return true;
        }

        public void Grow(StructBlock block, Chunk chunk)
        {
            if (!CanGrow(block, chunk))
                return;

            UniversalCoords blockUp = UniversalCoords.FromWorld(block.Coords.WorldX, block.Coords.WorldY + 1, block.Coords.WorldZ);
            if (block.World.GetEffectiveLight(blockUp) < 9)
                return;

            if (block.World.Server.Rand.Next(29) != 0)
                return;

            if ((block.MetaData & 8) == 0)
            {
                chunk.SetData(block.Coords, (byte)(block.MetaData | 8));
                return;
            }

            for (int i = block.Coords.WorldY; i < block.Coords.WorldY + 4; i++)
            {
                chunk.SetBlockAndData(block.Coords.WorldX, i, block.Coords.WorldZ, (byte)BlockData.Blocks.Log, block.MetaData);
                if(chunk.GetType(block.Coords.WorldX, i + 1, block.Coords.WorldZ) != BlockData.Blocks.Air)
                    break;
            }

            // Grow leaves
            for (int i = block.Coords.WorldY + 2; i < block.Coords.WorldY + 5; i++)
                for (int j = block.Coords.WorldX - 2; j <= block.Coords.WorldX + 2; j++)
                    for (int k = block.Coords.WorldZ - 2; k <= block.Coords.WorldZ + 2; k++)
                    {
                        Chunk nearbyChunk = block.World.GetChunkFromWorld(i, k, false, false);
                        if (nearbyChunk == null || (nearbyChunk.GetType(j, i, k) != BlockData.Blocks.Air))
                            continue;


                        nearbyChunk.SetBlockAndData(j, i, k, (byte)BlockData.Blocks.Leaves,
                                                        block.MetaData);
                    }

            for (int i = block.Coords.WorldX - 1; i <= block.Coords.WorldX + 1; i++)
                for (int j = block.Coords.WorldZ - 1; j <= block.Coords.WorldZ + 1; j++)
                {
                    Chunk nearbyChunk = block.World.GetChunkFromWorld(i, j, false, false);
                    if (nearbyChunk == null || nearbyChunk.GetType(i, block.Coords.WorldY + 5, j) != BlockData.Blocks.Air)
                        continue;


                    nearbyChunk.SetBlockAndData(i, block.Coords.WorldY + 5, j, (byte)BlockData.Blocks.Leaves,
                                                    block.MetaData);
                }
        }
    }
}