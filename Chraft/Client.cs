﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using Chraft.Commands;
using Chraft.Entity;
using Chraft.Entity.Mobs;
using Chraft.Net;
using Chraft.Net.Packets;
using Chraft.Plugins.Events;
using Chraft.World;
using Chraft.Utils;
using Chraft.Properties;
using Chraft.Interfaces;
using Chraft.Plugins.Events.Args;
using System.Collections.Concurrent;

namespace Chraft
{
    public partial class Client
    {
        internal const int ProtocolVersion = 17;
        private volatile TcpClient Tcp;
        private Thread RxThread;
        private volatile bool Running = true;
        public PacketHandler PacketHandler { get; private set; }
        private Timer KeepAliveTimer;
        public ConcurrentDictionary<PointI, Chunk> LoadedChunks = new ConcurrentDictionary<PointI, Chunk>();
        private List<EntityBase> LoadedEntities = new List<EntityBase>();
        public volatile bool LoggedIn = false;
        private Interface CurrentInterface = null;
        private PermissionHandler PermHandler;
        public ClientPermission Permissions;
        internal int SessionID { get; private set; }

        /// <summary>
        /// The mixed-case, clean username of the client.
        /// </summary>
        public string Username { get; private set; }

        /// <summary>
        /// The name that we display of the client
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// The current inventory.
        /// </summary>
        public Inventory Inventory { get; private set; }

        /// <summary>
        /// Is the client muted from chat
        /// </summary>
        public bool IsMuted { get; set; }

        /// <summary>
        /// A reference to the server logger.
        /// </summary>
        public Logger Logger { get { return Server.Logger; } }

        public const double EyeGroundOffset = 1.6200000047683716;

        public bool Ready { get; set; }

        public byte GameMode { get; set; }

        /// <summary>
        /// Instantiates a new Client object.
        /// </summary>
        /// <param name="server">The Server to associate with the entity.</param>
        /// <param name="sessionId">The entity ID for the client.</param>
        /// <param name="tcp">The TCP client to be used for communication.</param>
        internal Client(Server server, int sessionId, TcpClient tcp)
            : base(server, sessionId)
        {
            EnsureServer(server);
            SessionID = sessionId;
            Tcp = tcp;
            PacketHandler = new PacketHandler(Server, tcp);
            Inventory = null;
            DisplayName = Username;
            InitializePosition();
            InitializeRecv();
            PermHandler = new PermissionHandler(server);

        }

        private void InitializePosition()
        {
            World = Server.GetDefaultWorld();
            Position = new Location(
                World.Spawn.X,
                World.Spawn.Y + 1,
                World.Spawn.Z);
        }

        private void InitializeInventory()
        {
            if (Inventory == null)
            {
                Inventory = new Inventory(this);

                for (short i = 0; i < Inventory.SlotCount; i++) // Void inventory slots (for Holding)
                {
                    Inventory[i] = ItemStack.Void;
                }

                Inventory[Inventory.ActiveSlot] = new ItemStack(278, 1, 0);
            }

            Inventory.UpdateClient();
        }

        public float FoodSaturation { get; set; }
        public short Food { get; set; }

        private void InitializeHealth()
        {
            if (Health <= 0)
            {
                Health = 20;
            }

            if (Food <= 0)
            {
                Food = 20;
            }
            FoodSaturation = 5.0f;

            PacketHandler.SendPacket(new UpdateHealthPacket
            {
                Health = this.Health,
                Food = this.Food,
                FoodSaturation = this.FoodSaturation,
            });
        }
        private void SetGameMode()
        {
            PacketHandler.SendPacket(new NewInvalidStatePacket { GameMode = GameMode, Reason = NewInvalidStatePacket.NewInvalidReason.ChangeGameMode });
        }

        internal void AssociateInterface(Interface iface)
        {
            iface.PacketHandler = PacketHandler;
        }

