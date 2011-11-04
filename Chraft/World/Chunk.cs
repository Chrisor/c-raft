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
using System.IO;
using System.Threading;
using Chraft.Net;
using Chraft.Properties;
using Chraft.World.Blocks;
using Chraft.World.Blocks.Interfaces;
using Ionic.Zlib;
using Chraft.World.Weather;
using System.Collections;
using System.Diagnostics;
using System.Collections.Concurrent;
using Chraft.Net.Packets;

namespace Chraft.World
{
    public class Chunk : ChunkBase
    {
        private static object _SavingLock = new object();
        private static volatile bool Saving = false;

        public bool IsRecalculating {get; set;}
        public volatile bool Deleted;

        private int MaxHeight;

        public byte[,] HeightMap { get; private set; }
        public string DataFile { get { return World.Folder + "/x" + Coords.ChunkX + "_z" + Coords.ChunkZ + ".gz"; } }
        public bool Persistent { get; set; }
        public DateTime CreationDate;

        private ConcurrentDictionary<short, short> BlocksUpdating = new ConcurrentDictionary<short, short>();

        private ConcurrentDictionary<short, short> GrowableBlocks = new ConcurrentDictionary<short, short>();

        public ConcurrentDictionary<short, string> SignsText = new ConcurrentDictionary<short, string>();

        internal Chunk(WorldManager world, UniversalCoords coords)
            : base(world, coords)
        {
           
        }

        internal void InitBlockChangesTimer()
        {
            _UpdateTimer = new Timer(UpdateBlocksToNearbyPlayers, null, Timeout.Infinite, Timeout.Infinite);
        }

        public void Recalculate()
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();
            IsRecalculating = true;
            //Console.WriteLine("Recalculating for: {0}, {1}, Thread: {2}", X, Z, Thread.CurrentThread.ManagedThreadId);
            RecalculateHeight();
            RecalculateSky();
            SpreadSkyLight();

            while (World.ChunksToRecalculate.Count > 0)
            {
                ChunkLightUpdate chunkUpdate;
                World.ChunksToRecalculate.TryDequeue(out chunkUpdate);
                if (chunkUpdate != null && chunkUpdate.Chunk != null && !chunkUpdate.Chunk.Deleted)
                {
                    chunkUpdate.Chunk.StackSize = 0;
                    if (chunkUpdate.X == -1)
                        chunkUpdate.Chunk.SpreadSkyLight();
                    else
                        chunkUpdate.Chunk.SpreadSkyLightFromBlock((byte)chunkUpdate.X, (byte)chunkUpdate.Y, (byte)chunkUpdate.Z);
                } 
            }

            sw.Stop();

            //Console.WriteLine("Chunk ({0},{1}): {2}", Coords.ChunkX, Coords.ChunkZ, sw.ElapsedMilliseconds);
            //Console.WriteLine("Scheduled: {0}, {1}, Thread: {2}", X, Z, Thread.CurrentThread.ManagedThreadId);
        }

        public void SpreadSkyLight()
        {
            
            for (int x = 0; x < 16; ++x)
            {
                for (int z = 0; z < 16; ++z)
                {
                    byte y = HeightMap[x, z];
                    
                    SpreadSkyLightFromBlock((byte)x, y, (byte)z);
                }
            }
            
        }

        public void RecalculateLight()
        {
            
        }

        public void RecalculateHeight()
        {
            MaxHeight = 127;
            HeightMap = new byte[16, 16];
            for (int x = 0; x < 16; x++)
            {
                for (int z = 0; z < 16; z++)
                    RecalculateHeight(x, z);
            }
        }

        public void RecalculateHeight(UniversalCoords coords)
        {
            RecalculateHeight(coords.BlockX, coords.BlockZ);
        }

        public void RecalculateHeight(int x, int z)
        {
            int height;
            for (height = 127; height > 0 && GetOpacity(x, height - 1, z) == 0; height--) ;
            HeightMap[x, z] = (byte)height;

            if (height < MaxHeight)
                MaxHeight = height;
        }

