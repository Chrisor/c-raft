#region C#raft License
// This file is part of C#raft. Copyright C#raft Team 
// 
// C#raft is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as
// published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.
// 
// You should have received a copy of the GNU Affero General Public License
// along with this program. If not, see <http://www.gnu.org/licenses/>.
#endregion
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Chraft.Entity;
using Chraft.Interfaces;
using Chraft.Plugins.Events.Args;

namespace Chraft.World.Blocks
{
    class BlockDeadBush : BlockBase
    {
        public BlockDeadBush()
        {
            Name = "DeadBush";
            Type = BlockData.Blocks.DeadBush;
            IsSingleHit = true;
            IsAir = true;
            IsSolid = true;
            Opacity = 0x0;
            BlockBoundsOffset = new BoundingBox(0.1, 0, 0.1, 0.9, 0.8, 0.9);
        }

        public override void Place(EntityBase entity, StructBlock block, StructBlock targetBlock, BlockFace face)
        {
            if (face == BlockFace.Down)
                return;
            byte? blockId = targetBlock.World.GetBlockId(UniversalCoords.FromWorld(block.Coords.WorldX, block.Coords.WorldY - 1, block.Coords.WorldZ));
            // We can place the dead bush only on the sand
            if (blockId == null || blockId != (byte)BlockData.Blocks.Sand)
                return;
            // We can place the dead bush only on top of the sand block
            if (targetBlock.Type != (byte)BlockData.Blocks.Sand || face != BlockFace.Up)
                return;
            base.Place(entity, block, targetBlock, face);
        }

        public override void NotifyDestroy(EntityBase entity, StructBlock sourceBlock, StructBlock targetBlock)
        {
            if ((targetBlock.Coords.WorldY - sourceBlock.Coords.WorldY) == 1 &&
                targetBlock.Coords.WorldX == sourceBlock.Coords.WorldX &&
                targetBlock.Coords.WorldZ == sourceBlock.Coords.WorldZ)
                Destroy(targetBlock);
            base.NotifyDestroy(entity, sourceBlock, targetBlock);
        }

    }
}
