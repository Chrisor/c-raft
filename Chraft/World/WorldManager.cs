﻿using System;
using System.Linq;
using System.Threading;
using Chraft.Plugins.Events;
using Chraft.Properties;
using System.IO;
using Chraft.Entity;
using Chraft.World.Weather;
using Chraft.Plugins.Events.Args;
using System.Threading.Tasks;
using Chraft.Utils;
using System.Collections.Generic;

namespace Chraft.World
{
    public partial class WorldManager : IDisposable
    {
        private Timer GlobalTick;
        private ChunkGenerator Generator;
        public object ChunkGenLock = new object();
        private WorldChunkManager ChunkManager;

        public sbyte Dimension { get { return 0; } }
        public long Seed { get; private set; }
        public PointI Spawn { get; set; }
        public bool Running { get; private set; }
        public Server Server { get; private set; }
        public Logger Logger { get { return Server.Logger; } }
        public string Name { get { return Settings.Default.DefaultWorldName; } }
        public string Folder { get { return Settings.Default.WorldsFolder + "/" + Name; } }
        public WeatherManager Weather { get; private set; }

        private readonly ChunkSet _Chunks;
        private ChunkSet Chunks { get { return _Chunks; } }

        private Queue<ChunkLightUpdate> CurrentChunkToRecalculate;
        private Queue<ChunkLightUpdate> ChunkRecalculating;

        private readonly object ChunkLightUpdateLock = new object();
        private bool AlreadyRecalculatingLight;

        private Task _UpdateClientChunksTask;
        private Task _RecalculateSkylightTask;
        private Task _GrowStuffTask;

        private int _Time;
        private readonly ReaderWriterLockSlim _TimeWriteLock = new ReaderWriterLockSlim();
        private Chunk[] _ChunksCache;
        /// <summary>
        /// In units of 0.05 seconds (between 0 and 23999)
        /// </summary>
        public int Time
        {
            get {
                _TimeWriteLock.EnterReadLock();
                int time = _Time;
                //Console.WriteLine("Reading: {0}", _Time);
                _TimeWriteLock.ExitReadLock();
                return time; }
            set {
                _TimeWriteLock.EnterWriteLock();
                _Time = value;
                _TimeWriteLock.ExitWriteLock();
                }
        }

        private int _worldTicks = 0;
        
        /// <summary>
        /// The current World Tick independant of the world's current Time (1 tick = 0.05 secs with a max value of 4,294,967,295 gives approx. 6.9 years of ticks)
        /// </summary>
        public int WorldTicks
        {
            get
            {
                return _worldTicks;
            }
        }

        public Chunk this[int x, int z]
        {
            get
            {
                Chunk chunk;
                if ((chunk = Chunks[x, z]) != null)
                {
                    //Server.Logger.Log(Logger.LogLevel.Debug, "Getting {0}, {1}", x, z);
                    return chunk;
                }
                //Server.Logger.Log(Logger.LogLevel.Debug, "Creating {0}, {1}", x, z);
                return LoadChunk(x, z);
            }
        }

        public WorldManager(Server server)
        {          
            _Chunks = new ChunkSet();
            Server = server;
            CurrentChunkToRecalculate = new Queue<ChunkLightUpdate>(20);
            ChunkRecalculating = new Queue<ChunkLightUpdate>(20);
            Load();
        }

        public bool Load()
        {
            EnsureDirectory();

            //Event
            WorldLoadEventArgs e = new WorldLoadEventArgs(this);
            Server.PluginManager.CallEvent(Event.WORLD_LOAD, e);
            if (e.EventCanceled) return false;
            //End Event

            Generator = new ChunkGenerator(this, GetSeed());
            ChunkManager = new WorldChunkManager(this);

            InitializeSpawn();
            InitializeThreads();
            InitializeWeather();
            return true;
        }
        
        private void InitializeWeather()
        {
            Weather = new WeatherManager(this);
        }

        public int GetHeight(int x, int z)
        {
            return this[x >> 4, z >> 4].HeightMap[x & 0xf, z & 0xf];
        }

        private Chunk LoadChunk(int x, int z)
        {
            lock (ChunkGenLock)
            {
                Chunk chunk = new Chunk(this, x, z);
                if (chunk.Load())
                    Chunks.Add(chunk);
                else
                {
                    chunk = Generator.ProvideChunk(x, z, chunk);
                    chunk.Recalculate();
                    chunk.Save();
                    Chunks.Add(chunk);
                }
                return chunk;
            }
        }

