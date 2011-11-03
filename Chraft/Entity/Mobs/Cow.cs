using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Chraft.Net;
using Chraft.World;

namespace Chraft.Entity.Mobs
{
    public class Cow: Animal
    {
        public override string Name
        {
            get { return "Cow"; }
        }

        public override short MaxHealth { get { return 10; } }

        internal Cow(Chraft.World.WorldManager world, int entityId, Chraft.Net.MetaData data = null)
            : base(world, entityId, MobType.Cow, data)
        {
        }

        protected override void DoDeath(EntityBase killedBy)
        {
            sbyte count = (sbyte)Server.Rand.Next(2);
            if (count > 0)
                Server.DropItem(World, UniversalCoords.FromAbsWorld(Position.X, Position.Y, Position.Z), new Interfaces.ItemStack((short)BlockData.Items.Leather, count, 0));
            base.DoDeath(killedBy);
        }
    }
}
