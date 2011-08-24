﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Chraft.World;

namespace Chraft.Entity.Mobs
{
    public class Creeper : Mob
    {
        public override string Name
        {
            get { return "Creeper"; }
        }

        public override short AttackStrength
        {
            get
            {
                return 20; // 10 hearts (double when charged) varies based on proximity to blast radius
            }
        }

        internal Creeper(Chraft.World.WorldManager world, int entityId, Chraft.Net.MetaData data = null)
            : base(world, entityId, MobType.Creeper, data)
        {
        }

        protected override void DoDeath(EntityBase killedBy)
        {
            var killedByMob = killedBy as Mob;

            if (killedByMob.Type == MobType.Skeleton)
            {
                // If killed by a skeleton drop a music disc
                sbyte count = 1;
                short item;
                if (Server.Rand.Next(2) > 1)
                {
                    item = (short)BlockData.Items.Gold_Record;
                }
                else
                {
                    item = (short)BlockData.Items.Green_Record;
                }
                Server.DropItem(World, (int)this.Position.X, (int)this.Position.Y, (int)this.Position.Z, new Interfaces.ItemStack(item, count, 0));
            }
            else
            {
                sbyte count = (sbyte)Server.Rand.Next(2);
                if (count > 0)
                    Server.DropItem(World, (int)this.Position.X, (int)this.Position.Y, (int)this.Position.Z, new Interfaces.ItemStack((short)BlockData.Items.Gunpowder, count, 0));
            }
        }
    }
}
