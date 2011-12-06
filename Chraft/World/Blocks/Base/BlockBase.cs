﻿#region C#raft License
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
using System.Collections.Generic;
using System.Diagnostics;
using Chraft.Entity;
using Chraft.Interfaces;
using Chraft.Net;
using Chraft.Net.Packets;
using Chraft.Plugins.Events.Args;
using Chraft.World.Blocks.Interfaces;
using Chraft.Utils;

namespace Chraft.World.Blocks
{
    /// <summary>
    /// Represents a certain block in the world
    /// </summary>
    public struct StructBlock
    {
        public byte Type;
        public UniversalCoords Coords;
        public byte MetaData;
        public WorldManager World;

        public StructBlock(UniversalCoords coords, byte type, byte metaData, WorldManager world)
        {
            Type = type;
            Coords = coords;
            MetaData = metaData;
            World = world;
        }

        public StructBlock(int worldX, int worldY, int worldZ, byte type, byte metaData, WorldManager world)
        {
            Type = type;
            Coords = UniversalCoords.FromWorld(worldX, worldY, worldZ);
            MetaData = metaData;
            World = world;
        }
        
        public static readonly StructBlock Empty;
        
        public override string ToString()
        {
            return string.Format("Type {0}, Coords {1}", this.Type, this.Coords);
        }
    }

    public abstract class BlockBase : IBlockBase
    {
        /// <summary>
        /// String representation of the block name
        /// </summary>
        public string Name { get; protected set; }
  
        /// <summary>
        /// Gets or sets the block bounds offset, i.e. the MinXYZ/MaxXYZ to apply to the block XYZ to determine the blocks bounding box.
        /// </summary>
        /// <value>
        /// The block bounds offset.
        /// </value>
        public BoundingBox BlockBoundsOffset { get; protected set; }
                    
        /// <summary>
        /// Block type
        /// </summary>
        public BlockData.Blocks Type { get; protected set; }

        /// <summary>
        /// Can we move through the block
        /// </summary>
        public bool IsAir { get; protected set; }

        /// <summary>
        /// Gets or sets a value indicating whether the block is collidable.
        /// </summary>
        /// <value>
        /// <c>true</c> if block is collidable; otherwise, <c>false</c>.
        /// </value>
        public bool IsCollidable { get { return IsSolid; } }

        /// <summary>
        /// Is the block liquid
        /// </summary>
        public bool IsLiquid { get; protected set; }

        /// <summary>
        /// Is the block solid - can be a basement for other blocks
        /// </summary>
        public bool IsSolid { get; protected set; }

        /// <summary>
        /// Opacity of the block where 0x0 is transparent and 0xf is opaque
        /// </summary>
        public byte Opacity { get; protected set; }

        /// <summary>
        /// If the block is opaque or not
        /// </summary>
        public bool IsOpaque
        {
            get { return (Opacity == 0xf); }
        }
  
        /// <summary>
        /// Requires single hit to destroy
        /// </summary>
        public bool IsSingleHit { get; protected set; }

        /// <summary>
        /// Can the greens (but not the crops) grow on it
        /// </summary>
        public bool IsFertile { get; protected set; }

        /// <summary>
        /// Was the block plowed and made suitable for the crops
        /// </summary>
        public bool IsPlowed { get; protected set; }

        /// <summary>
        /// Can the block be burn
        /// </summary>
        public bool IsIgnitable
        {
            get { return (BurnEfficiency > 0); }
        }

        /// <summary>
        /// Block Flammability/Burn Efficiency measured in world ticks (x0.05secs). Value / 20 => number of seconds burn time. 10secs = 1 item smelted
        /// </summary>
        public short BurnEfficiency { get; protected set; }

        /// <summary>
        /// Light emitted by the block
        /// </summary>
        public byte Luminance { get; protected set; }

        public bool IsWaterProof { get; protected set; }

        public List<ItemStack> LootTable { get; protected set; }

        /// <summary>
        /// Base contructor
        /// </summary>
        protected BlockBase()
        {
            Name = "BaseBlock";
            Type = BlockData.Blocks.Air;
            IsAir = false;
            IsLiquid = false;
            Opacity = 0xf;
            IsSolid = false;
            IsSingleHit = false;
            IsFertile = false;
            IsPlowed = false;
            BurnEfficiency = 0;
            LootTable = new List<ItemStack>();
            Luminance = 0;
            IsWaterProof = false;
            BlockBoundsOffset = new BoundingBox(0, 0, 0, 1, 1, 1);
        }

        /// <summary>
        /// Destroy the block
        /// </summary>
        /// <param name="block">block that has been destroyed</param>
        public virtual void Destroy(StructBlock block)
        {
            Destroy(null, block);
        }

