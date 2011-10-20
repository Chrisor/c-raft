using System;
using Chraft.Utils;
using Chraft.World;

namespace Chraft.Entity
{
    public abstract class LivingEntity : EntityBase
    {

        short _health;
        /// <summary>
        /// Current entity Health represented as "halves of a heart", e.g. Health == 9 is 4.5 hearts. This value is clamped between 0 and EntityBase.MaxHealth.
        /// </summary>
        public virtual short Health
        {
            get { return _health; }
            set { _health = MathExtensions.Clamp(value, (short)0, this.MaxHealth); }
        }
        /// <summary>
        /// MaxHealth for this entity represented as "halves of a heart".
        /// </summary>
        public virtual short MaxHealth { get { return 20; } }
        
        public virtual float EyeHeight
        {
            get { return this.Height * 0.85f; }
        }
    
        public LivingEntity(Server server, int entityId)
         : base(server, entityId)
        {
            this.Health = MaxHealth;
        }
        
        /// <summary>
        /// Determines whether this instance can see the specified entity.
        /// </summary>
        /// <returns>
        /// <c>true</c> if this instance can see the specified entity; otherwise, <c>false</c>.
        /// </returns>
        /// <param name='entity'>
        /// The entity to check for line of sight to.
        /// </param>
        public bool CanSee(LivingEntity entity)
        {
            return this.World.RayTraceBlocks(new AbsWorldCoords(this.Position.X, this.Position.Y + this.EyeHeight, this.Position.Z), new AbsWorldCoords(entity.Position.X, entity.Position.Y + entity.EyeHeight, entity.Position.Z)) == null;
        }

        public string FacingDirection(byte points)
        {

            byte rotation = (byte)(Yaw * 256 / 360); // Gives rotation as 0 - 255, 0 being due E.

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


    }
}

