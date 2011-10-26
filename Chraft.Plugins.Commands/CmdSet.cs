﻿using Chraft.Commands;
using Chraft.Net;
using Chraft.World;
using Chraft.Interfaces;

namespace Chraft.Plugins.Commands
{
    public class CmdSet : IClientCommand
    {
        public CmdSet(IPlugin plugin)
        {
            Iplugin = plugin;
        }
        public ClientCommandHandler ClientCommandHandler { get; set; }

        public void Use(Client client, string commandName, string[] tokens)
        {
            if (client.Point2 == null || client.Point1 == null)
            {
                client.SendMessage("§cPlease select a cuboid first.");
                return;
            }

            UniversalCoords start = client.SelectionStart.Value;
            UniversalCoords end = client.SelectionEnd.Value;

            ItemStack item = client.Owner.Server.Items[tokens[0]];
            if (ItemStack.IsVoid(item))
            {
                client.SendMessage("§cUnknown item.");
                return;
            }

            if (item.Type > 255)
            {
                client.SendMessage("§cInvalid item.");
            }

            for (int x = start.WorldX; x <= end.WorldX; x++)
            {
                for (int y = start.WorldY; y <= end.WorldY; y++)
                {
                    for (int z = start.WorldZ; z <= end.WorldZ; z++)
                    {
                        client.Owner.World.SetBlockAndData(UniversalCoords.FromWorld(x, y, z), (byte)item.Type, (byte)item.Durability);
                    }
                }
            }
        }

        public void Help(Client client)
        {
            client.SendMessage("/set <Block> - Sets the selected cuboid to <Block>");
        }

        public string Name
        {
            get { return "set"; }
            set { }
        }

        public string Shortcut
        {
            get { return ""; }
        }

        public CommandType Type
        {
            get { return CommandType.Build; }
        }

        public string Permission
        {
            get { return "chraft.set"; }
        }

        public IPlugin Iplugin { get; set; }
    }
}