        private void CloseInterface()
        {
            if (CurrentInterface == null)
                return;
            PacketHandler.SendPacket(new CloseWindowPacket
            {
                WindowId = CurrentInterface.Handle
            });
        }

        public int Ping { get; set; }
        private int _lastKeepAliveId;
        private DateTime _keepAliveStart;
        private DateTime _lastClientResponse = DateTime.Now;
        private void KeepAliveTimer_Callback(object sender)
        {
            if (Running)
            {
                if ((DateTime.Now - _lastClientResponse).TotalSeconds > 60)
                {
                    // Client hasn't sent or responded to a keepalive within 60secs
                    this.Stop();
                    return;
                }
                _lastKeepAliveId = Server.Rand.Next();
                _keepAliveStart = DateTime.Now;
                PacketHandler.SendPacket(new KeepAlivePacket() { KeepAliveID = this._lastKeepAliveId });
            }
        }

        /// <summary>
        /// Move less than four blocks to the given destination.
        /// </summary>
        /// <param name="x">The X coordinate of the target.</param>
        /// <param name="y">The Y coordinate of the target.</param>
        /// <param name="z">The Z coordinate of the target.</param>
        public override void MoveTo(double x, double y, double z)
        {
            base.MoveTo(x, y, z);
            UpdateEntities();
        }

        /// <summary>
        /// Move less than four blocks to the given destination and rotate.
        /// </summary>
        /// <param name="x">The X coordinate of the target.</param>
        /// <param name="y">The Y coordinate of the target.</param>
        /// <param name="z">The Z coordinate of the target.</param>
        /// <param name="yaw">The absolute yaw to which client should change.</param>
        /// <param name="pitch">The absolute pitch to which client should change.</param>
        public override void MoveTo(double x, double y, double z, float yaw, float pitch)
        {
            base.MoveTo(x, y, z, yaw, pitch);
            UpdateEntities();
        }

        private void UpdateEntities()
        {
            IEnumerable<EntityBase> nearbyEntities = Server.GetNearbyEntities(World, Position.X, Position.Y, Position.Z);

            foreach (EntityBase e in nearbyEntities)
            {
                if (e.Equals(this))
                    continue;
                if (!LoadedEntities.Contains(e))
                    SendCreateEntity(e);
                if (this.Health > 0 && e is ItemEntity && Math.Abs(e.Position.X - Position.X) < 1 && Math.Abs(e.Position.Y - Position.Y) < 1 && Math.Abs(e.Position.Z - Position.Z) < 1)
                    PickupItem((ItemEntity)e);
            }

            foreach (EntityBase e in LoadedEntities)
            {
                if (nearbyEntities.Contains(e))
                    continue;
                SendDestroyEntity(e);
            }

            LoadedEntities = new List<EntityBase>(nearbyEntities);
        }

        /// <summary>
        /// Updates nearby players when Client is hurt.
        /// </summary>
        /// <param name="cause"></param>
        /// <param name="hitBy">The Client hurting the current Client.</param>
        /// <param name="args">First argument should always be the damage amount.</param>
        public void DamageClient(DamageCause cause, EntityBase hitBy = null, params object[] args)
        {

            //event start
            EntityDamageEventArgs entevent = new EntityDamageEventArgs(this, Convert.ToInt16(args[0]), null, cause);
            Server.PluginManager.CallEvent(Event.ENTITY_DAMAGE, entevent);
            if (GameMode == 1){entevent.EventCanceled = true;}
            if (entevent.EventCanceled) return;
            //event end

            switch (cause)
            {
                case DamageCause.BlockExplosion:
                    break;
                case DamageCause.Contact:
                    break;
                case DamageCause.Drowning:
                    break;
                case DamageCause.EntityAttack:
                    if (hitBy != null)
                    {

                    }
                    break;
                case DamageCause.EntityExplosion:
                    break;
                case DamageCause.Fall:
                    if (args.Length > 0)
                    {
                        Health -= Convert.ToInt16(args[0]);
                    }
                    break;
                case DamageCause.Fire:
                    break;
                case DamageCause.FireBurn:
                    break;
                case DamageCause.Lava:
                    break;
                case DamageCause.Lightning:
                    break;
                case DamageCause.Projectile:
                    break;
                case DamageCause.Suffocation:
                    break;
                case DamageCause.Void:
                    break;
                default:
                    Health -= 1;
                    break;

            }

            PacketHandler.SendPacket(new UpdateHealthPacket
            {
                Health = this.Health,
                Food = this.Food,
                FoodSaturation = this.FoodSaturation,
            });

            foreach (Client c in Server.GetNearbyPlayers(World, Position.X, Position.Y, Position.Z))
            {
                if (c == this)
                    continue;

                c.PacketHandler.SendPacket(new AnimationPacket // Hurt Animation
                {
                    Animation = 2,
                    PlayerId = this.EntityId
                });

                c.PacketHandler.SendPacket(new EntityStatusPacket // Hurt Action
                {
                    EntityId = this.EntityId,
                    EntityStatus = 2
                });
            }

            if (this.Health == 0)
                HandleDeath(hitBy);
        }