        public void RecalculateSky()
        {
            for (int x = 0; x < 16; x++)
            {
                for (int z = 0; z < 16; z++)
                {
                    RecalculateSky(x, z);
                }
            }
        }

        public void RecalculateSky(int x, int z)
        {
            int sky = 15;
            int y = 127;
            do
            {
                sky -= GetOpacity(x, y, z);

                if (sky < 0)
                    sky = 0;
                SkyLight.setNibble(x, y, z, (byte)sky);
            }
            while (--y > 0 && sky > 0);
        }

        public int StackSize;

        public void SpreadSkyLightFromBlock(byte x, byte y, byte z)
        {
            if (StackSize > 200)
            {
                World.ChunksToRecalculate.Enqueue(new ChunkLightUpdate(this, x, y, z));
                Console.WriteLine("Rescheduling chunk");
                return;
            }
            BitArray directionChunkExist = new BitArray(4);
            directionChunkExist.SetAll(false);

            byte[] skylights = new byte[7]{0,0,0,0,0,0,0};

            skylights[0] = (byte)SkyLight.getNibble(x,y,z);

            int newSkylight = skylights[0];
            byte chunkX = (byte)Coords.ChunkX;
            byte chunkZ = (byte) Coords.ChunkZ;
            // Take the skylight value of our neighbor blocks
            if (x > 0)
                skylights[1] = (byte)SkyLight.getNibble((x - 1), y, z);
            else if (World.ChunkExists(chunkX - 1, chunkZ))
            {
                skylights[1] = (byte)World.GetChunkFromChunk(chunkX - 1, chunkZ, false, true).SkyLight.getNibble((x - 1) & 0xf, y, z);
                directionChunkExist[0] = true;
            }

            if (x < 15)
                skylights[2] = (byte)SkyLight.getNibble(x + 1, y, z);
            else if (World.ChunkExists(chunkX + 1, chunkZ))
            {
                skylights[2] = (byte)World.GetChunkFromChunk(chunkX + 1, chunkZ, false, true).SkyLight.getNibble((x + 1) & 0xf, y, z);
                directionChunkExist[1] = true;
            }

            if (z > 0)
                skylights[3] = (byte)SkyLight.getNibble(x, y, z - 1);
            else if (World.ChunkExists(chunkX, chunkZ - 1))
            {
                skylights[3] = (byte)World.GetChunkFromChunk(chunkX, chunkZ - 1, false, true).SkyLight.getNibble(x, y, (z - 1) & 0xf);
                directionChunkExist[2] = true;
            }

            if (z < 15)
                skylights[4] = (byte)SkyLight.getNibble(x, y, z + 1);
            else if (World.ChunkExists(chunkX, chunkZ + 1))
            {
                skylights[4] = (byte)World.GetChunkFromChunk(chunkX, chunkZ + 1, false, true).SkyLight.getNibble(x, y, (z + 1) & 0xf);
                directionChunkExist[3] = true;
            }

            skylights[5] = (byte)SkyLight.getNibble(x, y + 1, z);

            if (y > 0)
                skylights[6] = (byte)SkyLight.getNibble(x, y - 1, z);


            if (HeightMap == null)
                Console.WriteLine("null: {0}, {1} {2}", chunkX, chunkZ, Thread.CurrentThread.ManagedThreadId);

            byte vertical = 0;
            if(HeightMap[x,z] > y)
            {
                if (skylights[1] > newSkylight)
                    newSkylight = skylights[1];
                    
                if (skylights[2] > newSkylight)
                    newSkylight = skylights[2];
                    
                if (skylights[3] > newSkylight)
                    newSkylight = skylights[3];
                    
                if (skylights[4] > newSkylight)
                    newSkylight = skylights[4];

                if (skylights[5] > newSkylight)
                {
                    newSkylight = skylights[5];
                    vertical = 1;
                }
                    
                if (skylights[6] > newSkylight)
                {
                    newSkylight = skylights[6];
                    vertical = 1;
                }
            }

            if (HeightMap[x, z] <= y)
                newSkylight = 15;
            else
            {
                byte toSubtract = (byte)(1 - vertical + BlockHelper.Instance(Types[x << 11 | z << 7 | y]).Opacity);
                newSkylight -= toSubtract;

                if (newSkylight < 0)
                    newSkylight = 0;
            }

            if (skylights[0] != newSkylight)
                SetSkyLight(x, y, z, (byte)newSkylight);

            --newSkylight;

            if (newSkylight < 0)
                newSkylight = 0;

            // Then spread the light to our neighbor if the has lower skylight value
            byte neighborCoord;
            
            neighborCoord = (byte)(x - 1);

            if (x > 0)
            {
                if (skylights[1] < newSkylight)
                {
                    ++StackSize;
                    SpreadSkyLightFromBlock(neighborCoord, y, z);
                    --StackSize;
                }
            }
            else if (directionChunkExist[0])
            {
                // Reread Skylight value since it can be changed in the meanwhile
                skylights[0] = (byte)World.GetChunkFromChunk(chunkX - 1, chunkZ, false, true).SkyLight.getNibble(neighborCoord & 0xf, y, z);

                if (skylights[0] < newSkylight)
                    World.ChunksToRecalculate.Enqueue(new ChunkLightUpdate(World.GetChunkFromChunk(chunkX - 1, chunkZ, false, true), neighborCoord & 0xf, y, z));
            }

            neighborCoord = (byte)(z - 1);

            if (z > 0)
            {
                // Reread Skylight value since it can be changed in the meanwhile
                skylights[0] = (byte)SkyLight.getNibble(x, y, neighborCoord);

                if (skylights[0] < newSkylight)
                {
                    ++StackSize;
                    SpreadSkyLightFromBlock(x, y, neighborCoord);
                    --StackSize;
                }
            }
            else if (directionChunkExist[2])
            {
                // Reread Skylight value since it can be changed in the meanwhile
                skylights[0] = (byte)World.GetChunkFromChunk(chunkX, chunkZ - 1, false, true).SkyLight.getNibble(x, y, neighborCoord & 0xf);
                if (skylights[0] < newSkylight)
                    World.ChunksToRecalculate.Enqueue(new ChunkLightUpdate(World.GetChunkFromChunk(chunkX, chunkZ - 1, false, true), x, y, neighborCoord & 0xf));
            }

            // Reread Skylight value since it can be changed in the meanwhile
            
            if (y > 0)
            {
                skylights[0] = (byte)SkyLight.getNibble(x, y - 1, z);
                if (skylights[0] < newSkylight)
                {
                    if (y < 50)
                        Console.WriteLine("Big hole in {0} {1} {2}", x + (chunkX * 16), y, z + (chunkZ * 16));
                    ++StackSize;
                    SpreadSkyLightFromBlock(x, (byte)(y - 1), z);
                    --StackSize;
                }
            }

            neighborCoord = (byte)(x + 1);

            if (x < 15)
            {

                // Reread Skylight value since it can be changed in the meanwhile
                skylights[0] = (byte)SkyLight.getNibble(neighborCoord, y, z);

                if (skylights[0] < newSkylight)
                {
                    ++StackSize;
                    SpreadSkyLightFromBlock(neighborCoord, y, z);
                    --StackSize;
                }
            }
            else if (directionChunkExist[1])
            {
                // Reread Skylight value since it can be changed in the meanwhile
                skylights[0] = (byte)World.GetChunkFromChunk(chunkX + 1, chunkZ, false, true).SkyLight.getNibble(neighborCoord & 0xf, y, z);

                if (skylights[0] < newSkylight)
                    World.ChunksToRecalculate.Enqueue(new ChunkLightUpdate(World.GetChunkFromChunk(chunkX + 1, chunkZ, false, true), neighborCoord & 0xf, y, z));
            }

            neighborCoord = (byte)(z + 1);

            if (z < 15)
            {
                // Reread Skylight value since it can be changed in the meanwhile
                skylights[0] = (byte)SkyLight.getNibble(x, y, neighborCoord);

                if (skylights[0] < newSkylight)
                {
                    ++StackSize;
                    SpreadSkyLightFromBlock(x, y, neighborCoord);
                    --StackSize;
                }
            }
            else if (directionChunkExist[3])
            {
                // Reread Skylight value since it can be changed in the meanwhile
                skylights[0] = (byte)World.GetChunkFromChunk(chunkX, chunkZ + 1, false, true).SkyLight.getNibble(x, y, neighborCoord & 0xf);
                if (skylights[0] < newSkylight)
                    World.ChunksToRecalculate.Enqueue(new ChunkLightUpdate(World.GetChunkFromChunk(chunkX, chunkZ + 1, false, true), x, y, neighborCoord & 0xf));
            }

            if (y < HeightMap[x, z])
            {
                // Reread Skylight value since it can be changed in the meanwhile
                skylights[0] = (byte)SkyLight.getNibble(x, y + 1, z);
                if(skylights[0] < newSkylight)
                {
                    ++StackSize;
                    SpreadSkyLightFromBlock(x, (byte)(y + 1), z);
                    --StackSize;
                }
            } 
        }

