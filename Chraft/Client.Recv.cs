﻿using System;
using System.Linq;
using Chraft.Net;
using Chraft.Net.Packets;
using Chraft.Plugins.Events;
using Chraft.Plugins.Events.Args;
using Chraft.World;
using Chraft.Entity;
using System.Text.RegularExpressions;
using Chraft.Utils;
using Chraft.Interfaces;
using System.Threading;
using System.Threading.Tasks;
using Chraft.Properties;
using Chraft.World.Blocks;
using Chraft.World.Blocks.Interfaces;

namespace Chraft
{
    public partial class Client
    {
        DateTime? _inAirStartTime = null;
        /// <summary>
        /// Returns the amount of time since the client was set as in the air
        /// </summary>
        public TimeSpan AirTime
        {
            get
            {
                if (_inAirStartTime == null)
                {
                    return new TimeSpan(0);
                }
                else
                {
                    return DateTime.Now - _inAirStartTime.Value;
                }
            }
        }

        double _beginInAirY = -1;
        double _lastGroundY = -1;
        bool _onGround = false;
        public bool OnGround
        {
            get
            {
                return _onGround;
            }
            set
            {
                if (_onGround != value)
                {
                    _onGround = value;

                    // TODO: For some reason the GetBlockId using an integer will sometime get the block adjacent to where the character is standing therefore falling down near a wall could cause issues (or falling into a 1x1 water might not pick up the water block)
                    BlockData.Blocks currentBlock = (BlockData.Blocks)this.World.GetBlockId((int)this.Position.X, (int)this.Position.Y, (int)this.Position.Z);

                    if (!_onGround)
                    {
                        _beginInAirY = this.Position.Y;
                        _inAirStartTime = DateTime.Now;
#if DEBUG
                        this.SendMessage("In air");
#endif
                    }
                    else
                    {
#if DEBUG
                        this.SendMessage("On ground");
#endif

                        double blockCount = 0;

                        if (_lastGroundY < this.Position.Y)
                        {
                            // We have climbed (using _lastGroundY gives us a more accurate value than using _beginInAirY when climbing)
                            blockCount = (_lastGroundY - this.Position.Y);
                        }
                        else
                        {
                            // We have fallen
                            double startY = Math.Max(_lastGroundY, _beginInAirY);
                            blockCount = (startY - this.Position.Y);
                        }
                        _lastGroundY = this.Position.Y;

                        if (blockCount != 0)
                        {
                            if (blockCount > 0.5)
                            {
#if DEBUG
                                this.SendMessage(String.Format("Fell {0} blocks", blockCount));
#endif
                                double fallDamage = (blockCount - 3);// (we don't devide by two because DamageClient uses whole numbers i.e. 20 = 10 health)

                                #region Adjust based on falling into water
                                // For each sixteen blocks of altitude the water must be one block deep, if the jump altitude is higher as sixteen blocks and the water is only one deep damage is taken from the total altitude minus sixteen (19 is safe i.e. 19-16 = 3 => no damage)
                                // If we are in water, count how many blocks above are also water
                                BlockData.Blocks block = currentBlock;
                                int waterCount = 0;
                                while (BlockData.IsLiquid(block))
                                {
                                    waterCount++;
                                    block = (BlockData.Blocks)this.World.GetBlockId((int)this.Position.X, (int)this.Position.Y + waterCount, (int)this.Position.Z);
                                }

                                fallDamage -= waterCount * 16;
                                #endregion

                                if (fallDamage > 0)
                                {
                                    var roundedValue = Convert.ToInt16(Math.Round(fallDamage, 1));
                                    DamageClient(DamageCause.Fall, null, roundedValue);

                                    if (this.Health <= 0)
                                    {
                                        // Make sure that we don't think we have fallen onto the respawn
                                        _lastGroundY = -1;
                                    }
                                }
                            }
                            else if (blockCount < -0.5)
                            {
#if DEBUG
                                this.SendMessage(String.Format("Climbed {0} blocks", blockCount * -1));
#endif
                            }
                        }

                        _beginInAirY = -1;
                    }

                    if (_inAirStartTime != null)
                    {
                        // Check how long in the air for (e.g. flying) - don't count if we are in water
                        if (currentBlock != BlockData.Blocks.Water && currentBlock != BlockData.Blocks.Still_Water && currentBlock != BlockData.Blocks.Stationary_Water && AirTime.TotalSeconds > 5)
                        {
                            // TODO: make the number of seconds configurable
                            Kick("Flying!!");
                        }

                        _inAirStartTime = null;
                    }
                }
            }
        }
        public double Stance { get; set; }