        public void ScheduleSkyLightUpdate(ChunkLightUpdate chunkUpdate)
        {
            lock(ChunkLightUpdateLock)
                CurrentChunkToRecalculate.Enqueue(chunkUpdate);
        }

        private void InitializeThreads()
        {
            this.Running = true;
            GlobalTick = new Timer(GlobalTickProc, null, 50, 50);
            SaveStart();
            //GrowStart();
            //CollectStart();
            EntityMoverStart();
        }

        private void InitializeSpawn()
        {
            Spawn = new PointI(Settings.Default.SpawnX, Settings.Default.SpawnY, Settings.Default.SpawnZ);
            for (int i = 127; i > 0; i--)
            {
                if (GetBlockOrLoad(Spawn.X, i, Spawn.Z) != 0)
                {
                    Spawn = new PointI(Spawn.X, i + 4, Spawn.Z);
                    break;
                }
            }
        }

        private void CollectStart()
        {
            Thread trd = new Thread(CollectRun);
            trd.IsBackground = false;
            trd.Start();
        }

        private void CollectRun()
        {
            while (Running)
                CollectProc();
        }

        private void CollectProc()
        {
            Chunk[] chunks = GetChunks();
            foreach (Chunk c in chunks)
            {
                if (c.Persistent)
                    continue;
                if (c.GetClients().Length > 0)
                    continue;
                Chunks.Remove(c);
            }
        }

        private void SaveStart()
        {
            Thread trd = new Thread(SaveRun);
            trd.IsBackground = false;
            trd.Priority = ThreadPriority.Lowest;
            trd.Start();
        }

        private void SaveRun()
        {
            while (Running)
                SaveProc(true);
            SaveProc(false);
        }

        private void SaveProc(bool passive)
        {
            Chunk[] chunks = GetChunks();
            for (int i = 0; i < chunks.Length && Running; i++)
            {
                chunks[i].Save();
                if (passive)
                    Thread.Sleep(5000);
            }
        }

        private void EnsureDirectory()
        {
            if (!Directory.Exists(Settings.Default.WorldsFolder))
                Directory.CreateDirectory(Settings.Default.WorldsFolder);
            if (!Directory.Exists(Folder))
                Directory.CreateDirectory(Folder);
        }

        public static int ligthUpdateCounter = 0;

        private void GlobalTickProc(object state)
        {
            // Increment the world tick count (low-lock sync via volatile - safe because this is an atomic operation)
            Interlocked.Increment(ref _worldTicks);

            int time;
            // Lock Time so that others cannot write to it
            _TimeWriteLock.EnterWriteLock();
            //Console.WriteLine("Writing Time");
            time = ++_Time;
            //Console.WriteLine("Time: {0}", _Time);
            if (time == 24000)
            {	// A day has passed.
                // MUST interface directly with _Time to bypass the write lock, which we hold.
                _Time = time = 0;
            }
            _TimeWriteLock.ExitWriteLock();
            

            // Using this.WorldTick here as it is independant of this.Time. "this.Time" can be changed outside of the WorldManager.
            if (this.WorldTicks % 20 == 0)
            {
                // Triggered once every ten seconds
                Task pulse = new Task(Server.DoPulse);
                pulse.Start();
                /*Thread thread = new Thread(Server.DoPulse);
                thread.IsBackground = true;
                thread.Start();*/
            }
           
            // Every 100ms
            if (this.WorldTicks % 2 == 0 && (_RecalculateSkylightTask == null || _RecalculateSkylightTask.IsCompleted))
            {
                if (CurrentChunkToRecalculate.Count > 0 && !AlreadyRecalculatingLight)
                {
                    AlreadyRecalculatingLight = true;
                    _RecalculateSkylightTask = new Task(RecalculateChunkSkylight, TaskCreationOptions.LongRunning);
                    _RecalculateSkylightTask.Start();
                }
            }

            // Every 250ms
            if (this.WorldTicks % 5 == 0 && (_UpdateClientChunksTask == null || _UpdateClientChunksTask.IsCompleted))
            {
                //Parallel.ForEach<Client>(Server.GetClients(), c => { if(c.LoggedIn)c.UpdateChunks(Settings.Default.SightRadius); });
                _UpdateClientChunksTask = new Task(UpdateClientChunks);
                _UpdateClientChunksTask.Start();
            }

            // Every second
            if(this.WorldTicks % 100 == 0)
            {
                if (_GrowStuffTask == null || _GrowStuffTask.IsCompleted)
                {
                    _GrowStuffTask = new Task(GrowProc);
                    _GrowStuffTask.Start();
                }

                Task collect = new Task(CollectProc);
                collect.Start();
            }
        }