        /// <summary>
        /// Destroy the block
        /// </summary>
        /// <param name="entity">entity who destroyed the block</param>
        /// <param name="block">block that has been destroyed</param>
        public virtual void Destroy(EntityBase entity, StructBlock block)
        {
            BlockDestroyEventArgs eventArgs = RaiseDestroyEvent(entity, block);
            if (eventArgs.EventCanceled)
                return;
            
            PlaySoundOnDestroy(entity, block);

            UpdateWorld(block, true);

            DropItems(entity, block);

            DamageItem(entity);

            NotifyNearbyBlocks(entity, block);
        }

        /// <summary>
        /// Removes the block from the world. Don't drop anything.
        /// </summary>
        /// <param name="block">block that is being removed</param>
        public virtual void Remove(StructBlock block)
        {
            UpdateWorld(block, true);
            NotifyNearbyBlocks(null, block);
        }

        /// <summary>
        /// Spawns the block in the world (not placed by the player)
        /// </summary>
        /// <param name="block">block that is being spawned</param>
        public virtual void Spawn(StructBlock block)
        {
            UpdateWorld(block);
            NotifyNearbyBlocks(null, block, false);
        }

        /// <summary>
        /// Notifies the nearby block that the current block has been destroyed
        /// May be used by recipient block to start the physic simulation etc
        /// </summary>
        /// <param name="entity">entity who destroyed the block</param>
        /// <param name="block">block that has been destroyed</param>
        protected virtual void NotifyNearbyBlocks(EntityBase entity, StructBlock block, bool destroyed = true)
        {
            List<UniversalCoords> blocks = new List<UniversalCoords>(6);
            if (block.Coords.WorldY < 127)
                blocks.Add(UniversalCoords.FromWorld(block.Coords.WorldX, block.Coords.WorldY + 1, block.Coords.WorldZ));
            if (block.Coords.WorldY > 0)
                blocks.Add(UniversalCoords.FromWorld(block.Coords.WorldX, block.Coords.WorldY - 1, block.Coords.WorldZ));
            blocks.Add(UniversalCoords.FromWorld(block.Coords.WorldX - 1, block.Coords.WorldY, block.Coords.WorldZ));
            blocks.Add(UniversalCoords.FromWorld(block.Coords.WorldX + 1, block.Coords.WorldY, block.Coords.WorldZ));
            blocks.Add(UniversalCoords.FromWorld(block.Coords.WorldX, block.Coords.WorldY, block.Coords.WorldZ - 1));
            blocks.Add(UniversalCoords.FromWorld(block.Coords.WorldX, block.Coords.WorldY, block.Coords.WorldZ + 1));
            byte blockId = 0;
            byte blockMeta = 0;
            foreach (var coords in blocks)
            {
                Chunk chunk = block.World.GetChunk(coords);

                if (chunk == null)
                    break;

                blockId = (byte)chunk.GetType(coords);
                blockMeta = chunk.GetData(coords);
                if (destroyed)
                    BlockHelper.Instance(blockId).NotifyDestroy(entity, block, new StructBlock(coords, blockId, blockMeta, block.World));
                else
                    BlockHelper.Instance(blockId).NotifyPlace(entity, block, new StructBlock(coords, blockId, blockMeta, block.World));
            }
        }

        /// <summary>
        /// Process the notification about nearby block destruction
        /// </summary>
        /// <param name="entity">entity who destroyed the nearby block</param>
        /// <param name="sourceBlock">block that has been destroyed</param>
        /// <param name="targetBlock">block that recieves the notification</param>
        public virtual void NotifyDestroy(EntityBase entity, StructBlock sourceBlock, StructBlock targetBlock)
        { }

        public virtual void NotifyPlace(EntityBase entity, StructBlock sourceBlock, StructBlock targetBlock)
        { }

        /// <summary>
        /// Called when the entity touches the block - pressure plates, proximity sensors etc
        /// </summary>
        /// <param name="entity">entity who touched the block</param>
        /// <param name="block">block that has been touched</param>
        public virtual void Touch(EntityBase entity, StructBlock block, BlockFace face) { }