        /// <summary>
        /// Handles the death of the Client.
        /// </summary>
        /// <param name="hitBy">Who killed the current Client.</param>
        private void HandleDeath(EntityBase hitBy = null, string deathBy = "")
        {
            // TODO: Add config option for none/global/local death messages
            // ...Or maybe make messages a plugin?
            string deathMessage;

            if (hitBy == null && deathBy == "") // Generic message
            {
                deathBy = "mysteriously!";
            }
            else if (hitBy is Client)
            {
                Client c = (Client)hitBy;
                deathBy = "by " + c.DisplayName.ToString() + " using" + Server.Items.ItemName(c.Inventory.Slots[c.Inventory.ActiveSlot].Type);
            }
            else if (hitBy is Mob)
            {
                Mob m = (Mob)hitBy;
                deathBy = "by " + m.Type;
            }

            deathMessage = this.DisplayName.ToString() + " was killed " + deathBy;

            foreach (Client c in Server.GetNearbyPlayers(World, Position.X, Position.Y, Position.Z))
            {
                c.SendMessage(deathMessage);

                if (c == this)
                    continue;

                c.PacketHandler.SendPacket(new EntityStatusPacket // Death Action
                {
                    EntityId = this.EntityId,
                    EntityStatus = 3
                });
            }

            Inventory.DropAll((int)Position.X, (int)Position.Y, (int)Position.Z);
        }

        /// <summary>
        /// Handles the respawning of the Client, called from respawn packet.
        /// </summary>
        private void HandleRespawn()
        {
            // This can no doubt be improved as waiting on the updatechunk thread is quite slow.
            Server.RemoveEntity(this);

            Position.X = World.Spawn.X;
            Position.Y = World.Spawn.Y;
            Position.Z = World.Spawn.Z;

            StopUpdateChunks();
            UpdateChunks(1, CancellationToken.None, false);
            PacketHandler.SendPacket(new RespawnPacket { });
            UpdateEntities();
            //SendSpawnPosition();
            SendInitialPosition();
            SendInitialTime();
            InitializeInventory();
            InitializeHealth();          
            ScheduleUpdateChunks();
            

            Server.AddEntity(this);
        }

        private void PickupItem(ItemEntity item)
        {
            if (!Server.GetEntities().Contains(item))
                return;
            Server.RemoveEntity(item);

            foreach (Client c in Server.GetNearbyPlayers(item.World, item.Position.X, item.Position.Y, item.Position.Z))
            {
                c.PacketHandler.SendPacket(new CollectItemPacket
                {
                    EntityId = item.EntityId,
                    PlayerId = EntityId
                });
            }

            Inventory.AddItem(item.ItemId, (sbyte)item.Count, item.Durability);
        }