        private void UpdateClientChunks()
        {
            foreach (Client c in Server.GetClients())
            {
                if (c.LoggedIn)
                    c.UpdateChunks(Settings.Default.SightRadius);
            }
        }

        public static int LightUpdatesCounter;

        private void RecalculateChunkSkylight()
        {
            lock(ChunkLightUpdateLock)
            {
                Queue<ChunkLightUpdate> temp = ChunkRecalculating;
                ChunkRecalculating = CurrentChunkToRecalculate;
                CurrentChunkToRecalculate = temp;
            }

            while (ChunkRecalculating.Count > 0)
            {
                ChunkLightUpdate chunkUpdate = ChunkRecalculating.Dequeue();
                
                if (chunkUpdate.Chunk == null && !chunkUpdate.Chunk.Deleted)
                {
                    chunkUpdate.Chunk.StackSize = 0;
                    if (chunkUpdate.X == -1)
                        chunkUpdate.Chunk.SpreadSkyLight();
                    else
                        chunkUpdate.Chunk.SpreadSkyLightFromBlock((byte)chunkUpdate.X, (byte)chunkUpdate.Y, (byte)chunkUpdate.Z);
                }
                

                //chunkUpdate.Chunk.IsRecalculating = false;
                /*++LightUpdatesCounter;

                if(LightUpdatesCounter > 50)
                {
                    LightUpdatesCounter = 0;
                    Thread.Sleep(500);
                }*/
            }

            AlreadyRecalculatingLight = false;
        }

        public Chunk GetChunkFromPosition(int x, int z)
        {
            return Chunks[x, z];
        }

        public byte this[int x, int y, int z]
        {
            get
            {
                Chunk WorkChunk = GetChunkFromPosition(x, z);
                return (WorkChunk[x & 0xf, y, z & 0xf]);
            }
            set
            {
                Chunk WorkChunk = GetChunkFromPosition(x, z);
                WorkChunk[x & 0xf, y, z & 0xf] = value;
            }
        }

        public void Dispose()
        {
            this.Running = false;
            this.GlobalTick.Change(Timeout.Infinite, Timeout.Infinite);
        }

        public byte GetBlockOrLoad(int x, int y, int z)
        {
            return this[x >> 4, z >> 4][x & 0xf, y, z & 0xf];
        }

        public Chunk[] GetChunks()
        {
            int changes = Interlocked.Exchange(ref Chunks.Changes, 0);
            if(_ChunksCache == null && changes > 0)
                _ChunksCache = Chunks.Values.ToArray();

            return _ChunksCache;
        }

        private void GrowStart()
        {
            Thread thread = new Thread(GrowThread);
            thread.IsBackground = true;
            thread.Priority = ThreadPriority.BelowNormal;
            thread.Start();
        }

        private void GrowThread()
        {
            while (Running)
            {
                Thread.Sleep(1000);
                GrowProc();
            }
        }

        private void GrowProc()
        {
            foreach (Chunk c in GetChunks())
            {
                c.Grow();
            }
        }

        private void EntityMoverStart()
        {
            Thread thread = new Thread(MovementThread);
            thread.IsBackground = true;
            thread.Priority = ThreadPriority.BelowNormal;
            thread.Start();
        }

        private void MovementThread()
        {
            while (Running)
            {
                Thread.Sleep(200);
                MovProc();
            }
        }