        private bool CanLoad()
        {
            return Settings.Default.LoadFromSave && File.Exists(DataFile);
        }

        public bool Load()
        {
            if (!CanLoad())
                return false;

            Stream zip = null;
            Monitor.Enter(_SavingLock);
            try
            {
                zip = new DeflateStream(File.Open(DataFile, FileMode.Open), CompressionMode.Decompress);
                HeightMap = new byte[16, 16];
                for (int x = 0; x < 16; ++x)
                {
                    for (int z = 0; z < 16; ++z)
                    {
                        HeightMap[x, z] = (byte)zip.ReadByte();
                    }
                }
                LoadAllBlocks(zip);
                return true;
            }
            catch (Exception ex)
            {
                World.Logger.Log(ex);
                return false;
            }
            finally
            {
                Monitor.Exit(_SavingLock);
                if (zip != null)
                    zip.Dispose();

                (BlockHelper.Instance((byte) BlockData.Blocks.Sign_Post) as BlockSignBase).LoadSignsFromDisk(this, World.SignsFolder);
            }
        }

        private void LoadAllBlocks(Stream strm)
        {
            for (int x = 0; x < 16; x++)
            {
                for (int y = 0; y < 128; y++)
                {
                    for (int z = 0; z < 16; z++)
                        LoadBlock(x, y, z, strm);
                }
            }
        }