        /// <summary>
        /// Places the block
        /// </summary>
        /// <param name="block">block that is being placed</param>
        /// <param name="targetBlock">block that is being targeted (aimed)</param>
        /// <param name="face">side of the target block</param>
        public virtual void Place(StructBlock block, StructBlock targetBlock, BlockFace face)
        {
            Place(null, block, targetBlock, face);
        }
        /// <summary>
        /// Places the block
        /// </summary>
        /// <param name="entity">entity who placed the block</param>
        /// <param name="block">block that is being placed</param>
        /// <param name="targetBlock">block that is being targeted (aimed)</param>
        /// <param name="face">side of the target block</param>
        public virtual void Place(EntityBase entity, StructBlock block, StructBlock targetBlock, BlockFace face)
        {
            if (!CanBePlacedOn(entity, block, targetBlock, face) || !RaisePlaceEvent(entity, block))
            {
                // Revert the change since the client has already graphically placed the block
                if(entity is Player)
                {
                    Player player = entity as Player;
                    player.Server.SendPacketToNearbyPlayers(player.World, player.Position, new BlockChangePacket{Data = targetBlock.MetaData, Type = targetBlock.Type, X = targetBlock.Coords.WorldX, Y = (sbyte)targetBlock.Coords.WorldY, Z = targetBlock.Coords.WorldZ});
                }
                return;
            }

            UpdateWorld(block);
            RemoveItem(entity);
            NotifyNearbyBlocks(entity, block, false);
        }

        /// <summary>
        /// Raises the block destruction event
        /// </summary>
        /// <param name="entity">entity who destroyed the block</param>
        /// <param name="block">block that has been destroyed</param>
        /// <returns>resulting event args</returns>
        protected virtual BlockDestroyEventArgs RaiseDestroyEvent(EntityBase entity, StructBlock block)
        {
            BlockDestroyEventArgs e = new BlockDestroyEventArgs(this, entity);
            block.World.Server.PluginManager.CallEvent(Plugins.Events.Event.BlockDestroy, e);
            return e;
        }

        /// <summary>
        /// Raises the block placement event
        /// </summary>
        /// <param name="entity">entity who placed the block</param>
        /// <param name="block">block that has been placed</param>
        /// <returns>resulting event args</returns>
        protected virtual bool RaisePlaceEvent(EntityBase entity, StructBlock block)
        {
            BlockPlaceEventArgs e = new BlockPlaceEventArgs(this, entity);
            block.World.Server.PluginManager.CallEvent(Plugins.Events.Event.BlockPlace, e);
            // Destruction made not by the living can not be interrupted?
            if (entity == null)
                return true;
            return !e.EventCanceled;
        }

        /// <summary>
        /// Plays the sound on block destruction
        /// </summary>
        /// <param name="entity">entity that destroyed the block</param>
        /// <param name="block">block that has been destroyed</param>
        protected virtual void PlaySoundOnDestroy(EntityBase entity, StructBlock block)
        {
            foreach (Client c in block.World.Server.GetNearbyPlayers(block.World, block.Coords))
            {
                if (c.Owner == entity)
                    continue;

                c.SendPacket(new SoundEffectPacket
                {
                    EffectID = SoundEffectPacket.SoundEffect.BLOCK_BREAK,
                    X = block.Coords.WorldX,
                    Y = block.Coords.WorldY,
                    Z = block.Coords.WorldZ,
                    SoundData = block.Type
                });
            }
        }

        /// <summary>
        /// Updates world data upon block destruction
        /// </summary>
        /// <param name="block">block that has been destroyed</param>
        protected virtual void UpdateWorld(StructBlock block, bool isDestroyed = false)
        {
            byte newType = (isDestroyed ? (byte)BlockData.Blocks.Air : block.Type);
            byte newMeta = (isDestroyed ? (byte)0 : block.MetaData);
            block.World.SetBlockAndData(block.Coords, newType, newMeta);
            RecalculateChunkValues(block);
        }

        public virtual void RecalculateChunkValues(StructBlock block)
        {
            Chunk chunk = GetBlockChunk(block);

            if (chunk == null)
                return;
            byte blockX, blockY, blockZ;

            blockX = (byte)block.Coords.BlockX;
            blockY = (byte)block.Coords.BlockY;
            blockZ = (byte)block.Coords.BlockZ;

            byte oldHeight = chunk.HeightMap[blockX, blockZ];

            if (blockY + 1 >= chunk.HeightMap[blockX, blockZ])
                chunk.RecalculateHeight(block.Coords);
#if PROFILE
            Stopwatch watch = new Stopwatch();
            watch.Start();
#endif
            //chunk.SpreadLightFromBlock(blockX, blockY, blockZ, chunk.GetBlockLight(blockX, blockY, blockZ), oldHeight);
#if PROFILE
            watch.Stop();

            block.World.Logger.Log(Logger.LogLevel.Info, "Block skylight recalc: {0}ms", watch.ElapsedMilliseconds);
#endif
            block.World.Update(block.Coords, false);
        }