        private void MovProc()
        {
            Parallel.ForEach(Server.GetEntities().Where((entity) => entity.World == this), (e) =>
            {
                e.TimeInWorld++;

                if (e is Mob)
                {
                    Mob m = (Mob)e;

                    m.Update();
                }
                else if (e is ItemEntity)
                {
                    byte? uBlock = GetBlockOrNull((int)e.Position.X, (int)(e.Position.Y - 0.4), (int)e.Position.Z);

                    if (uBlock != null) // Ignore if item is in an unloaded chunk.
                    {
                        switch ((BlockData.Blocks)uBlock)
                        {
                            case BlockData.Blocks.Air:
                            case BlockData.Blocks.Brown_Mushroom:
                            case BlockData.Blocks.Crops:
                            case BlockData.Blocks.Ladder:
                            case BlockData.Blocks.Lever:
                            case BlockData.Blocks.Portal:
                            case BlockData.Blocks.Rails:
                            case BlockData.Blocks.Red_Mushroom:
                            case BlockData.Blocks.Red_Rose:
                            case BlockData.Blocks.Redstone_Torch:
                            case BlockData.Blocks.Redstone_Torch_On:
                            case BlockData.Blocks.Redstone_Wire:
                            case BlockData.Blocks.Reed:
                            case BlockData.Blocks.Sapling:
                            case BlockData.Blocks.Still_Water:
                            case BlockData.Blocks.Stone_Button:
                            case BlockData.Blocks.Torch:
                            case BlockData.Blocks.Water:
                            case BlockData.Blocks.Yellow_Flower:
                                e.Position.Y -= 0.4;
                                break;

                            case BlockData.Blocks.Fire:
                            case BlockData.Blocks.Lava:
                            case BlockData.Blocks.Still_Lava:
                                Server.RemoveEntity(e);
                                break;
                        }
                    }

                    // TOOD: Water flow movement.
                }
            });
        }

        public void SpawnAnimal(int X, int Y, int Z)
        {
            MobType type = MobType.Giant;
            switch (Server.Rand.Next(4))
            {
                case 0: type = MobType.Cow; break;
                case 1: type = MobType.Hen; break;
                case 2: type = MobType.Pig; break;
                case 3: type = MobType.Sheep; break;
            }

            Mob mob = MobFactory.CreateMob(this, this.Server.AllocateEntity(), type);

            mob.Position = new Location(new Vector3(X + 0.5, Y, Z + 0.5));
            mob.World = this;

            mob.Hunter = true;
            mob.Hunting = false;

            //Event
            EntitySpawnEventArgs e = new EntitySpawnEventArgs(mob, mob.Position.Vector);
            Server.PluginManager.CallEvent(Plugins.Events.Event.ENTITY_SPAWN, e);
            if (e.EventCanceled) return;
            mob.Position.Vector = e.Location;
            //End Event
            
            //mob.Data // Set accessor is inaccebile?
            Server.AddEntity(mob);
        }

        public void SpawnMob(int X, int Y, int Z, MobType type = MobType.Pig)
        {
            if (type == MobType.Pig) // Type has not been forced.
            {
                switch (Server.Rand.Next(4))
                {
                    case 0: type = MobType.Zombie; break;
                    case 1: type = MobType.Skeleton; break;
                    case 2: type = MobType.Creeper; break;
                    case 3: type = MobType.Spider; break; // TODO: Check space is larger than 1x2
                }
            }

            Mob mob = MobFactory.CreateMob(this, this.Server.AllocateEntity(), type);

            mob.Position = new Location(new Vector3(X + 0.5, Y, Z + 0.5));
            mob.World = this;

            mob.Hunter = true;
            mob.Hunting = false;

            //Event
            EntitySpawnEventArgs e = new EntitySpawnEventArgs(mob, mob.Position.Vector);
            Server.PluginManager.CallEvent(Plugins.Events.Event.ENTITY_SPAWN, e);
            if (e.EventCanceled) return;
            mob.Position.Vector = e.Location;
            //End Event
            
            //mob.Data // Set accessor is inaccebile?
            Server.AddEntity(mob); // TODO: Limit this in some way.
        }

        public Chunk GetBlockChunk(int x, int y, int z)
        {
            if (!ChunkExists(x >> 4, z >> 4))
                return null;
            return Chunks[x >> 4, z >> 4];
        }

        public byte GetBlockId(int x, int y, int z)
        {
            if (!ChunkExists(x >> 4, z >> 4))
                return 0;
            return Chunks[x >> 4, z >> 4][x & 0xf, y, z & 0xf];
        }

        public byte GetBlockData(int x, int y, int z)
        {
            if (!ChunkExists(x >> 4, z >> 4))
                return 0;
            return Chunks[x >> 4, z >> 4].GetData(x & 0xf, y, z & 0xf);
        }

        public byte GetBlockLight(int x, int y, int z)
        {
            if (!ChunkExists(x >> 4, z >> 4))
                return 0;
            return Chunks[x >> 4, z >> 4].GetBlockLight(x & 0xf, y, z & 0xf);
        }