        private void LoadBlock(int x, int y, int z, Stream strm)
        {
            byte type = (byte)strm.ReadByte();
            byte data = (byte)strm.ReadByte();
            byte ls = (byte)strm.ReadByte();
            this[x, y, z] = type;
            SetData(x, y, z, data, false);
            SetDualLight(x, y, z, ls);
        }

        private bool EnterSave()
        {
            Monitor.Enter(_SavingLock);
            if (Saving)
                return false;
            Saving = true;
            return true;
        }

        private void ExitSave()
        {
            Saving = false;
            Monitor.Exit(_SavingLock);
        }

        private void WriteBlock(int x, int y, int z, Stream strm)
        {
            strm.WriteByte(this[x, y, z]);
            strm.WriteByte(GetData(x, y, z));
            strm.WriteByte(GetDualLight(x, y, z));
        }

        private void WriteAllBlocks(Stream strm)
        {
            for (int x = 0; x < 16; x++)
            {
                for (int y = 0; y < 128; y++)
                {
                    for (int z = 0; z < 16; z++)
                        WriteBlock(x, y, z, strm);
                }
            }
        }

        public override void Save()
        {
            if (!EnterSave())
                return;

            Stream zip = new DeflateStream(File.Create(DataFile + ".tmp"), CompressionMode.Compress);
            try
            {
                for (int x = 0; x < 16; ++x)
                {
                    for (int z = 0; z < 16; ++z)
                    {
                        zip.WriteByte(HeightMap[x, z]);
                    }
                }
                WriteAllBlocks(zip);
                zip.Flush();
            }
            finally
            {
                try
                {
                    zip.Dispose();
                    File.Delete(DataFile);
                    File.Move(DataFile + ".tmp", DataFile);
                }
                catch
                {
                }
                finally
                {
                    ExitSave();
                }
            }
        }

