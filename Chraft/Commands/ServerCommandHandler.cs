﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;

namespace Chraft.Commands
{
    public class ServerCommandHandler : ICommandHandler
    {
        private List<IServerCommand> Commands;

        public ServerCommandHandler()
        {
            Commands = new List<IServerCommand>();
            Init();
        }
        /// <summary>
        /// Finds a command and returns it for use.
        /// 
        /// Exceptions:
        /// <exception cref="CommandNotFoundException">CommandNotFoundException</exception>
        /// </summary>
        /// <param name="Command">The name of the command to find.</param>
        /// <returns>A command with the given name.</returns>
        public ICommand Find(string Command)
        {
            foreach (IServerCommand cmd in Commands)
            {
                if (cmd.Name == Command)
                {
                    return cmd;
                }
            }
            IServerCommand Cmd;
            try
            {
                Cmd = FindShort(Command) as IServerCommand;
                return Cmd;
            }
            catch { }
            throw new CommandNotFoundException("The specified command was not found!");
        }
        /// <summary>
        /// Finds a command and returns it for use.
        /// 
        /// Exceptions:
        /// <exception cref="CommandNotFoundException">CommandNotFoundException</exception>
        /// </summary>
        /// <param name="Shortcut">The shortcut of the command to find.</param>
        /// <returns>A command with the given shortcut.</returns>
        public ICommand FindShort(string Shortcut)
        {
            foreach (IServerCommand cmd in Commands)
            {
                if (cmd.Shortcut == Shortcut)
                {
                    return cmd;
                }
            }
            throw new CommandNotFoundException("The specified command was not found!");
        }
        /// <summary>
        /// Registers a command with the server.
        /// Exceptions:
        /// <exception cref="CommandAlreadyExistsException">CommandAlreadyExistsException</exception>
        /// </summary>
        /// <param name="command">The <see cref="IServerCommand">Command</see> to register.</param>
        public void RegisterCommand(ICommand command)
        {
            if (command is IServerCommand)
            {
                foreach (IServerCommand cmd in Commands)
                {
                    if (cmd.Name == command.Name)
                    {
                        throw new CommandAlreadyExistsException("A command with the same name already exists!");
                    }
                    else if (cmd.Shortcut == command.Shortcut && !string.IsNullOrEmpty(cmd.Shortcut))
                    {
                        throw new CommandAlreadyExistsException("A command with the same shortcut already exists!");
                    }
                }
                IServerCommand Cmd = command as IServerCommand;
                Cmd.ServerCommandHandler = this;
                Commands.Add(Cmd);
            }
        }
        /// <summary>
        /// Removes a command from the server.
        /// 
        /// Exceptions:
        /// <exception cref="CommandNotFoundException">CommandNotFoundException</exception>
        /// </summary>
        /// <param name="command">The <see cref="IServerCommand">Command</see> to remove.</param>
        public void UnregisterCommand(ICommand command)
        {
            if (command is IServerCommand)
            {
                if (Commands.Contains(command))
                {
                    Commands.Remove(command as IServerCommand);
                }
                else
                {
                    throw new CommandNotFoundException("The given command was not found!");
                }
            }
        }
        /// <summary>
        /// Gets an array of all of the commands registerd.
        /// </summary>
        /// <returns>Array of <see cref="IServerCommand"/></returns>
        public ICommand[] GetCommands()
        {
            return Commands.ToArray();
        }
        private void Init()
        {
            foreach (Type t in from t in Assembly.GetExecutingAssembly().GetTypes()
                               where t.GetInterfaces().Contains(typeof(IServerCommand)) && !t.IsAbstract
                               select t)
            {
                RegisterCommand((IServerCommand)t.GetConstructor(Type.EmptyTypes).Invoke(null));
            }
        }
    }
}