        public byte GetSkyLight(int x, int y, int z)
        {
            if (!ChunkExists(x >> 4, z >> 4))
                return 0;
            return Chunks[x >> 4, z >> 4].GetSkyLight(x & 0xf, y, z & 0xf);
        }

        public byte? GetBlockOrNull(int x, int y, int z)
        {
            if (y < 0 || y > 127)
                return null;
            if (!ChunkExists(x >> 4, z >> 4))
                return null;
            return Chunks[x >> 4, z >> 4][x & 0xf, y, z & 0xf];
        }

        public long GetSeed()
        {
            return Settings.Default.WorldSeed.GetHashCode();
        }

        public void UpdateClients(int x, int y, int z)
        {
            byte type = GetBlockId(x, y, z);
            byte data = GetBlockData(x, y, z);
            foreach (Client c in Server.GetNearbyPlayers(this, x, y, z))
                c.SendBlock(x, y, z, type, data);
        }

        public void SetBlockAndData(int x, int y, int z, byte type, byte data)
        {
            Chunk chunk = this[x >> 4, z >> 4];
            int bx = x & 0xf;
            int bz = z & 0xf;
            chunk[bx, y, bz] = type;
            chunk.SetData(bx, y, bz, data);
            UpdateClients(x, y, z);
        }

        public void SetBlockData(int x, int y, int z, byte data)
        {
            this[x >> 4, z >> 4].SetData(x & 0xf, y, z & 0xf, data);
            UpdateClients(x, y, z);
        }

        internal WorldChunkManager GetWorldChunkManager()
        {
            return ChunkManager;
        }

        public bool ChunkExists(int x, int z)
        {
            return (Chunks[x,z] != null);
        }

        public void RemoveChunk(Chunk c)
        {
            Chunks.Remove(c);
        }

        internal void Update(int x, int y, int z, bool updateClients = true)
        {
            if (updateClients)
                UpdateClients(x, y, z);
            UpdatePhysics(x, y, z);
            this[x >> 4, z >> 4].ForAdjacent(x & 0xf, y, z & 0xf, delegate(int bx, int by, int bz)
            {
                UpdatePhysics(bx, by, bz);
            });
        }

        private void UpdatePhysics(int x, int y, int z, bool updateClients = true)
        {
            BlockData.Blocks type = (BlockData.Blocks)GetBlockId(x, y, z);

            if (type == BlockData.Blocks.Sand && y > 0 && GetBlockId(x, y - 1, z) == 0)
            {
                SetBlockAndData(x, y, z, 0, 0);
                SetBlockAndData(x, y - 1, z, (byte)BlockData.Blocks.Sand, 0);
                Update(x, y - 1, z, updateClients);
                return;
            }

            if (type == BlockData.Blocks.Gravel && y > 0 && GetBlockId(x, y - 1, z) == 0)
            {
                SetBlockAndData(x, y, z, 0, 0);
                SetBlockAndData(x, y - 1, z, (byte)BlockData.Blocks.Gravel, 0);
                Update(x, y - 1, z, updateClients);
                return;
            }

            if (type == BlockData.Blocks.Water)
            {
                byte water = 8;
                this[x >> 4, z >> 4].ForNSEW(x & 0xf, y, z & 0xf, delegate(int bx, int by, int bz)
                {
                    if (GetBlockId(bx, by, bz) == (byte)BlockData.Blocks.Still_Water)
                        water = 0;
                    else if (GetBlockId(bx, by, bz) == (byte)BlockData.Blocks.Water && GetBlockData(bx, by, bz) < water)
                        water = (byte)(GetBlockData(bx, by, bz) + 1);
                });
                if (water != GetBlockData(x, y, z))
                {
                    if (water == 8)
                        SetBlockAndData(x, y, z, 0, 0);
                    else
                        SetBlockAndData(x, y, z, (byte)BlockData.Blocks.Water, water);
                    //Update(x, y, z, updateClients);
                    return;
                }
            }

            if (type == BlockData.Blocks.Air)
            {
                if (y < 127 && (GetBlockId(x, y + 1, z) == (byte)BlockData.Blocks.Water || GetBlockId(x, y + 1, z) == (byte)BlockData.Blocks.Still_Water))
                {
                    SetBlockAndData(x, y, z, (byte)BlockData.Blocks.Water, 0);
                    //Update(x, y, z, updateClients);
                    return;
                }

                if (y < 127 && (GetBlockId(x, y + 1, z) == (byte)BlockData.Blocks.Lava || GetBlockId(x, y + 1, z) == (byte)BlockData.Blocks.Still_Lava))
                {
                    SetBlockAndData(x, y, z, (byte)BlockData.Blocks.Lava, 0);
                    //Update(x, y, z, updateClients);
                    return;
                }

                byte water = 8;
                this[x >> 4, z >> 4].ForNSEW(x & 0xf, y, z & 0xf, delegate(int bx, int by, int bz)
                {
                    if (GetBlockId(bx, by, bz) == (byte)BlockData.Blocks.Still_Water)
                        water = 0;
                    else if (GetBlockId(bx, by, bz) == (byte)BlockData.Blocks.Water && GetBlockData(bx, by, bz) < water)
                        water = (byte)(GetBlockData(bx, by, bz) + 1);
                });
                if (water < 8)
                {
                    SetBlockAndData(x, y, z, (byte)BlockData.Blocks.Water, water);
                    //Update(x, y, z, updateClients);
                    return;
                }

                byte lava = 8;
                this[x >> 4, z >> 4].ForNSEW(x & 0xf, y, z & 0xf, delegate(int bx, int by, int bz)
                {
                    if (GetBlockId(bx, by, bz) == (byte)BlockData.Blocks.Still_Lava)
                        lava = 0;
                    else if (GetBlockId(bx, by, bz) == (byte)BlockData.Blocks.Lava && GetBlockData(bx, by, bz) < lava)
                        lava = (byte)(GetBlockData(bx, by, bz) + 1);
                });
                if (water < 4)
                {
                    SetBlockAndData(x, y, z, (byte)BlockData.Blocks.Lava, lava);
                    //Update(x, y, z, updateClients);
                    return;
                }
            }
        }

