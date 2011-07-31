﻿using System;
using System.Linq;
using Chraft.Net;
using Chraft.Entity;
using Chraft.Net.Packets;
using Chraft.Plugins.Events;
using Chraft.World;
using Chraft.Utils;
using Chraft.Interfaces;
using Chraft.Commands;
using Chraft.Plugins.Events.Args;

namespace Chraft
{
    public partial class Client : EntityBase, IDisposable
    {
        /// <summary>
        /// Invoked whenever the user sends a command.
        /// </summary>
        public event EventHandler<CommandEventArgs> Command;

        /// <summary>
        /// Invoked prior to a chat message transmission.
        /// </summary>
        public event EventHandler<ChatEventArgs> ChatMessage;

        /// <summary>
        /// Gets or sets the first point of the cuboid selection.
        /// </summary>
        public PointI? Point1 { get; set; }

        /// <summary>
        /// Gets or sets the second point of the cuboid selection.
        /// </summary>
        public PointI? Point2 { get; set; }

        /// <summary>
        /// Gets or sets the start of the cuboid selection.
        /// </summary>
        public PointI? SelectionStart
        {
            get
            {
                if (Point1 == null || Point2 == null)
                    return null;
                return new PointI(Point1.Value.X < Point2.Value.X ? Point1.Value.X : Point2.Value.X,
                    Point1.Value.Y < Point2.Value.Y ? Point1.Value.Y : Point2.Value.Y,
                    Point1.Value.Z < Point2.Value.Z ? Point1.Value.Z : Point2.Value.Z);
            }
        }

        /// <summary>
        /// Gets or sets the end of the cuboid selection.
        /// </summary>
        public PointI? SelectionEnd
        {
            get
            {
                if (Point1 == null || Point2 == null)
                    return null;
                return new PointI(Point1.Value.X > Point2.Value.X ? Point1.Value.X : Point2.Value.X,
                    Point1.Value.Y > Point2.Value.Y ? Point1.Value.Y : Point2.Value.Y,
                    Point1.Value.Z > Point2.Value.Z ? Point1.Value.Z : Point2.Value.Z);
            }
        }

        /// <summary>
        /// Send a chat message from the user.
        /// </summary>
        /// <param name="clean">The pre-cleaned message to be sent.</param>
        public void ExecuteChat(string clean)
        {
            //Event
            ClientPreChatEventArgs e1 = new ClientPreChatEventArgs(this, clean);
            Server.PluginManager.CallEvent(Event.PLAYER_PRE_CHAT, e1);
            if (e1.EventCanceled) return;
            clean = e1.Message;
            //End Event
            
            if (IsMuted)
            {
                SendMessage("You have been muted");
                return;
            }

            if ((clean = OnChat(clean)) != null)
            {
                //Event
                ClientChatEventArgs e2 = new ClientChatEventArgs(this, clean);
                Server.PluginManager.CallEvent(Event.PLAYER_CHAT, e2);
                if (e2.EventCanceled) return;
                clean = e2.Message;
                //End Event
                
                Server.Broadcast(Chat.Format(DisplayName, clean));
                Logger.Log(Logger.LogLevel.Info, "{0}: {1}", DisplayName, clean);
            }
        }

        private string OnChat(string message)
        {
            ChatEventArgs e = new ChatEventArgs
            {
                Cancelled = false,
                Message = message
            };
            if (ChatMessage != null)
                ChatMessage.Invoke(this, e);
            return e.Cancelled ? null : e.Message;
        }

        /// <summary>
        /// Execute a command in the context of the user.
        /// </summary>
        /// <param name="command">The command text, with the slash removed.</param>
        public void ExecuteCommand(string command)
        {
            //Event
            ClientPreCommandEventArgs e = new ClientPreCommandEventArgs(this, command);
            Server.PluginManager.CallEvent(Event.PLAYER_PRE_COMMAND, e);
            if (e.EventCanceled) return;
            command = e.Command;
            //End Event
            
            if (!CanUseCommand(command))
            {
                SendMessage("You do not have permission to use that command");
                return;
            }
            Logger.Log(Logger.LogLevel.Info, DisplayName + " issued server command: " + command);
            Server.Broadcast(DisplayName + " executed command " + command, this);
            CommandProc(command, Chat.Tokenize(command));
        }

        private void CommandProc(string raw, string[] tokens)
        {
            ClientCommand cmd;
            try
            {
                cmd = Server.ClientCommandHandler.Find(tokens[0]) as ClientCommand;
            }
            catch (CommandNotFoundException e)
            {
                SendMessage(ChatColor.Red + e.Message);
                return;
            }
            try
            {
                //Event
                ClientCommandEventArgs e = new ClientCommandEventArgs(this, cmd, tokens);
                Server.PluginManager.CallEvent(Event.PLAYER_COMMAND, e);
                if (e.EventCanceled) return;
                tokens = e.Tokens;
                //End Event
                
                cmd.Use(this, tokens);
            }
            catch(Exception e)
            {
                SendMessage("There was an error while executing the command.");
                Server.Logger.Log(e);
            }
        }

        private void SetHealth(string[] tokens)
        {
            if (tokens.Length < 1)
            {
                SetHealth(20);
                return;
            }
            SetHealth(short.Parse(tokens[1]));
        }
    }
}