        private void InitializeRecv()
        {
            InitializeRecvBasic();
            InitializeRecvPlayer();
            InitializeRecvInterface();
            InitializeRecvUse();
        }

        private void InitializeRecvUse()
        {
            PacketHandler.UseEntity += new PacketEventHandler<UseEntityPacket>(PacketHandler_UseEntity);
            PacketHandler.UseBed += new PacketEventHandler<UseBedPacket>(PacketHandler_UseBed);
        }

        private void InitializeRecvInterface()
        {
            PacketHandler.CloseWindow += PacketHandler_CloseWindow;
            PacketHandler.WindowClick += PacketHandler_WindowClick;
        }

        private void InitializeRecvBasic()
        {
            PacketHandler.ChatMessage += PacketHandler_ChatMessage;
            PacketHandler.LoginRequest += PacketHandler_LoginRequest;
            PacketHandler.Handshake += PacketHandler_Handshake;
            PacketHandler.Disconnect += PacketHandler_Disconnect;
            PacketHandler.ServerListPing += PacketHandler_ServerListPing;
            PacketHandler.KeepAlive += new PacketEventHandler<KeepAlivePacket>(PacketHandler_KeepAlive);

        }

        void PacketHandler_KeepAlive(object sender, PacketEventArgs<KeepAlivePacket> e)
        {
            _lastClientResponse = DateTime.Now;
            if (_lastKeepAliveId > 0 && e.Packet.KeepAliveID == _lastKeepAliveId)
            {
                this.Ping = (int)Math.Round((DateTime.Now - _keepAliveStart).TotalMilliseconds, MidpointRounding.AwayFromZero);
            }

        }

        private void InitializeRecvPlayer()
        {
            PacketHandler.Animation += new PacketEventHandler<AnimationPacket>(PacketHandler_Animation);
            PacketHandler.PlayerPosition += PacketHandler_PlayerPosition;
            PacketHandler.PlayerPositionRotation += PacketHandler_PlayerPositionRotation;
            PacketHandler.PlayerRotation += PacketHandler_PlayerRotation;
            PacketHandler.PlayerDigging += PacketHandler_PlayerDigging;
            PacketHandler.Player += PacketHandler_Player;
            PacketHandler.PlayerBlockPlacement += PacketHandler_PlayerBlockPlacement;
            PacketHandler.HoldingChange += PacketHandler_HoldingChange;
            PacketHandler.Respawn += PacketHander_Respawn; // Does this need a new handler?
            PacketHandler.CreativeInventoryAction += PacketHandler_CreativeInventoryAction;
        }

        void PacketHandler_CreativeInventoryAction(object sender, PacketEventArgs<CreativeInventoryActionPacket> e)
        {
            if (GameMode == 1)
                Inventory[e.Packet.Slot] = new ItemStack(e.Packet.ItemID, (sbyte)e.Packet.Quantity, e.Packet.Damage);
            else
                Kick("Invalid action: CreativeInventoryAction");
        }

        private void PacketHandler_Animation(object sender, PacketEventArgs<AnimationPacket> e)
        {
            foreach (Client c in Server.GetNearbyPlayers(World, Position.X, Position.Y, Position.Z))
            {
                if (c == this)
                    continue;
                c.PacketHandler.SendPacket(new AnimationPacket
                {
                    Animation = e.Packet.Animation,
                    PlayerId = this.EntityId
                });
            }
        }

        private void PacketHander_Respawn(object sender, PacketEventArgs<RespawnPacket> e)
        {
            HandleRespawn();
        }

        private void PacketHandler_ChatMessage(object sender, PacketEventArgs<ChatMessagePacket> e)
        {
            string clean = Chat.CleanMessage(e.Packet.Message);

            if (clean.StartsWith("/"))
                ExecuteCommand(clean.Substring(1));
            else
                ExecuteChat(clean);
        }


        #region Use

        private void PacketHandler_UseBed(object sender, PacketEventArgs<UseBedPacket> e)
        {
            throw new NotImplementedException();
        }

