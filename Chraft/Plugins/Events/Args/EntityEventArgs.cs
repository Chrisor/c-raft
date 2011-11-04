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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Chraft.Entity;
using Chraft.Net;
using Chraft.World;
using Chraft.Utils;

namespace Chraft.Plugins.Events.Args
{
    /// <summary>
    /// The base EventArgs for an Entity Event.
    /// </summary>
    public class EntityEventArgs : ChraftEventArgs
    {
        public EntityBase Entity { get; private set; }
        public EntityEventArgs(EntityBase entity)
        {
            Entity = entity;
        }
    }

    public class EntityDeathEventArgs : EntityEventArgs 
    {
        public EntityBase KilledBy { get; set; }
        public EntityDeathEventArgs(EntityBase entity, EntityBase killedBy)
            : base(entity)
        {
            KilledBy = killedBy;
        }
    }
    public class EntitySpawnEventArgs : EntityEventArgs 
    {
        public AbsWorldCoords Location { get; set; }

        public EntitySpawnEventArgs(EntityBase entity, AbsWorldCoords Location)
            : base(entity)
        {
            this.Location = Location;
        }
    }
    public class EntityMoveEventArgs : EntityEventArgs 
    {
        public AbsWorldCoords NewPosition { get; set; }
        public AbsWorldCoords OldPosition { get; private set; }

        public EntityMoveEventArgs(EntityBase entity, AbsWorldCoords newPosition, AbsWorldCoords oldPosition) 
            : base(entity)
        {
            NewPosition = newPosition;
            OldPosition = oldPosition;
        }
    }
    public class EntityDamageEventArgs : EntityEventArgs
    {
        public short Damage { get; set; }
        public EntityBase DamagedBy { get; set; }
        public DamageCause Cause { get; set; }

        public EntityDamageEventArgs(EntityBase entity, short damage, EntityBase damagedBy, DamageCause cause)
            : base(entity)
        {
            Damage = damage;
            DamagedBy = damagedBy;
            Cause = cause;
        }
    }
    public class EntityAttackEventArgs : EntityEventArgs
    {
        public short Damage { get; set; }
        public EntityBase EntityToAttack { get; set; }

        public EntityAttackEventArgs(EntityBase entity, short damage, EntityBase entityToAttack)
            : base(entity)
        {
            Damage = damage;
            EntityToAttack = entityToAttack;
        }
    }
}
