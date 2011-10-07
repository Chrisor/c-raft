﻿using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using Chraft.Entity;
using Chraft.Net.Packets;
using Chraft.World;
using Chraft.Properties;
using System.Threading;
using Chraft.World.Weather;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Chraft.Net
{
    public partial class Client 
    {
        public ConcurrentQueue<Packet> packetsToBeSent = new ConcurrentQueue<Packet>();

        private int _TimesEnqueuedForSend;
        public void SendPacket(Packet packet)
        {
            if (!Running)
                return;

            packetsToBeSent.Enqueue(packet);

            int newValue = Interlocked.Increment(ref _TimesEnqueuedForSend);

            if ((newValue - 1) == 0)
                Server.SendClientQueue.Enqueue(this);

            //Logger.Log(Chraft.Logger.LogLevel.Info, "Sending packet: {0}", packet.GetPacketType().ToString());

            _Player.Server.NetworkSignal.Set();
        }

        public void Send_Async(byte[] data)
        {
            if (!Running)
            {
                DisposeSendSystem();
                return;
            }

            if (data[0] == (byte)PacketType.Disconnect)
                _SendSocketEvent.Completed += Disconnected;

            _SendSocketEvent.SetBuffer(data, 0, data.Length);

            bool pending = _Socket.SendAsync(_SendSocketEvent);

            if (!pending)
                Send_Completed(null, _SendSocketEvent);
        }

        public void Send_Sync(byte[] data)
        {
            if (!Running)
            {
                DisposeSendSystem();
                return;
            }
            _Socket.Send(data, data.Length, 0);
        }

        public void Send_Start(Packet packet = null)
        {
            if(!Running)
            {
                DisposeSendSystem();
                return;
            }

            try
            {
                byte[] data;
                if (packet == null)
                {
                    if (packetsToBeSent.Count > 0)
                    {
                        if (!packetsToBeSent.TryDequeue(out packet))
                        {
                            Interlocked.Exchange(ref _TimesEnqueuedForSend, 0);
                            return;
                        }

                        packet.Write();
                        data = packet.GetBuffer();

                        if (packet.Async)
                            Send_Async(data);
                        else
                        {
                            Send_Sync(data);
                            while (Running && packetsToBeSent.Count > 0)
                            {
                                if (!packetsToBeSent.TryDequeue(out packet))
                                {
                                    Interlocked.Exchange(ref _TimesEnqueuedForSend, 0);
                                    return;
                                }

                                if (packet.Async)
                                    break;

                                packet.Write();
                                data = packet.GetBuffer();

                                Send_Sync(data);
                                packet = null;
                            }

                            if (packet != null)
                                Send_Start(packet);
                            else
                                Interlocked.Exchange(ref _TimesEnqueuedForSend, 0);
                        }
                    }
                    else
                        Interlocked.Exchange(ref _TimesEnqueuedForSend, 0);
                }
                else
                {
                    packet.Write();
                    data = packet.GetBuffer();
                    Send_Async(data);
                }
            }
            catch (Exception e)
            {
                MarkToDispose();
                DisposeSendSystem();
                Logger.Log(Logger.LogLevel.Error, e.Message);
                // TODO: log something?
            }
            
        }

        private void Send_Completed(object sender, SocketAsyncEventArgs e)
        {
            if (e.Buffer[0] == (byte)PacketType.Disconnect)
                e.Completed -= Disconnected;
            if (!Running)
                DisposeSendSystem();
            else if(e.SocketError == SocketError.Success)
                Send_Start();
        }

        internal void SendPulse()
        {
            if(_Player.LoggedIn)
            {
                _Player.SynchronizeEntities();
                SendPacket(new TimeUpdatePacket
                {
                    Time = _Player.World.Time
                });
            }
        }

        internal void SendBlock(int x, int y, int z, byte type, byte data)
        {
            if (_Player.LoggedIn)
            {
                SendPacket(new BlockChangePacket
                {
                    Data = data,
                    Type = type,
                    X = x,
                    Y = (sbyte)y,
                    Z = z
                });
            }
        }

        /// <summary>
        /// Updates a region of blocks
        /// </summary>
        /// <param name="x">Start coordinate, X component</param>
        /// <param name="y">Start coordinate, Y component</param>
        /// <param name="z">Start coordinate, Z component</param>
        /// <param name="l">Length (X magnitude)</param>
        /// <param name="w">Height (Y magnitude)</param>
        /// <param name="h">Width (Z magnitude)</param>
        public void SendBlockRegion(int x, int y, int z, int l, int h, int w)
        {
            for (int dx = 0; dx < l; dx++)
            {
                for (int dy = 0; dy < h; dy++)
                {
                    for (int dz = 0; dz < w; dz++)
                    {
                        byte? type = _Player.World.GetBlockOrNull(x + dx, y + dy, z + dz);
                        if (type != null)
                            SendBlock(x + dx, y + dy, z + dz, type.Value, _Player.World.GetBlockData(x + dx, y + dy, z + dz));
                    }
                }
            }
        }

        private void SendMotd()
        {
            string MOTD = Settings.Default.MOTD.Replace("%u", _Player.DisplayName);
            SendMessage(MOTD);
        }


        #region Login

        public void SendLoginRequest()
        {
            SendPacket(new LoginRequestPacket
            {
                ProtocolOrEntityId = _Player.SessionID,
                Dimension = _Player.World.Dimension,
                Username = "",
                MapSeed = _Player.World.Seed,
                WorldHeight = 128,
                MaxPlayers = 50,
                Unknown = 2
            });
        }

        public void SendInitialTime()
        {
            SendPacket(new TimeUpdatePacket
            {
                Time = _Player.World.Time
            });
        }

        public void SendInitialPosition()
        {
            SendPacket(new PlayerPositionRotationPacket
            {
                X = _Player.Position.X,
                Y = _Player.Position.Y + Player.EyeGroundOffset,
                Z = _Player.Position.Z,
                Yaw = (float)_Player.Position.Yaw,
                Pitch = (float)_Player.Position.Pitch,
                Stance = Stance,
                OnGround = false
            });
        }

        public void SendSpawnPosition()
        {
            SendPacket(new SpawnPositionPacket
            {
                X = _Player.World.Spawn.WorldX,
                Y = _Player.World.Spawn.WorldY,
                Z = _Player.World.Spawn.WorldZ
            });
        }

        public void SendHandshake()
        {
            SendPacket(new HandshakePacket
            {

                UsernameOrHash = (_Player.Server.UseOfficalAuthentication ? _Player.Server.ServerHash : "-")
                //UsernameOrHash = "-" // No authentication
                //UsernameOrHash = this.Server.ServerHash // Official Minecraft server authentication
            });
        }

        public void SendLoginSequence()
        {
            _Player.Permissions = _Player.PermHandler.LoadClientPermission(this);
            Load();
            StartKeepAliveTimer();
            SendLoginRequest();
            SendSpawnPosition();
            SendInitialTime();
            // This must be sent sync otherwise we will fall through them
            _Player.UpdateChunks(2, true, CancellationToken.None);
            SendInitialPosition();
            SendInitialTime();
            SetGameMode();
            _Player.InitializeInventory();
            _Player.InitializeHealth();
            _Player.OnJoined();
            SendMotd();
            /*Thread.Sleep(10000);
            _UpdateChunks = new Task(() => _Player.UpdateChunks(Settings.Default.SightRadius, CancellationToken.None));
            _UpdateChunks.Start();*/
        }

        #endregion


        #region Chunks

        public void SendPreChunk(int x, int z, bool load, bool sync)
        {
            PreChunkPacket prepacket = new PreChunkPacket
            {
                Load = load,
                X = x,
                Z = z,
                Async = !sync
            };
            SendPacket(prepacket);
        }

        internal void SendChunk(Chunk chunk, bool sync)
        {
            MapChunkPacket packet = new MapChunkPacket
            {
                Chunk = chunk,
                Async = !sync
            };
            SendPacket(packet);
        }

        #endregion


        #region Entities

        public void SendCreateEntity(EntityBase entity)
        {
            if (entity is Player)
            {
                Player p = ((Player) entity);
                Client c = p.Client;

                SendPacket(new NamedEntitySpawnPacket
                {
                    EntityId = p.EntityId,
                    X = p.Position.X,
                    Y = p.Position.Y,
                    Z = p.Position.Z,
                    Yaw = p.PackedYaw,
                    Pitch = p.PackedPitch,
                    PlayerName = p.Username + p.EntityId,
                    CurrentItem = 0
                });
                for (short i = 0; i < 5; i++)
                {
                    SendPacket(new EntityEquipmentPacket
                    {
                        EntityId = p.EntityId,
                        Slot = i,
                        ItemId = -1,
                        Durability = 0
                    });
                }
            }
            else if (entity is ItemEntity)
            {
                ItemEntity item = (ItemEntity)entity;
                SendPacket(new SpawnItemPacket
                {
                    X = item.Position.X,
                    Y = item.Position.Y,
                    Z = item.Position.Z,
                    Yaw = item.PackedYaw,
                    Pitch = item.PackedPitch,
                    EntityId = item.EntityId,
                    ItemId = item.ItemId,
                    Count = item.Count,
                    Durability = item.Durability,
                    Roll = 0
                });
            }
            else if (entity is Mob)
            {
                
                Mob mob = (Mob)entity;
                Logger.Log(Logger.LogLevel.Debug, ("ClientSpawn: Sending Mob " + mob.Type + " (" + mob.Position.X + ", " + mob.Position.Y + ", " + mob.Position.Z + ")"));
                SendPacket(new MobSpawnPacket
                {
                    X = mob.Position.X,
                    Y = mob.Position.Y,
                    Z = mob.Position.Z,
                    Yaw = mob.PackedYaw,
                    Pitch = mob.PackedPitch,
                    EntityId = mob.EntityId,
                    Type = mob.Type,
                    Data = mob.Data
                });
            }
            else
                if (entity is TileEntity)
                {
                    
                }
                else
                {
                    SendEntity(entity);
                    SendTeleportTo(entity);
                }
        }

        internal void SendEntity(EntityBase entity)
        {
            SendPacket(new CreateEntityPacket
            {
                EntityId = entity.EntityId
            });
        }

        public void SendDestroyEntity(EntityBase entity)
        {
            SendPacket(new DestroyEntityPacket
            {
                EntityId = entity.EntityId
            });
        }

        internal void SendTeleportTo(EntityBase entity)
        {
            SendPacket(new EntityTeleportPacket
            {
                EntityId = entity.EntityId,
                X = entity.Position.X,
                Y = entity.Position.Y,
                Z = entity.Position.Z,
                Yaw = entity.PackedYaw,
                Pitch = entity.PackedPitch
            });

            //SendMoveBy(entity, (sbyte)((_Player.Position.X - (int)entity.Position.X) * 32), (sbyte)((_Player.Position.Y - (int)entity.Position.Y) * 32), (sbyte)((_Player.Position.Z - (int)entity.Position.Z) * 32));
        }

        internal void SendRotateBy(EntityBase entity, sbyte dyaw, sbyte dpitch)
        {
            SendPacket(new EntityLookPacket
            {
                EntityId = entity.EntityId,
                Yaw = dyaw,
                Pitch = dpitch
            });
        }

        internal void SendMoveBy(EntityBase entity, sbyte dx, sbyte dy, sbyte dz)
        {
            SendPacket(new EntityRelativeMovePacket
            {
                EntityId = entity.EntityId,
                DeltaX = dx,
                DeltaY = dy,
                DeltaZ = dz
            });
        }

        internal void SendMoveRotateBy(EntityBase entity, sbyte dx, sbyte dy, sbyte dz, sbyte dyaw, sbyte dpitch)
        {
            SendPacket(new EntityLookAndRelativeMovePacket
            {
                EntityId = entity.EntityId,
                DeltaX = dx,
                DeltaY = dy,
                DeltaZ = dz,
                Yaw = dyaw,
                Pitch = dpitch
            });
        }

        internal void SendAttachEntity(EntityBase entity, EntityBase attachTo)
        {
            SendPacket(new AttachEntityPacket
            {
                EntityId = entity.EntityId,
                VehicleId = attachTo.EntityId
            });
        }

        #endregion


        #region Clients

        public void SendHoldingEquipment(Client c) // Updates entity holding via 0x05
        {
            SendPacket(new EntityEquipmentPacket
            {
                EntityId = c.Owner.EntityId,
                Slot = 0,
                ItemId = c.Owner.Inventory.ActiveItem.Type,
                Durability = c.Owner.Inventory.ActiveItem.Durability
            });
        }

        public void SendEntityEquipment(Client c, short slot) // Updates entity equipment via 0x05
        {
            SendPacket(new EntityEquipmentPacket
            {
                EntityId = c.Owner.EntityId,
                Slot = slot,
                ItemId = c.Owner.Inventory.Slots[slot].Type,
                Durability = 0
            });
        }

        #endregion

        public void SendWeather(WeatherState weather, UniversalCoords coords)
        {

            //throw new NotImplementedException();
        }
    }
}
