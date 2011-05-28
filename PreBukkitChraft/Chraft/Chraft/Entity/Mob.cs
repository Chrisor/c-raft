﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using Chraft.Net;
using Chraft.Net.Packets;

namespace Chraft.Entity
{
	public class Mob : EntityBase
	{
		public MobType Type { get; set; }
		public MetaData Data { get; private set; }
        //public int Health { get; set; }

        public int AttackRange; // Clients within this range will take damage
        public int SightRange; // Clients within this range will be hunted
        public int GotoLoc; // Location as int entity should move towards
        //public double gotoX, gotoY, gotoZ; // Location entity should move towards
        public World.NBT.Vector3 gotoPos; // Location entity should move towards

        public bool Hunter; // Is this mob capable of tracking clients?
        public bool Hunting; // Is this mob currently tracking a client?
        
		public Mob(Server server, int entityId, MobType type)
			: this(server, entityId, type, new MetaData())
		{
		}

		public Mob(Server server, int entityId, MobType type, MetaData data)
			: base(server, entityId)
		{
			Data = data;
			Type = type;
		}

        public void DamageMob(Client hitBy = null)
        {
            if (hitBy != null)
            {
                // TODO: Get the Clients held item.
                this.Health -= 1;
            }
            else
            {
                // TODO: Generic damage from falling/lava/fire?
                this.Health -= 1;
            }

            foreach (Client c in World.Server.GetNearbyPlayers(World, Position.X, Position.Y, Position.Z))
            {
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

            // TODO: Entity Knockback

            if (this.Health == 0) HandleDeath(hitBy);
        }

        public void HandleDeath(Client hitBy = null)
        {
            if (hitBy != null)
            {
                // TODO: Stats/Achievement hook or something
            }

            foreach (Client c in World.Server.GetNearbyPlayers(World, Position.X, Position.Y, Position.Z))
            {
                c.PacketHandler.SendPacket(new EntityStatusPacket // Death Action
                {
                    EntityId = this.EntityId,
                    EntityStatus = 3
                });
            }

            // TODO: Spawn goodies

            System.Timers.Timer removeTimer = new System.Timers.Timer(1000);

            removeTimer.Elapsed += delegate
            {
                removeTimer.Stop();
                World.Server.Entities.Remove(this);
                removeTimer.Dispose();
            };

            removeTimer.Start();
        }

        public void HuntMode()
        {
            int newGotoLoc;

            foreach (Client c in World.Server.GetNearbyPlayers(World, Position.X, Position.Y, Position.Z))
            {
                if (Math.Abs(c.Position.X - Position.X) <= AttackRange)
                {
                    if (Math.Abs(c.Position.Y - Position.Y) < 1)
                    {
                        if (Math.Abs(c.Position.Z - Position.Z) <= AttackRange)
                        {
                            //c.DamageClient(this);
                        }
                    }
                }

                newGotoLoc = (int)Math.Abs(c.Position.X - Position.X) + (int)Math.Abs(c.Position.Y - Position.Y) + (int)Math.Abs(c.Position.Z - Position.Z);
                if (GotoLoc < newGotoLoc && GotoLoc < SightRange)
                {
                    this.World.Logger.Log(Logger.LogLevel.Debug, "Found: " + Position.X + ", " + Position.Y + ", " + Position.Z);
                    GotoLoc = newGotoLoc;
                    gotoPos.X = c.Position.X;
                    gotoPos.Y = c.Position.Y;
                    gotoPos.Z = c.Position.Z;
                    Hunting = true;
                }
            }

            if (Hunting != true)
                PassiveMode();
            //else
                //ProcessMovement(gotoPos.X, gotoPos.Y, gotoPos.Z);
        }

        public void PassiveMode()
        {
            if(gotoPos == null)
                gotoPos = Position;
            /*
            if (Hunter)
            {
                foreach (Client c in World.Server.GetNearbyPlayers(World, Position.X, Position.Y, Position.Z))
                {
                    int newGotoLoc = (int)Math.Abs(c.Position.X - Position.X) + (int)Math.Abs(c.Position.Y - Position.Y) + (int)Math.Abs(c.Position.Z - Position.Z);
                    if (newGotoLoc < SightRange)
                    {
                        Hunting = true;
                        HuntMode();
                        return;
                    }
                }
            }*/
            /*
            if (gotoPos.X != Position.X && gotoPos.Y != Position.Y && gotoPos.Z != Position.Z)
            {
                gotoPos.X = Position.X + 2;
                gotoPos.Y = Position.Y;
                gotoPos.Z = Position.Z + 2;
            }*/
            /*
            if (Yaw < 128) Yaw += 8;
            else Yaw = 0;

            if (Pitch > 16 && Pitch < 32) Pitch = 128;
            else if (Pitch > 200) Pitch = 0;
            else Pitch += 1;
            */
            if(Position.Equals(gotoPos))
                if (new Random().Next(100) > 60) {
                    gotoPos.X += new Random().Next(3) - 1.5;
                    gotoPos.Z += new Random().Next(3) - 1.5;
                }
            ProcessMovement(gotoPos.X, gotoPos.Y, gotoPos.Z);
        }

        private void ProcessMovement(double mX, double mY, double mZ)
        {
            int x = (int)(Position.X + (Math.Sign(mX - Position.X)));
            int y = (int)(Position.Y - 1);
            int z = (int)(Position.Z + (Math.Sign(mZ - Position.Z)));
           
            byte b = World.GetBlockId(x, y, z);
            //byte b1 = World.GetBlockId(x, y + 1, z);
            //byte b2 = World.GetBlockId(x, y + 2, z);
            //byte b3 = World.GetBlockId(x, y + 3, z);

            //if (b != 0)
            //    Position.Y += 1;

            Position.X += (World.GetBlockId((int)Math.Sign(mX - Position.X), (int)Position.Y, (int)Position.Z) != 0) ? 0 : Math.Sign(mX - Position.X) * 0.2;
            Position.Z += (World.GetBlockId((int)Position.X, (int)Position.Y, (int)Math.Sign(mZ - Position.Z)) != 0) ? 0 : Math.Sign(mZ - Position.Z) * 0.2;
            Position.Y += (b == 0) ? -1 : 0;
            if (World.GetBlockId((int)this.Position.X + 1, (int)this.Position.Y, (int)this.Position.Z) != 0)
                Position.Y += 1;

            foreach (Client c in World.Server.GetNearbyPlayers(World, Position.X, Position.Y, Position.Z))
            {
                    c.PacketHandler.SendPacket(new EntityTeleportPacket
                    {
                        EntityId = this.EntityId,
                        //X = x,
                        //Y = Position.Y,
                        //Z = z,
                        X = this.Position.X,
                        Y = this.Position.Y,
                        Z = this.Position.Z,
                        Yaw = this.PackedYaw,
                        Pitch = this.PackedPitch
                    });
            }
        }
	}
}