        internal bool GrowTree(int x, int y, int z, byte treeType = (byte) 0)
        {
            // TODO: Expand this futher to build redwood.
            if (y > 120)
                return false;

            for (int by = y; by < y + 5; by++)
                SetBlockAndData(x, by, z, (byte)BlockData.Blocks.Log, treeType);

            for (int by = y + 2; by < y + 5; by++)
                for (int bx = x - 2; bx <= x + 2; bx++)
                    for (int bz = z - 2; bz <= z + 2; bz++)
                        SetLeaves(bx, by, bz);

            for (int bx = x - 1; bx <= x + 1; bx++)
                for (int bz = z - 1; bz <= z + 1; bz++)
                    SetLeaves(bx, y + 5, bz);
            return true;
        }

        private void SetLeaves(int x, int y, int z, byte treeType = (byte) 0)
        {
            if (!ChunkExists(x >> 4, z >> 4) || GetBlockId(x, y, z) != 0)
                return;
            SetBlockAndData(x, y, z, (byte)BlockData.Blocks.Leaves, treeType);
        }

        internal void GrowCactus(int x, int y, int z)
        {
            if (y > 120)
                return;

            //World.Logger.Log(Logger.LogLevel.Info, "Checking Cactus at: " + (X + x) + " " + (Y + y) + " " + (Z + z));
            // TODO: Fixing this, NSEW isn't working as it's supposed to.
            for (int by = y; by < y + 3; by++)
            {
                if (!this[x >> 4, z >> 4].IsNSEWTo(x & 0xf, by, z & 0xf, (byte)BlockData.Blocks.Air))
                    return;
            }

            for (int by = y; by < y + 3; by++)
            {
                SetBlockAndData(x, by, z, (byte)BlockData.Blocks.Cactus, 0);
            }
        }

        internal void FromFace(int x, int y, int z, BlockFace blockFace, out int bx, out int by, out int bz)
        {
            bx = x;
            by = y;
            bz = z;

            switch (blockFace)
            {
                case BlockFace.Self:
                    break;

                case BlockFace.Up:
                    by++;
                    break;

                case BlockFace.Down:
                    by--;
                    break;

                case BlockFace.North:
                    bx--;
                    break;

                case BlockFace.South:
                    bx++;
                    break;

                case BlockFace.East:
                    bz--;
                    break;

                case BlockFace.West:
                    bz++;
                    break;

                case BlockFace.NorthEast:
                    bx--;
                    bz--;
                    break;

                case BlockFace.NorthWest:
                    bx--;
                    bz++;
                    break;

                case BlockFace.SouthEast:
                    bx++;
                    bz--;
                    break;

                case BlockFace.SouthWest:
                    bx++;
                    bz++;
                    break;
            }
        }
    }
}

