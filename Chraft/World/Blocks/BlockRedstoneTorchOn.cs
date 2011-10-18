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
    class BlockRedstoneTorchOn : BlockBase
    {
        public BlockRedstoneTorchOn()
        {
            Name = "RedstoneTorchOn";
            Type = BlockData.Blocks.Redstone_Torch_On;
            IsAir = true;
            IsSingleHit = true;
            Luminance = 0x7;
            LootTable.Add(new ItemStack((short)BlockData.Blocks.Redstone_Torch, 1));
            Opacity = 0x0;
        }

        public override void Place(EntityBase entity, StructBlock block, StructBlock targetBlock, BlockFace face)
        {
            LivingEntity living = (entity as LivingEntity);
            if (living == null)
                return;

            switch (face)
            {
                case BlockFace.Down: return;
                case BlockFace.Up: block.MetaData = (byte)MetaData.Torch.Standing;
                    break;
                case BlockFace.West: block.MetaData = (byte)MetaData.Torch.West;
                    break;
                case BlockFace.East: block.MetaData = (byte)MetaData.Torch.East;
                    break;
                case BlockFace.North: block.MetaData = (byte)MetaData.Torch.North;
                    break;
                case BlockFace.South: block.MetaData = (byte)MetaData.Torch.South;
                    break;
            }

            base.Place(entity, block, targetBlock, face);
        }

        public override void NotifyDestroy(EntityBase entity, StructBlock sourceBlock, StructBlock targetBlock)
        {
            if (targetBlock.Coords.WorldY > sourceBlock.Coords.WorldY && targetBlock.MetaData == (byte)MetaData.Torch.Standing ||
                targetBlock.Coords.WorldX > sourceBlock.Coords.WorldX && targetBlock.MetaData == (byte)MetaData.Torch.South ||
                targetBlock.Coords.WorldX < sourceBlock.Coords.WorldX && targetBlock.MetaData == (byte)MetaData.Torch.North ||
                targetBlock.Coords.WorldZ > sourceBlock.Coords.WorldZ && targetBlock.MetaData == (byte)MetaData.Torch.West ||
                targetBlock.Coords.WorldZ < sourceBlock.Coords.WorldZ && targetBlock.MetaData == (byte)MetaData.Torch.East)
                Destroy(targetBlock);
            base.NotifyDestroy(entity, sourceBlock, targetBlock);
        }
    }
}