        public string FacingDirection(byte points)
        {

            byte rotation = (byte)(Position.Yaw * 256 / 360); // Gives rotation as 0 - 255, 0 being due E.

            if (points == 8)
            {
                if (rotation < 17 || rotation > 240)
                    return "E";
                if (rotation < 49)
                    return "SE";
                if (rotation < 81)
                    return "S";
                if (rotation < 113)
                    return "SW";
                if (rotation > 208)
                    return "NE";
                if (rotation > 176)
                    return "N";
                if (rotation > 144)
                    return "NW";
                return "W";
            }
            if (rotation < 32 || rotation > 224)
                return "E";
            if (rotation < 76)
                return "S";
            if (rotation > 140)
                return "N";
            return "W";
        }

        private void SynchronizeEntities()
        {
            foreach (EntityBase e in Server.GetNearbyEntities(World, Position.X, Position.Y, Position.Z))
            {
                if (e.Equals(this))
                    continue;
                PacketHandler.SendPacket(new EntityTeleportPacket
                {
                    EntityId = e.EntityId,
                    X = e.Position.X,
                    Y = e.Position.Y,
                    Z = e.Position.Z,
                    Yaw = e.PackedYaw,
                    Pitch = e.PackedPitch
                });
            }
        }

        public void UpdateChunks(int radius, CancellationToken token, bool remove = true)
        {
            List<PointI> nearbyChunks = new List<PointI>();
            List<PointI> toUpdate = new List<PointI>();
            int chunkX = (int)Position.X >> 4;
            int chunkZ = (int)Position.Z >> 4;

            for (int x = chunkX - radius; x <= chunkX + radius; ++x)
            {
                for (int z = chunkZ - radius; z <= chunkZ + radius; ++z)
                {
                    if (token.IsCancellationRequested)
                        return;

                    nearbyChunks.Add(new PointI(x, z));

                    if (!LoadedChunks.ContainsKey(new PointI(x, z)))
                    {
                        toUpdate.Add(new PointI(x, z));
                        SendPreChunk(x, z, true);
                    }
                }
            }

            foreach (PointI c in toUpdate)
            {
                if (token.IsCancellationRequested)
                    return;

                Chunk chunk = World[c.X, c.Z, true, true];
                chunk.AddClient(this);
                LoadedChunks.TryAdd(c, chunk);
                SendChunk(chunk);
            }

            if(remove)
            {
                foreach (PointI c in LoadedChunks.Keys.Where<PointI>(c => !nearbyChunks.Contains(c)))
                {
                    if (token.IsCancellationRequested)
                        return;

                    SendPreChunk(c.X, c.Z, false);
                    Chunk chunk;
                    LoadedChunks.TryRemove(c, out chunk);
                    chunk.RemoveClient(this);
                }
            }

        }

        /// <summary>
        /// Start reading packets from the client in a separate thread.
        /// </summary>
        public void Start()
        {
            RxThread = new Thread(RxProc) { IsBackground = true };
            RxThread.Start();
        }

        /// <summary>
        /// Stop reading packets from the client, and kill the keep-alive timer.
        /// </summary>
        public void Stop()
        {
            this.Ready = false;
            Running = false;
            if (KeepAliveTimer != null)
            {
                KeepAliveTimer.Change(Timeout.Infinite, Timeout.Infinite);
                KeepAliveTimer = null;
            }
        }

        /// <summary>
        /// Disconnect the client with the given reason.
        /// </summary>
        /// <param name="reason">The reason to be displayed to the player.</param>
        public void Kick(string reason)
        {
            //Event
            ClientKickedEventArgs e = new ClientKickedEventArgs(this, reason);
            Server.PluginManager.CallEvent(Event.PLAYER_KICKED, e);
            if (e.EventCanceled) return;
            reason = e.Message;
            //End Event
            PacketHandler.SendPacket(new DisconnectPacket
            {
                Reason = reason
            });
            Stop();
        }