        private void PacketHandler_UseEntity(object sender, PacketEventArgs<UseEntityPacket> e)
        {
            //Console.WriteLine(e.Packet.Target);
            //this.SendMessage("You are interacting with " + e.Packet.Target + " " + e.Packet.LeftClick);

            foreach (EntityBase eb in Server.GetNearbyEntities(World, Position.X, Position.Y, Position.Z))
            {
                if (eb.EntityId != e.Packet.Target)
                    continue;

                if (eb is Client)
                {
                    Client c = (Client)eb;

                    if (e.Packet.LeftClick)
                    {
                        if (c.Health > 0)
                            c.DamageClient(DamageCause.EntityAttack, this, 0);
                    }
                    else
                    {
                        // TODO: Store the object being ridden, so we can update player movement.
                        // This will ride the entity, sends -1 to dismount.
                        foreach (Client cl in Server.GetNearbyPlayers(World, Position.X, Position.Y, Position.Z))
                        {
                            cl.PacketHandler.SendPacket(new AttachEntityPacket
                            {
                                EntityId = this.EntityId,
                                VehicleId = c.EntityId
                            });
                        }
                    }
                }
                else if (eb is Mob)
                {
                    Mob m = (Mob)eb;

                    if (e.Packet.LeftClick)
                    {
                        if (m.Health > 0)
                            m.DamageMob(this);
                    }
                    else
                    {
                        // We are interacting with a Mob - tell it what we are using to interact with it
                        m.InteractWith(this, this.Inventory.ActiveItem);

                        // TODO: move the following to appropriate mob locations
                        // TODO: Check Entity has saddle set.
                        //// This will ride the entity, sends -1 to dismount.
                        //foreach (Client c in Server.GetNearbyPlayers(World, Position.X, Position.Y, Position.Z))
                        //{
                        //    c.PacketHandler.SendPacket(new AttachEntityPacket
                        //    {
                        //        EntityId = this.EntityId,
                        //        VehicleId = c.EntityId
                        //    });
                        //}
                    }
                }
                /*else
                {
                    this.SendMessage(e.Packet.Target + " has no interaction handler!");
                }*/
            }
        }

        #endregion


        #region Interfaces

        private void PacketHandler_WindowClick(object sender, PacketEventArgs<WindowClickPacket> e)
        {
            Interface iface = CurrentInterface ?? Inventory;
            iface.OnClicked(e.Packet);
        }

        private void PacketHandler_CloseWindow(object sender, PacketEventArgs<CloseWindowPacket> e)
        {
            if (CurrentInterface != null)
            {
                CurrentInterface.Close(false);
            }
            else if (this.Inventory != null && e.Packet.WindowId == this.Inventory.Handle)
            {
                this.Inventory.Close(false);
            }
            CurrentInterface = null;
        }

        private void PacketHandler_HoldingChange(object sender, PacketEventArgs<HoldingChangePacket> e)
        {
            Inventory.OnActiveChanged((short)(e.Packet.Slot += 36));

            foreach (Client c in Server.GetNearbyPlayers(World, Position.X, Position.Y, Position.Z).Where(c => c != this))
            {
                c.SendHoldingEquipment(this);
            }
        }

        #endregion


        #region Block Con/Destruction

