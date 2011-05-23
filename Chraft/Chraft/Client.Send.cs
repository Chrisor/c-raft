﻿using Chraft.Entity;
using Chraft.Net;
using Chraft.World;
using Chraft.Properties;
using System.Threading;
using Chraft.World.Weather;

namespace Chraft
{
	public partial class Client 
	{
		internal void SendPulse()
		{
			if (LoggedIn)
			{
				SynchronizeEntities();
				PacketHandler.SendPacket(new TimeUpdatePacket
				{
					Time = World.Time
				});
			}
		}

		internal void SendBlock(int x, int y, int z, byte type, byte data)
		{
			if (LoggedIn)
			{
				PacketHandler.SendPacket(new BlockChangePacket
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
						byte? type = World.GetBlockOrNull(x + dx, y + dy, z + dz);
						if (type != null)
							SendBlock(x + dx, y + dy, z + dz, type.Value, World.GetBlockData(x + dx, y + dy, z + dz));
					}
				}
			}
		}

		private void SendMotd()
		{
            string MOTD = Settings.Default.MOTD.Replace("%u", this.DisplayName);
			this.SendMessage(MOTD);
		}

		public void SendPacket(Packet packet)
		{
			this.PacketHandler.SendPacket(packet);
		}


		#region Login

		private void SendLoginRequest()
		{
			PacketHandler.SendPacket(new LoginRequestPacket
			{
				ProtocolOrEntityId = this.SessionID,
				Dimension = World.Dimension,
				Username = "",
				MapSeed = World.Seed
			});
		}

		private void SendInitialTime()
		{
			PacketHandler.SendPacket(new TimeUpdatePacket
			{
				Time = World.Time
			});
		}

		private void SendInitialPosition()
		{
			PacketHandler.SendPacket(new PlayerPositionRotationPacket
			{
				X = X,
				Y = Y + 1,
				Z = Z,
				Yaw = Yaw,
				Pitch = Pitch,
				Stance = Stance,
				OnGround = false
			});
		}

		private void SendSpawnPosition()
		{
			PacketHandler.SendPacket(new SpawnPositionPacket
			{
				X = World.Spawn.X,
				Y = World.Spawn.Y,
				Z = World.Spawn.Z
			});
		}

		private void SendHandshake()
		{
			PacketHandler.SendPacket(new HandshakePacket
			{
				UsernameOrHash = "-" // No authentication
			});
		}

		private void SendLoginSequence()
		{
			SendMessage("§cLoading, please wait...");
			Load();
			StartKeepAliveTimer();
			SendLoginRequest();
			SendSpawnPosition();
			UpdateChunks(2);
			SendInitialTime();
			SendInitialPosition();
			InitializeInventory();
            InitializeHealth();
			OnJoined();
			SendMotd();
			UpdateChunks(Settings.Default.SightRadius);
			SendMessage("§cLoading complete.");

			Thread thread = new Thread(UpdateChunksThread);
			thread.IsBackground = true;
			thread.Priority = ThreadPriority.Highest;
			thread.Start();
		}

		#endregion


		#region Chunks

		public void SendWeather(WeatherState weather, int x, int z)
		{
			// TODO: Implement weather
		}

		private void SendPreChunk(int x, int z, bool load)
		{
			PreChunkPacket prepacket = new PreChunkPacket
			{
				Load = load,
				X = x,
				Z = z
			};
			PacketHandler.SendPacket(prepacket);
		}

		internal void SendChunk(Chunk chunk)
		{
			MapChunkPacket packet = new MapChunkPacket
			{
				Chunk = chunk
			};
			PacketHandler.SendPacket(packet);
		}

		#endregion


		#region Entities