        private void RxProc()
        {
            try
            {
                while (Running)
                {
                    if (!PacketHandler.ProcessPacket())
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Log(Logger.LogLevel.Error, "Killing client: " + ex);
            }
            finally
            {
                Dispose();
            }
        }

        /// <summary>
        /// Disposes associated resources and stops the client.  Also removes the client from the server's client/entity lists.
        /// </summary>
        public void Dispose()
        {
            string disconnectMsg = ChatColor.Yellow + DisplayName + " has left the game.";
            StopUpdateChunks();
            //Event
            ClientLeftEventArgs e = new ClientLeftEventArgs(this);
            Server.PluginManager.CallEvent(Plugins.Events.Event.PLAYER_LEFT, e);
            //You cant stop the player from leaving so dont try.
            disconnectMsg = e.BrodcastMessage;
            //End Event
            Server.Broadcast(disconnectMsg);

            Save();
            Running = false;
            LoggedIn = false;
            PacketHandler.Dispose();

            Server.Clients.Remove(this.SessionID);
            Server.RemoveEntity(this);
            foreach (PointI c in LoadedChunks.Keys)
            {
                Chunk chunk = World[c.X, c.Z, false, false];
                if (chunk != null)
                    chunk.RemoveClient(this);
            }

            if (Tcp.Connected)
                Tcp.Close();

            GC.Collect();
        }

        /// <summary>
        /// Sends a message to the player via chat.
        /// </summary>
        /// <param name="message">The message to be displayed in the chat HUD.</param>
        public void SendMessage(string message)
        {
            PacketHandler.SendPacket(new ChatMessagePacket
            {
                Message = message
            });
        }

        private void StartKeepAliveTimer()
        {
            KeepAliveTimer = new Timer(KeepAliveTimer_Callback, null, 10000, 10000);
        }

        private bool CheckUsername(string username)
        {
            string usernameToCheck = Regex.Replace(username, Chat.DISALLOWED, "");
            Logger.Log(Logger.LogLevel.Debug, "Username: {0}", usernameToCheck);
            return usernameToCheck == Username;
        }

        private void OnJoined()
        {
            LoggedIn = true;
            string DisplayMessage = DisplayName + " has logged in";
            //Event
            ClientJoinedEventArgs e = new ClientJoinedEventArgs(this);
            Server.PluginManager.CallEvent(Event.PLAYER_JOINED, e);
            //We kick the player because it would not work to use return.
            if (e.EventCanceled) Kick("");
            DisplayMessage = e.BrodcastMessage;
            //End Event
            Server.Broadcast(DisplayMessage);
        }


        private void SetHealth(short health)
        {
            if (health > 20)
            {
                health = 20;
            }
            this.Health = health;
            PacketHandler.SendPacket(new UpdateHealthPacket
            {
                Health = this.Health,
                Food = this.Food,
                FoodSaturation = this.FoodSaturation,
            });
        }


        #region Permission related commands
        //Check if the player has permissions to use the command from a command object
        public bool CanUseCommand(Command command)
        {
            return PermHandler.HasPermission(this, command);
        }

        //
        public bool CanUseCommand(string command)
        {
            return PermHandler.HasPermission(Username, command);
        }

        //Returns the players prefix
        public string GetPlayerPrefix()
        {
            return PermHandler.GetPlayerPrefix(this);
        }
        //returns the players suffix
        public string GetPlayerSuffix()
        {
            return PermHandler.GetPlayerSuffix(this);
        }
        #endregion

    }
    //Cheat enum since enums can't be strings.
    public static class ChatColor
    {
        public static string Black = "§0",
        DarkBlue = "§1",
        DarkGreen = "§2",
        DarkTeal = "§3",
        DarkRed = "§4",
        Purple = "§5",
        Gold = "§6",
        Gray = "§7",
        DarkGray = "§8",
        Blue = "§9",
        BrightGreen = "§a",
        Teal = "§b",
        Red = "§c",
        Pink = "§d",
        Yellow = "§e",
        White = "§f";
    }
}