        internal void AddClient(Client client)
        {
            lock (Clients)
                Clients.Add(client);
            lock (Entities)
                Entities.Add(client.Owner);
        }

        internal void RemoveClient(Client client)
        {
            lock (Clients)
                Clients.Remove(client);
            lock (Entities)
                Entities.Remove(client.Owner);

            if (Clients.Count == 0 && !Persistent)
            {
                Save();
                World.RemoveChunk(this);
            }
        }

        public override void OnSetType(UniversalCoords coords, BlockData.Blocks value)
        {
            base.OnSetType(coords, value);
            byte blockId = (byte)value;

            if (GrowableBlocks.ContainsKey(coords.BlockPackedCoords))
            {
                short unused;

                if (!BlockHelper.IsGrowable(blockId))
                {
                    GrowableBlocks.TryRemove(coords.BlockPackedCoords, out unused);
                }
                else
                {
                    StructBlock block = new StructBlock(coords, blockId, GetData(coords), World);
                    if (!(BlockHelper.Instance(blockId) as IBlockGrowable).CanGrow(block, this))
                    {
                        GrowableBlocks.TryRemove(coords.BlockPackedCoords, out unused);
                    }
                }
            }
            else
            {
                if (BlockHelper.IsGrowable(blockId))
                {
                    StructBlock block = new StructBlock(coords, blockId, GetData(coords), World);
                    if ((BlockHelper.Instance(blockId) as IBlockGrowable).CanGrow(block, this))
                    {
                        GrowableBlocks.TryAdd(coords.BlockPackedCoords, coords.BlockPackedCoords);
                    }
                }
            }
        }

        public override void OnSetType(int blockX, int blockY, int blockZ, BlockData.Blocks value)
        {
            base.OnSetType(blockX, blockY, blockZ, value);

            byte blockId = (byte)value;
            short blockPackedCoords = (short)(blockX << 11 | blockZ << 7 | blockY);


            if (GrowableBlocks.ContainsKey(blockPackedCoords))
            {
                short unused;

                if (!BlockHelper.IsGrowable(blockId))
                {
                    GrowableBlocks.TryRemove(blockPackedCoords, out unused);
                }
                else
                {
                    byte metaData = GetData(blockX, blockY, blockZ);
                    StructBlock block = new StructBlock(UniversalCoords.FromBlock(Coords.ChunkX, Coords.ChunkZ, blockX, blockY, blockZ), blockId, metaData, World);
                    if (!(BlockHelper.Instance(blockId) as IBlockGrowable).CanGrow(block, this))
                    {
                        GrowableBlocks.TryRemove(blockPackedCoords, out unused);
                    }
                }
            }
            else
            {
                if (BlockHelper.IsGrowable(blockId))
                {
                    byte metaData = GetData(blockX, blockY, blockZ);
                    UniversalCoords blockCoords = UniversalCoords.FromBlock(Coords.ChunkX, Coords.ChunkZ, blockX, blockY,
                                                                            blockZ);
                    StructBlock block = new StructBlock(blockCoords, blockId, metaData, World);
                    if ((BlockHelper.Instance(blockId) as IBlockGrowable).CanGrow(block, this))
                        GrowableBlocks.TryAdd(blockPackedCoords, blockPackedCoords);
                }
            }
        }

        public void InitGrowableCache()
        {
            byte blockId = 0;
            byte blockMeta = 0;
            UniversalCoords blockCoords;
            StructBlock block;
            for (int x = 0; x < 16; x++)
                for (int y = 0; y < 128; y++)
                    for (int z = 0; z < 16; z++)
                    {
                        blockId = (byte)GetType(x, y, z);
                        if (BlockHelper.IsGrowable(blockId))
                        {
                            blockCoords = UniversalCoords.FromBlock(Coords.ChunkX, Coords.ChunkZ, x, y, z);
                            blockMeta = GetData(x, y, z);
                            block = new StructBlock(blockCoords, blockId, blockMeta, World);
                            if ((BlockHelper.Instance(blockId) as IBlockGrowable).CanGrow(block, this))
                                GrowableBlocks.TryAdd(blockCoords.BlockPackedCoords, blockCoords.BlockPackedCoords);
                        }
                    }
        }

