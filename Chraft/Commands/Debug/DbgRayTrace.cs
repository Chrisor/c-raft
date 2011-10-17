using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Chraft.Net;
using Chraft.Utils;
using Chraft.World;

namespace Chraft.Commands.Debug
{
    public class DbgRayTrace : ClientCommand
    {
        public ClientCommandHandler ClientCommandHandler { get; set; }

        public void Use(Client client, string commandName, string[] tokens)
        {
            Vector3 facing = new Vector3(client.Owner.Yaw, client.Owner.Pitch);
                
                
            Vector3 start = new Vector3(client.Owner.Position.X, client.Owner.Position.Y + client.Owner.EyeHeight, client.Owner.Position.Z);
            Vector3 end = facing * 120 + start;

            if (tokens.Length == 0)
            {
                // Ray trace out 50 blocks along client facing direction
                RayTraceHitBlock hit = client.Owner.World.RayTraceBlocks(new AbsWorldCoords(start), new AbsWorldCoords(end));
                
                if (hit == null)
                    client.SendMessage(String.Format("No block targetted within {0} metres", start.Distance(end)));
                else
                {
                    client.SendMessage(hit.ToString());
                }
            }
            else if (tokens[0] == "destroy") // destroy the targetted block
            {
                RayTraceHitBlock hit = client.Owner.World.RayTraceBlocks(new AbsWorldCoords(start), new AbsWorldCoords(end));
                
                if (hit != null)
                {
                    client.Owner.World.SetBlockAndData(hit.TargetBlock, 0, 0);
                }
            }
            else if (tokens[0] == "perf") // performance check
            {
                DateTime startTime = DateTime.Now;
                RayTraceHitBlock hit = null;
                for (int i = 0; i < 1000; i++)
                {
                    hit = client.Owner.World.RayTraceBlocks(new AbsWorldCoords(start), new AbsWorldCoords(end));
                }
                
                DateTime endTime = DateTime.Now;
                if (hit != null)
                {
                    client.SendMessage(String.Format("Time to ray trace {0} metres:", start.Distance(hit.Hit)));
                }
                else
                {
                    client.SendMessage(String.Format("Time to ray trace {0} metres:", start.Distance(end)));
                }
                client.SendMessage(((endTime - startTime).TotalMilliseconds/1000.0).ToString() + " ms");
            }
        }

        public void Help(Client client)
        {
            
        }

        public string Name
        {
            get { return "dbgraytrace"; }
        }

        public string Shortcut
        {
            get { return "dbgray"; }
        }

        public CommandType Type
        {
            get { return CommandType.Information; }
        }

        public string Permission
        {
            get { return "chraft.debug"; }
        }
    }
}