        /// <summary>
        /// Invoked to drop the loot after block destruction
        /// </summary>
        /// <param name="block">block that has been destroyed</param>
        protected virtual void DropItems(StructBlock block, List<ItemStack> overridedLoot = null)
        {
            DropItems(null, block, overridedLoot);
        }

        /// <summary>
        /// Invoked to drop the loot after block destruction
        /// </summary>
        /// <param name="entity">entity that destroyed the block</param>
        /// <param name="block">block that has been destroyed</param>
        protected virtual void DropItems(EntityBase entity, StructBlock block, List<ItemStack> overridedLoot = null)
        {
            List<ItemStack> toDrop;
            if (overridedLoot != null && overridedLoot.Count > 0)
                toDrop = overridedLoot;
            else if (LootTable != null && LootTable.Count > 0)
                toDrop = LootTable;
            else
                return;
            foreach (var lootEntry in toDrop)
            {
                if (lootEntry.Count > 0)
                    block.World.Server.DropItem(block.World, block.Coords, lootEntry);
            }
        }

        /// <summary>
        /// Removes the active item from inventory when block is placed
        /// </summary>
        /// <param name="entity">the entity who placed the block</param>
        protected virtual void RemoveItem(EntityBase entity)
        {
            Player player = entity as Player;
            if (player != null && player.GameMode == 0)
                player.Inventory.RemoveItem(player.Inventory.ActiveSlot);
        }

        /// <summary>
        /// Damages the active item in the inventory when the block is destroyed
        /// </summary>
        /// <param name="entity">the entity who destroyed the block</param>
        protected virtual void DamageItem(EntityBase entity)
        {
            Player player = entity as Player;
            if (player != null && player.GameMode == 0)
                player.Inventory.DamageItem(player.Inventory.ActiveSlot);
        }

        /// <summary>
        /// Checks if the block can be placed next to the target one
        /// </summary>
        /// <param name="who">the entity who places the block</param>
        /// <param name="block">the block being placed</param>
        /// <param name="targetBlock">the block being targeted (aimed)</param>
        /// <param name="targetSide">the side of the target block</param>
        /// <returns>true if the block can be placed, false otherwise</returns>
        protected virtual bool CanBePlacedOn(EntityBase who, StructBlock block, StructBlock targetBlock, BlockFace targetSide)
        {
            if (!BlockHelper.IsSolid(targetBlock.Type))
                return false;

            byte? originalBlock = block.World.GetBlockId(block.Coords);

            if ( originalBlock == null || (originalBlock != (byte)BlockData.Blocks.Air &&
                originalBlock != (byte)BlockData.Blocks.Water &&
                originalBlock != (byte)BlockData.Blocks.Still_Water &&
                originalBlock != (byte)BlockData.Blocks.Lava &&
                originalBlock != (byte)BlockData.Blocks.Still_Lava))
                return false;

            if (!BlockHelper.IsAir(block.Type) && !BlockHelper.IsLiquid(block.Type))
                foreach (EntityBase entity in block.World.Server.GetNearbyEntities(block.World, UniversalCoords.ToAbsWorld(block.Coords)))
                {
                    LivingEntity living = entity as LivingEntity;
                    if (living == null)
                        continue;

                    if (living.BoundingBox.IntersectsWith(GetCollisionBoundingBox(block)))
                        return false;
                }

            return true;
        }

        /// <summary>
        /// Gets the collision bounding box for the provided location.
        /// </summary>
        /// <returns>
        /// The collision bounding box.
        /// </returns>
        /// <param name='coords'>
        /// Coords.
        /// </param>
        public BoundingBox GetCollisionBoundingBox(StructBlock block)
        {
            UniversalCoords coords = block.Coords;
            return new BoundingBox(coords.WorldX + BlockBoundsOffset.Minimum.X, coords.WorldY + BlockBoundsOffset.Minimum.Y, coords.WorldZ + BlockBoundsOffset.Minimum.Z,
                                   coords.WorldX + BlockBoundsOffset.Maximum.X, coords.WorldY + BlockBoundsOffset.Maximum.Y, coords.WorldZ + BlockBoundsOffset.Maximum.Z);
        }
        
        public RayTraceHitBlock RayTraceIntersection(StructBlock block, Vector3 start, Vector3 end)
        {
            BoundingBox boundingBox = GetCollisionBoundingBox(block);
            
            RayTraceHit rayTraceHit = boundingBox.RayTraceIntersection(start, end);
            if (rayTraceHit != null)
                return new RayTraceHitBlock(block.Coords, rayTraceHit.FaceHit, rayTraceHit.Hit);
            
            return null;          
        }

        public Chunk GetBlockChunk(StructBlock block)
        {
            return block.World.GetChunk(block.Coords);
        }
    }
}