        private void PacketHandler_PlayerItemPlacement(object sender, PacketEventArgs<PlayerBlockPlacementPacket> e)
        {
            // if(!Permissions.CanPlayerBuild(Username)) return;
            if (Inventory.Slots[Inventory.ActiveSlot].Type <= 255)
                return;

            int x = e.Packet.X;
            int y = e.Packet.Y;
            int z = e.Packet.Z;

            BlockData.Blocks adjacentBlockType = (BlockData.Blocks)World.GetBlockId(x, y, z); // Get block being built against.

            // Placed Item Info
            int px, py, pz;
            short pType = Inventory.Slots[Inventory.ActiveSlot].Type;
            short pMetaData = Inventory.Slots[Inventory.ActiveSlot].Durability;

            World.FromFace(x, y, z, e.Packet.Face, out px, out py, out pz);

            switch (e.Packet.Face)
            {
                case BlockFace.Held:
                    return; // TODO: Process buckets, food, etc.
            }

            switch (adjacentBlockType)
            {
                case BlockData.Blocks.Air:
                case BlockData.Blocks.Water:
                case BlockData.Blocks.Lava:
                case BlockData.Blocks.Still_Water:
                case BlockData.Blocks.Still_Lava:
                    return;
            }

            switch ((BlockData.Items)pType)
            {
                case BlockData.Items.Diamond_Hoe:
                case BlockData.Items.Gold_Hoe:
                case BlockData.Items.Iron_Hoe:
                case BlockData.Items.Stone_Hoe:
                case BlockData.Items.Wooden_Hoe:
                    if (adjacentBlockType == BlockData.Blocks.Dirt || adjacentBlockType == BlockData.Blocks.Grass)
                    {
                        // Think the client has a Notch bug where hoe's durability is not updated properly.
                        px = x; py = y; pz = z;
                        World.SetBlockAndData(px, py, pz, (byte)BlockData.Blocks.Soil, 0x00);
                    }
                    break;

                case BlockData.Items.Sign:

                    if (e.Packet.Face == BlockFace.Up) // Floor Sign
                    {
                        // Get the direction the player is facing.
                        switch (FacingDirection(8))
                        {
                            case "N":
                                pMetaData = (byte)MetaData.SignPost.North;
                                break;
                            case "NE":
                                pMetaData = (byte)MetaData.SignPost.Northeast;
                                break;
                            case "E":
                                pMetaData = (byte)MetaData.SignPost.East;
                                break;
                            case "SE":
                                pMetaData = (byte)MetaData.SignPost.Southeast;
                                break;
                            case "S":
                                pMetaData = (byte)MetaData.SignPost.South;
                                break;
                            case "SW":
                                pMetaData = (byte)MetaData.SignPost.Southwest;
                                break;
                            case "W":
                                pMetaData = (byte)MetaData.SignPost.West;
                                break;
                            case "NW":
                                pMetaData = (byte)MetaData.SignPost.Northwest;
                                break;
                            default:
                                return;
                        }

                        World.SetBlockAndData(px, py, pz, (byte)BlockData.Blocks.Sign_Post, (byte)pMetaData);
                    }
                    else // Wall Sign
                    {
                        switch (e.Packet.Face)
                        {
                            case BlockFace.East: pMetaData = (byte)MetaData.SignWall.East;
                                break;
                            case BlockFace.West: pMetaData = (byte)MetaData.SignWall.West;
                                break;
                            case BlockFace.North: pMetaData = (byte)MetaData.SignWall.North;
                                break;
                            case BlockFace.South: pMetaData = (byte)MetaData.SignWall.South;
                                break;
                            case BlockFace.Down:
                                return;
                        }

                        World.SetBlockAndData(px, py, pz, (byte)BlockData.Blocks.Wall_Sign, (byte)pMetaData);
                    }
                    break;

                case BlockData.Items.Seeds:
                    if (adjacentBlockType == BlockData.Blocks.Soil && e.Packet.Face == BlockFace.Down)
                    {
                        World.SetBlockAndData(px, py, pz, (byte)BlockData.Blocks.Crops, 0x00);
                    }
                    break;

                case BlockData.Items.Redstone:
                    World.SetBlockAndData(px, py, pz, (byte)BlockData.Blocks.Redstone_Wire, 0x00);
                    break;

                case BlockData.Items.Minecart:
                case BlockData.Items.Boat:
                case BlockData.Items.Storage_Minecart:
                case BlockData.Items.Powered_Minecart:
                    // TODO: Create new object
                    break;

                case BlockData.Items.Iron_Door:
                case BlockData.Items.Wooden_Door:
                    {
                        if (!BlockData.Air.Contains((BlockData.Blocks)World.GetBlockId(px, py + 1, pz)))
                            return;

                        switch (FacingDirection(4)) // Built on floor, set by facing dir
                        {
                            case "N":
                                pMetaData = (byte)MetaData.Door.Northwest;
                                break;
                            case "W":
                                pMetaData = (byte)MetaData.Door.Southwest;
                                break;
                            case "S":
                                pMetaData = (byte)MetaData.Door.Southeast;
                                break;
                            case "E":
                                pMetaData = (byte)MetaData.Door.Northeast;
                                break;
                            default:
                                return;
                        }

                        if ((BlockData.Items)pType == BlockData.Items.Iron_Door)
                        {
                            World.SetBlockAndData(px, py + 1, pz, (byte)BlockData.Blocks.Iron_Door, (byte)MetaData.Door.IsTopHalf);
                            World.SetBlockAndData(px, py, pz, (byte)BlockData.Blocks.Iron_Door, (byte)pMetaData);
                        }
                        else
                        {
                            World.SetBlockAndData(px, py + 1, pz, (byte)BlockData.Blocks.Wooden_Door, (byte)MetaData.Door.IsTopHalf);
                            World.SetBlockAndData(px, py, pz, (byte)BlockData.Blocks.Wooden_Door, (byte)pMetaData);
                        }

                        World.Update(px, py + 1, pz);
                    }
                    break;
                case BlockData.Items.Shears:
                    if (adjacentBlockType == BlockData.Blocks.Leaves)
                    {
                        // TODO: Set correct leaves type (durability?): 0 basic leaves, 1 pine, 2 birch
                        Server.DropItem(World, x, y, z, new ItemStack((short)BlockData.Blocks.Leaves, 1, 0));
                        World.SetBlockAndData(x, y, z, 0, 0);
                        World.Update(x, y, z);
                    }
                    break;
            }
            if (GameMode == 0)
            {
                if (!Inventory.DamageItem(Inventory.ActiveSlot)) // If item isn't durable, remove it.
                    Inventory.RemoveItem(Inventory.ActiveSlot);
            }

            World.Update(px, py, pz);
        }