        internal void Grow()
        {
            byte blockId = 0;
            byte metaData = 0;
            short unused;
            StructBlock block;
            IBlockGrowable iGrowable;
            int blockX, blockY, blockZ;
            byte light, sky;

            //short blockPackedCoords = (short)(blockX << 11 | blockZ << 7 | blockY);
            foreach (var growableBlock in GrowableBlocks)
            {
                blockX = growableBlock.Key >> 11;
                blockY = (growableBlock.Key & 0xff) % 128;
                blockZ = (growableBlock.Key >> 7) & 0xf;

                blockId = (byte)GetType(blockX, blockY, blockZ);
                light = GetBlockLight(blockX, blockY, blockZ);
                sky = GetSkyLight(blockX, blockY, blockZ);
                if (BlockHelper.IsGrowable(blockId))
                {
                    metaData = GetData(blockX, blockY, blockZ);
                    block = new StructBlock(UniversalCoords.FromBlock(Coords.ChunkX, Coords.ChunkZ, blockX, blockY, blockZ), blockId, metaData, World);
                    iGrowable = (BlockHelper.Instance(blockId) as IBlockGrowable);
                    if (iGrowable.CanGrow(block, this))
                    {
                        iGrowable.Grow(block, this);

                        continue;
                    }
                }
                GrowableBlocks.TryRemove(growableBlock.Key, out unused);
            }
        }

        /*private void Grow(UniversalCoords coords)
        {
            BlockData.Blocks type = GetType(coords);
            byte metaData = GetData(coords);

            if (!(BlockHelper.Instance((byte)type) is IBlockGrowable))
                return;

            UniversalCoords oneUp = UniversalCoords.FromAbsWorld(coords.WorldX, coords.WorldY + 1, coords.WorldZ);
            byte light = GetBlockLight(oneUp);
            byte sky = GetSkyLight(oneUp);

            StructBlock thisBlock = new StructBlock(coords, (byte)type, metaData, this.World);
            IBlockGrowable blockToGrow = (BlockHelper.Instance((byte)type) as IBlockGrowable);
            blockToGrow.Grow(thisBlock);

            switch (type)
            {
                case BlockData.Blocks.Grass:
                    GrowDirt(coords);
                    break;
            }

            if (light < 7 && sky < 7)
            {
                SpawnMob(oneUp);
                return;
            }
            if (type == BlockData.Blocks.Grass)
                SpawnAnimal(coords);
        }*/

        public void ForAdjacent(UniversalCoords coords, ForEachBlock predicate)
        {
            predicate(UniversalCoords.FromWorld(coords.WorldX - 1, coords.WorldY, coords.WorldZ));
            predicate(UniversalCoords.FromWorld(coords.WorldX + 1, coords.WorldY, coords.WorldZ));
            predicate(UniversalCoords.FromWorld(coords.WorldX, coords.WorldY, coords.WorldZ - 1));
            predicate(UniversalCoords.FromWorld(coords.WorldX, coords.WorldY, coords.WorldZ + 1));
            if (coords.BlockY > 0)
                predicate(UniversalCoords.FromWorld(coords.WorldX, coords.WorldY - 1, coords.WorldZ));
            if (coords.BlockY < 127)
                predicate(UniversalCoords.FromWorld(coords.WorldX, coords.WorldY + 1, coords.WorldZ));
        }

        public void ForNSEW(UniversalCoords coords, ForEachBlock predicate)
        {
            predicate(UniversalCoords.FromWorld(coords.WorldX - 1, coords.WorldY, coords.WorldZ));
            predicate(UniversalCoords.FromWorld(coords.WorldX + 1, coords.WorldY, coords.WorldZ));
            predicate(UniversalCoords.FromWorld(coords.WorldX, coords.WorldY, coords.WorldZ - 1));
            predicate(UniversalCoords.FromWorld(coords.WorldX, coords.WorldY, coords.WorldZ + 1));
        }