		private void SendCreateEntity(EntityBase entity)
		{
			if (entity is Client)
			{
				Client c = (Client)entity;
				PacketHandler.SendPacket(new NamedEntitySpawnPacket
				{
					EntityId = c.EntityId,
					X = c.X,
					Y = c.Y,
					Z = c.Z,
					Yaw = c.PackedYaw,
					Pitch = c.PackedPitch,
					PlayerName = c.Username + c.EntityId,
					CurrentItem = 0
				});
				for (short i = 0; i < 5; i++)
				{
					PacketHandler.SendPacket(new EntityEquipmentPacket
					{
						EntityId = c.EntityId,
						Slot = i,
						ItemId = -1,
						Durability = 0
					});
				}
			}
			else if (entity is ItemEntity)
			{
				ItemEntity item = (ItemEntity)entity;
				PacketHandler.SendPacket(new SpawnItemPacket
				{
					X = item.X,
					Y = item.Y,
					Z = item.Z,
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
                Logger.Log(Logger.LogLevel.Debug, ("ClientSpawn: Sending Mob " + mob.Type + " (" + mob.X + ", " + mob.Y + ", " + mob.Z + ")"));
				PacketHandler.SendPacket(new MobSpawnPacket
				{
					X = mob.X,
					Y = mob.Y,
					Z = mob.Z,
					Yaw = mob.PackedYaw,
					Pitch = mob.PackedPitch,
					EntityId = mob.EntityId,
					Type = mob.Type,
					Data = mob.Data
				});
			}
			else
			{
				SendEntity(entity);
				SendTeleportTo(entity);
			}
		}

		internal void SendEntity(EntityBase entity)
		{
			PacketHandler.SendPacket(new CreateEntityPacket
			{
				EntityId = entity.EntityId
			});
		}

		private void SendDestroyEntity(EntityBase entity)
		{
			PacketHandler.SendPacket(new DestroyEntityPacket
			{
				EntityId = entity.EntityId
			});
		}

		internal void SendTeleportTo(EntityBase entity)
		{
			PacketHandler.SendPacket(new EntityTeleportPacket
			{
				EntityId = entity.EntityId,
				X = entity.X,
				Y = entity.Y,
				Z = entity.Z,
				Yaw = entity.PackedYaw,
				Pitch = entity.PackedPitch
			});
			SendMoveBy(entity, (sbyte)((X - (int)entity.X) * 32), (sbyte)((Y - (int)entity.Y) * 32), (sbyte)((Z - (int)entity.Z) * 32));
		}

		internal void SendRotateBy(EntityBase entity, sbyte dyaw, sbyte dpitch)
		{
			PacketHandler.SendPacket(new EntityLookPacket
			{
				EntityId = entity.EntityId,
				Yaw = dyaw,
				Pitch = dpitch
			});
		}

		internal void SendMoveBy(EntityBase entity, sbyte dx, sbyte dy, sbyte dz)
		{
			PacketHandler.SendPacket(new EntityRelativeMovePacket
			{
				EntityId = entity.EntityId,
				DeltaX = dx,
				DeltaY = dy,
				DeltaZ = dz
			});
		}

		internal void SendMoveRotateBy(EntityBase entity, sbyte dx, sbyte dy, sbyte dz, sbyte dyaw, sbyte dpitch)
		{
			PacketHandler.SendPacket(new EntityLookAndRelativeMovePacket
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
            PacketHandler.SendPacket(new AttachEntityPacket
            {
                EntityId = entity.EntityId,
                VehicleId = attachTo.EntityId
            });
        }

		#endregion


        #region Clients

        private void SendHoldingEquipment(Client c) // Updates entity holding via 0x05
        {
            PacketHandler.SendPacket(new EntityEquipmentPacket
            {
                EntityId = c.EntityId,
                Slot = 0,
                ItemId = c.Inventory.ActiveItem.Type,
                Durability = c.Inventory.ActiveItem.Durability
            });
        }

        private void SendEntityEquipment(Client c, short slot) // Updates entity equipment via 0x05
        {
            PacketHandler.SendPacket(new EntityEquipmentPacket
            {
                EntityId = c.EntityId,
                Slot = slot,
                ItemId = c.Inventory.Slots[slot].Type,
                Durability = 0
            });
        }

        #endregion
	}
}