        private void PacketHandler_PlayerBlockPlacement(object sender, PacketEventArgs<PlayerBlockPlacementPacket> e)
        {
            /*
             * Scenarios:
             * 
             * 1) using an item against a block (e.g. stone and flint)
             * 2) placing a new block
             * 3) using a block: e.g. open/close door, open chest, open workbench, open furnace
             * 
             * */

            //  if (!Permissions.CanPlayerBuild(Username)) return;
            // Using activeslot provides current item info wtihout having to maintain ActiveItem

            int x = e.Packet.X;
            int y = e.Packet.Y;
            int z = e.Packet.Z;

            BlockData.Blocks type = (BlockData.Blocks)World.GetBlockId(x, y, z); // Get block being built against.
            byte metadata = World.GetBlockData(x, y, z);
            StructBlock facingBlock = new StructBlock(x, y, z, (byte)type, metadata, World);

            int bx, by, bz;
            World.FromFace(x, y, z, e.Packet.Face, out bx, out by, out bz);

            if (World.BlockHelper.Instance((byte)type) is IBlockInteractive)
            {
                (World.BlockHelper.Instance((byte)type) as IBlockInteractive).Interact(this, facingBlock);
                return;
            }

            if (Inventory.Slots[Inventory.ActiveSlot].Type <= 0 || Inventory.Slots[Inventory.ActiveSlot].Count < 1)
                return;

            // TODO: Neaten this out, or address via handler?
            if (Inventory.Slots[Inventory.ActiveSlot].Type > 255 || e.Packet.Face == BlockFace.Held) // Client is using an Item.
            {
                PacketHandler_PlayerItemPlacement(sender, e);
                return;
            }

            // Built Block Info

            byte bType = (byte)Inventory.Slots[Inventory.ActiveSlot].Type;
            byte bMetaData = (byte)Inventory.Slots[Inventory.ActiveSlot].Durability;

            StructBlock bBlock = new StructBlock(bx, by, bz, bType, bMetaData, World);

            World.BlockHelper.Instance(bType).Place(this, bBlock, facingBlock, e.Packet.Face);
        }

        private void PacketHandler_PlayerDigging(object sender, PacketEventArgs<PlayerDiggingPacket> e)
        {
            int x = e.Packet.X;
            int y = e.Packet.Y;
            int z = e.Packet.Z;

            byte type = World.GetBlockId(x, y, z);
            byte data = World.GetBlockData(x, y, z);

            switch (e.Packet.Action)
            {
                case PlayerDiggingPacket.DigAction.StartDigging:
                    this.SendMessage(String.Format("SkyLight: {0}", World.GetSkyLight(x, y, z)));
                    this.SendMessage(String.Format("BlockLight: {0}", World.GetBlockLight(x, y, z)));
                    this.SendMessage(String.Format("Opacity: {0}", World.GetBlockChunk(x, y, z).GetOpacity(x & 0xf, y, z & 0xf)));
                    this.SendMessage(String.Format("Height: {0}", World.GetHeight(x, z)));
                    this.SendMessage(String.Format("Data: {0}", World.GetBlockData(x, y, z)));
                    //this.SendMessage()
                    if (World.BlockHelper.Instance(type).IsSingleHit)
                        goto case PlayerDiggingPacket.DigAction.FinishDigging;
                    if (GameMode == 1)
                        goto case PlayerDiggingPacket.DigAction.FinishDigging;
                    break;

                case PlayerDiggingPacket.DigAction.FinishDigging:
                    StructBlock block = new StructBlock(x, y, z, type, data, World);
                    World.BlockHelper.Instance(type).Destroy(this, block);
                    break;
            }
        }

        #endregion


        #region Movement and Updates

        private void PacketHandler_Player(object sender, PacketEventArgs<PlayerPacket> e)
        {
            this.Ready = true;
            this.OnGround = e.Packet.OnGround;
            this.UpdateEntities();
        }