        public bool IsAdjacentTo(UniversalCoords coords, byte block)
        {
            bool retval = false;
            ForAdjacent(coords, delegate(UniversalCoords uc)
            {
                retval = retval || World.GetBlockId(uc) == block;
            });
            return retval;
        }

        public bool IsNSEWTo(UniversalCoords coords, byte block)
        {
            bool retval = false;
            ForNSEW(coords, delegate(UniversalCoords uc)
            {
                if (World.GetBlockId(uc) == block)
                    retval = true;
            });
            return retval;
        }

        public void GrowCactus(UniversalCoords coords)
        {
            if (GetType(coords) == BlockData.Blocks.Cactus)
                return;

            if (GetType(UniversalCoords.FromWorld(coords.WorldX, coords.WorldY - 3, coords.WorldZ)) == BlockData.Blocks.Cactus)
                return;

            if (!IsNSEWTo(coords, (byte)BlockData.Blocks.Air))
                return;

            if (World.Server.Rand.Next(60) == 0)
            {
                SetType(coords, BlockData.Blocks.Cactus);
            }
        }

        private void GrowDirt(UniversalCoords coords)
        {
            if (coords.WorldY >= 127 || IsAir(UniversalCoords.FromWorld(coords.WorldX, coords.WorldY + 1, coords.WorldZ)))
                return;

            if (World.Server.Rand.Next(30) != 0)
            {
                SetType(coords, BlockData.Blocks.Dirt);
            }
        }

        internal void SetWeather(WeatherState weather)
        {
            foreach (Client c in GetClients())
            {
                c.SendWeather(weather, Coords);
            }
        }

        protected override void UpdateBlocksToNearbyPlayers(object state)
        {
            BlocksUpdateLock.EnterWriteLock();
            int num = Interlocked.Exchange(ref NumBlocksToUpdate, 0);
            ConcurrentDictionary<short, short> temp = BlocksToBeUpdated;
            BlocksToBeUpdated = BlocksUpdating;
            BlocksUpdateLock.ExitWriteLock();

            BlocksUpdating = temp;

            if (num == 1)
            {
                short keyCoords = BlocksUpdating.Keys.First();
                short index;
                BlocksUpdating.TryGetValue(keyCoords, out index);
                int blockX = (index >> 12 & 0xf);
                int blockY = (index & 0xff);
                int blockZ = (index >> 8 & 0xf);
                byte blockId = (byte)GetType(blockX, blockY, blockZ);
                byte data = GetData(blockX, blockY, blockZ);

                World.Server.SendPacketToNearbyPlayers(World, Coords, new BlockChangePacket 
                {X = Coords.WorldX + blockX, Y = (sbyte) blockY, Z = Coords.WorldZ + blockZ, Data = data, Type = blockId});
                
            }
            else if (num < 20)
            {
                sbyte[] data = new sbyte[num];
                sbyte[] types = new sbyte[num];
                short[] blocks = new short[num];

                int count = 0;
                foreach (short key in BlocksUpdating.Keys)
                {
                    short index;
                    BlocksUpdating.TryGetValue(key, out index);
                    int blockX = (index >> 12 & 0xf);
                    int blockY = (index & 0xff);
                    int blockZ = (index >> 8 & 0xf);

                    data[count] = (sbyte)GetData(blockX, blockY, blockZ);
                    types[count] = (sbyte)GetType(blockX, blockY, blockZ);
                    blocks[count] = index;
                    ++count;
                }
                World.Server.SendPacketToNearbyPlayers(World, Coords, new MultiBlockChangePacket { CoordsArray = blocks, Metadata = data, Types = types, ChunkCoords = Coords });
            }
            else
            {
                World.Server.SendPacketToNearbyPlayers(World, Coords, new MapChunkPacket { Chunk = this });
            }

            BlocksUpdating.Clear();
            base.UpdateBlocksToNearbyPlayers(state);
        }
    }
}
