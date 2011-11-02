﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Chraft.Entity;
using Chraft.Interfaces;
using Chraft.Net;
using Chraft.Plugins.Events.Args;

namespace Chraft.World.Blocks
{
    class BlockRedMushroom : BlockBaseMushroom
    {
        public BlockRedMushroom()
        {
            Name = "RedMushroom";
            Type = BlockData.Blocks.Red_Mushroom;
            LootTable.Add(new ItemStack((short)Type, 1));
        }

        public override void Fertilize(EntityBase entity, StructBlock block)
        {
            Chunk chunk = GetBlockChunk(block);
            if (chunk == null)
                return;

            BlockData.Blocks blockBelow = chunk.GetType(block.Coords.BlockX, block.Coords.BlockY - 1,
                                                     block.Coords.BlockZ);

            if (blockBelow != BlockData.Blocks.Dirt && blockBelow != BlockData.Blocks.Grass &&
                blockBelow != BlockData.Blocks.Mycelium)
                return;

            int stemHeight = block.World.Server.Rand.Next(3) + 4;
            int capY = block.Coords.WorldY + stemHeight + 1;
            if (capY > 127)
                return;

            BlockData.Blocks blockId;
            for (int dY = block.Coords.WorldY + 1; dY < capY - 1; dY++)
            {
                blockId = chunk.GetType(block.Coords.BlockX, dY, block.Coords.BlockZ);
                if (blockId != BlockData.Blocks.Air && blockId != BlockData.Blocks.Leaves)
                    return;
            }

            int absdX, absdZ;

            byte? nullableBlockId;
            for (int dX = -2; dX < 3; dX++)
                for (int dZ = -2; dZ < 3; dZ++)
                    for (int dY = block.Coords.WorldY + 1; dY <= capY; dY++)
                    {
                        absdX = Math.Abs(dX);
                        absdZ = Math.Abs(dZ);
                        if (absdX == 2 && absdZ == 2)
                            continue;
                        if (dY == capY && absdX > 1 && absdZ > 1)
                            continue;
                        if (dY < capY && absdX < 2 && absdZ < 2)
                            continue;

                        nullableBlockId = block.World.GetBlockId(block.Coords.WorldX + dX, dY, block.Coords.WorldZ + dZ);
                        if (nullableBlockId == null || (nullableBlockId != (byte)BlockData.Blocks.Leaves && nullableBlockId != (byte)BlockData.Blocks.Air))
                            return;
                    }

            byte metaData = (byte)MetaData.HugeMushroom.NorthWeastSouthEast;
            for (int dY = block.Coords.WorldY; dY < capY; dY++)
                if (chunk.GetType(block.Coords.BlockX, dY, block.Coords.BlockZ) != BlockData.Blocks.Leaves)
                    chunk.SetBlockAndData(block.Coords.BlockX, dY, block.Coords.BlockZ, (byte)BlockData.Blocks.BrownMushroomCap, metaData);

            for (int dX = -2; dX < 3; dX++)
                for (int dZ = -2; dZ < 3; dZ++)
                {
                    Chunk currentChunk = block.World.GetChunkFromWorld(block.Coords.WorldX + dX,
                                                                       block.Coords.WorldZ + dZ, false, false);

                    if(currentChunk == null)
                        continue;

                    for (int dY = capY - 3; dY <= capY; dY++)
                    {
                        absdX = Math.Abs(dX);
                        absdZ = Math.Abs(dZ);
                        if (absdX == 2 && absdZ == 2)
                            continue;
                        if (dY == capY && (absdX > 1 || absdZ > 1))
                            continue;
                        if (dY < capY && absdX < 2 && absdZ < 2)
                            continue;
                        blockId = currentChunk.GetType(block.Coords.BlockX + dX, dY, block.Coords.BlockZ + dZ);
                        if (blockId == BlockData.Blocks.Leaves)
                            continue;

                        if (dY == capY)
                        {
                            // Draw cap
                            if (dX == 0 && dZ == 0)
                                metaData = (byte) MetaData.HugeMushroom.Top;
                            else if (dX == -1 && dZ == 0)
                                metaData = (byte) MetaData.HugeMushroom.TopWest;
                            else if (dX == 1 && dZ == 0)
                                metaData = (byte) MetaData.HugeMushroom.TopEast;
                            else if (dX == 0 && dZ == -1)
                                metaData = (byte) MetaData.HugeMushroom.TopNorth;
                            else if (dX == 0 && dZ == 1)
                                metaData = (byte) MetaData.HugeMushroom.TopSouth;
                            else if (dX == -1 && dZ == -1)
                                metaData = (byte) MetaData.HugeMushroom.TopNorthWest;
                            else if (dX == -1 && dZ == 1)
                                metaData = (byte) MetaData.HugeMushroom.TopSouthWest;
                            else if (dX == 1 && dZ == -1)
                                metaData = (byte) MetaData.HugeMushroom.TopNorthEast;
                            else if (dX == 1 && dZ == 1)
                                metaData = (byte) MetaData.HugeMushroom.TopSouthEast;
                        }
                        else
                        {
                            // Draw sides
                            if (dX == -2 && dZ == -1)
                                metaData = (byte) MetaData.HugeMushroom.TopNorthWest;
                            else if (dX == -2 && dZ == 0)
                                metaData = (byte) MetaData.HugeMushroom.TopWest;
                            else if (dX == -2 && dZ == 1)
                                metaData = (byte) MetaData.HugeMushroom.TopSouthWest;
                            else if (dX == 2 && dZ == -1)
                                metaData = (byte) MetaData.HugeMushroom.TopNorthEast;
                            else if (dX == 2 && dZ == 0)
                                metaData = (byte) MetaData.HugeMushroom.TopEast;
                            else if (dX == 2 && dZ == 1)
                                metaData = (byte) MetaData.HugeMushroom.TopSouthEast;
                            else if (dX == -1 && dZ == 2)
                                metaData = (byte) MetaData.HugeMushroom.TopSouthWest;
                            else if (dX == 0 && dZ == 2)
                                metaData = (byte) MetaData.HugeMushroom.TopSouth;
                            else if (dX == 1 && dZ == 2)
                                metaData = (byte) MetaData.HugeMushroom.TopSouthEast;
                            else if (dX == -1 && dZ == -2)
                                metaData = (byte) MetaData.HugeMushroom.TopNorthWest;
                            else if (dX == 0 && dZ == -2)
                                metaData = (byte) MetaData.HugeMushroom.TopNorth;
                            else if (dX == 1 && dZ == -2)
                                metaData = (byte) MetaData.HugeMushroom.TopNorthEast;
                        }

                        currentChunk.SetBlockAndData(block.Coords.BlockX + dX, dY, block.Coords.BlockZ + dZ,
                                                    (byte) BlockData.Blocks.RedMushroomCap, metaData);
                    }
                }
        }
    }
}