        private void PacketHandler_PlayerRotation(object sender, PacketEventArgs<PlayerRotationPacket> e)
        {
            this.RotateTo(e.Packet.Yaw, e.Packet.Pitch);
            this.OnGround = e.Packet.OnGround;
            this.UpdateEntities();
        }

        private void PacketHandler_PlayerPositionRotation(object sender, PacketEventArgs<PlayerPositionRotationPacket> e)
        {
            this.MoveTo(e.Packet.X, e.Packet.Y - EyeGroundOffset, e.Packet.Z, e.Packet.Yaw, e.Packet.Pitch);
            this.OnGround = e.Packet.OnGround;
            this.Stance = e.Packet.Stance;

            CheckAndUpdateChunks(e.Packet.X, e.Packet.Z);
        }

        private double _LastX;
        private double _LastZ;
        private int _MovementsArrived;
        private Task _UpdateChunks;
        private CancellationTokenSource _UpdateChunksToken = new CancellationTokenSource();

        private void PacketHandler_PlayerPosition(object sender, PacketEventArgs<PlayerPositionPacket> e)
        {
            this.Ready = true;
            this.MoveTo(e.Packet.X, e.Packet.Y, e.Packet.Z);
            this.OnGround = e.Packet.OnGround;
            this.Stance = e.Packet.Stance;

            CheckAndUpdateChunks(e.Packet.X, e.Packet.Z);
        }

        public void StopUpdateChunks()
        {
            _UpdateChunksToken.Cancel();
        }

        public void ScheduleUpdateChunks()
        {
            _UpdateChunksToken = new CancellationTokenSource();
            var token = _UpdateChunksToken.Token;
            _UpdateChunks = new Task(() => { UpdateChunks(Settings.Default.SightRadius, token); }, token);
            _UpdateChunks.Start();
        }

        private void CheckAndUpdateChunks(double packetX, double packetZ)
        {
            ++_MovementsArrived;

            if (_MovementsArrived % 8 == 0)
            {
                double distance = Math.Pow(Math.Abs(packetX - _LastX), 2.0) + Math.Pow(Math.Abs(packetZ - _LastZ), 2.0);
                _MovementsArrived = 0;
                if (distance > 16 && (_UpdateChunks == null || _UpdateChunks.IsCompleted))
                {
                    _LastX = packetX;
                    _LastZ = packetZ;
                    ScheduleUpdateChunks();
                }
            }
        }
        #endregion

        #region Login

        void PacketHandler_ServerListPing(object sender, PacketEventArgs<ServerListPingPacket> e)
        {
            // Received a ServerListPing, so send back Disconnect with the Reason string containing data (server description, number of users, number of slots), delimited by a §
            var clientCount = this.Server.GetAuthenticatedClients().Count();
            this.SendPacket(new DisconnectPacket() { Reason = String.Format("{0}§{1}§{2}", this.Server.ToString(), clientCount, Chraft.Properties.Settings.Default.MaxPlayers) });
        }

        private void PacketHandler_Disconnect(object sender, PacketEventArgs<DisconnectPacket> e)
        {
            Logger.Log(Logger.LogLevel.Info, DisplayName + " disconnected: " + e.Packet.Reason);
            this.Stop();
        }

        private void PacketHandler_Handshake(object sender, PacketEventArgs<HandshakePacket> e)
        {
            Username = Regex.Replace(e.Packet.UsernameOrHash, Chat.DISALLOWED, "");
            DisplayName = Username;
            SendHandshake();
        }

        private void PacketHandler_LoginRequest(object sender, PacketEventArgs<LoginRequestPacket> e)
        {
            if (!CheckUsername(e.Packet.Username))
                Kick("Inconsistent username");
            else if (e.Packet.ProtocolOrEntityId < ProtocolVersion)
                Kick("Outdated client");
            else
            {
                if (Server.UseOfficalAuthentication)
                {
                    try
                    {
                        string authenticated = Http.GetHttpResponse(new Uri(String.Format("http://www.minecraft.net/game/checkserver.jsp?user={0}&serverId={1}", e.Packet.Username, this.Server.ServerHash)));
                        if (authenticated != "YES")
                        {
                            Kick("Authentication failed");
                            return;
                        }
                    }
                    catch (Exception exc)
                    {
                        Kick("Error while authenticating...");
                        this.Logger.Log(exc);
                        return;
                    }
                }

                SendLoginSequence();
            }
        }

        #endregion
    }
}