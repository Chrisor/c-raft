using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Chraft.Entity;
using Chraft.Net.Packets;
using Chraft.Plugins.Events;
using Chraft.World;
using Chraft.Utils;
using Chraft.Plugins.Events.Args;

namespace Chraft.Net
{
    public partial class Client
    {
        internal const int ProtocolVersion = 17;
        private readonly Socket _socket;
        public volatile bool Running = true;
        public PacketHandler PacketHandler { get; private set; }
        private Timer _keepAliveTimer;
        private readonly Player _player = null;

        public static SocketAsyncEventArgsPool SendSocketEventPool = new SocketAsyncEventArgsPool(10);
        public static SocketAsyncEventArgsPool RecvSocketEventPool = new SocketAsyncEventArgsPool(10);
        public static BufferPool RecvBufferPool = new BufferPool("Receive", 2048, 2048);


        private byte[] _recvBuffer;
        private SocketAsyncEventArgs _sendSocketEvent;
        private SocketAsyncEventArgs _recvSocketEvent;

        private ByteQueue _currentBuffer;
        private ByteQueue _processedBuffer;
        private ByteQueue _fragPackets;

        private bool _sendSystemDisposed;
        private bool _recvSystemDisposed;

        private readonly object _disposeLock = new object();

        private DateTime _nextActivityCheck;

        public Player Owner
        {
            get { return _player; }
        }

        public ByteQueue FragPackets
        {
            get { return _fragPackets; }
            set { _fragPackets = value; }
        }

        /// <summary>
        /// A reference to the server logger.
        /// </summary>
        public Logger Logger { get { return _player.Server.Logger; } }

        /// <summary>
        /// Instantiates a new Client object.
        /// </summary>
        internal Client(Socket socket, Player player)
        {
            _socket = socket;
            _player = player;
            _player.Client = this;
            _currentBuffer = new ByteQueue();
            _processedBuffer = new ByteQueue();
            _fragPackets = new ByteQueue();
            _nextActivityCheck = DateTime.Now + TimeSpan.FromSeconds(5.0);
            //PacketHandler = new PacketHandler(Server, socket);
        }

        private void SetGameMode()
        {
            SendPacket(new NewInvalidStatePacket
            {
                GameMode = _player.GameMode,
                Reason = NewInvalidStatePacket.NewInvalidReason.ChangeGameMode
            });
        }

        public void Start()
        {
            Running = true;
            _sendSocketEvent = SendSocketEventPool.Pop();
            _recvSocketEvent = RecvSocketEventPool.Pop();
            _recvBuffer = RecvBufferPool.AcquireBuffer();

            _recvSocketEvent.SetBuffer(_recvBuffer, 0, _recvBuffer.Length);
            _recvSocketEvent.Completed += new EventHandler<SocketAsyncEventArgs>(Recv_Completed);

            _sendSocketEvent.Completed += new EventHandler<SocketAsyncEventArgs>(Send_Completed);

            Task.Factory.StartNew(Recv_Start);
        }

        /*internal void AssociateInterface(Interface iface)
        {
            iface.PacketHandler = PacketHandler;
        }*/

        private void CloseInterface()
        {
            if (_player.CurrentInterface == null)
                return;
            SendPacket(new CloseWindowPacket
            {
                WindowId = _player.CurrentInterface.Handle
            });
        }

        public int Ping { get; set; }
        public int LastKeepAliveId;
        public DateTime KeepAliveStart;
        public DateTime LastClientResponse = DateTime.Now;

        private void KeepAliveTimer_Callback(object sender)
        {
            if (Running)
            {
                if ((DateTime.Now - LastClientResponse).TotalSeconds > 60)
                {
                    // Client hasn't sent or responded to a keepalive within 60secs
                    this.Stop();
                    return;
                }
                LastKeepAliveId = _player.Server.Rand.Next();
                KeepAliveStart = DateTime.Now;
                SendPacket(new KeepAlivePacket() {KeepAliveID = this.LastKeepAliveId});
            }
        }

        public void CheckAlive()
        {
            if(DateTime.Now > _nextActivityCheck)
                Stop();
        }

        /// <summary>
        /// Stop reading packets from the client, and kill the keep-alive timer.
        /// </summary>
        public void Stop()
        {
            MarkToDispose();
            DisposeRecvSystem();
            DisposeSendSystem();
        }

        /// <summary>
        /// Disconnect the client with the given reason.
        /// </summary>
        /// <param name="reason">The reason to be displayed to the player.</param>
        public void Kick(string reason)
        {
            //Event
            ClientKickedEventArgs e = new ClientKickedEventArgs(this, reason);
            _player.Server.PluginManager.CallEvent(Event.PLAYER_KICKED, e);
            if (e.EventCanceled) return;
            reason = e.Message;
            //End Event
            Save();
            SendPacket(new DisconnectPacket
            {
                Reason = reason
            });
        }

        public void Disconnected(object sender, SocketAsyncEventArgs e)
        {
            Save();
            // Just wait a bit since it's possible that we close the socket before the packet reached the client
            Thread.Sleep(200);
            Stop();
        }

        /// <summary>
        /// Disposes associated resources and stops the client.  Also removes the client from the server's client/entity lists.
        /// </summary>
        public void Dispose()
        {
            _player.Server.Logger.Log(Chraft.Logger.LogLevel.Info, "Disposing {0}", _player.DisplayName);
            string disconnectMsg = ChatColor.Yellow + _player.DisplayName + " has left the game.";
            //Event
            ClientLeftEventArgs e = new ClientLeftEventArgs(this);
            _player.Server.PluginManager.CallEvent(Plugins.Events.Event.PLAYER_LEFT, e);
            //You cant stop the player from leaving so dont try.
            disconnectMsg = e.BrodcastMessage;
            //End Event
            _player.Server.Broadcast(disconnectMsg);

            if(_player.LoggedIn)
                Save();

            _player.LoggedIn = false;
            _player.Ready = false;

            _player.Server.RemoveClient(this);
            _player.Server.Logger.Log(Chraft.Logger.LogLevel.Info, "Clients online: {0}", _player.Server.Clients.Count);
            _player.Server.RemoveEntity(_player);
            foreach (int packedCoords in _player.LoadedChunks.Keys)
            {
                Chunk chunk = _player.World.GetChunk(UniversalCoords.FromPackedChunk(packedCoords), false, false);
                if (chunk != null)
                    chunk.RemoveClient(this);
            }

            if (_keepAliveTimer != null)
            {
                _keepAliveTimer.Change(Timeout.Infinite, Timeout.Infinite);
                _keepAliveTimer = null;
            }

            RecvBufferPool.ReleaseBuffer(_recvBuffer);
            SendSocketEventPool.Push(_sendSocketEvent);
            RecvSocketEventPool.Push(_recvSocketEvent);

            if (_socket.Connected)
                _socket.Close();

            GC.Collect();
        }

        public void MarkToDispose()
        {
            lock (_disposeLock)
            {
                if (Running)
                {
                    Running = false;
                    StopUpdateChunks();
                }
            }
        }

        public void DisposeSendSystem()
        {
            lock(_disposeLock)
            {
                if (!_sendSystemDisposed)
                {
                    _sendSystemDisposed = true;
                    if (_recvSystemDisposed)
                    {
                        Server.ClientsToDispose.Enqueue(this);
                        _player.Server.NetworkSignal.Set();
                    }
                }
            }
        }

        public void DisposeRecvSystem()
        {
            lock (_disposeLock)
            {
                if (!_recvSystemDisposed)
                {
                    _recvSystemDisposed = true;
                    if (_sendSystemDisposed)
                    {
                        Server.ClientsToDispose.Enqueue(this);
                        _player.Server.NetworkSignal.Set();
                    }
                }
            }
        }

        /// <summary>
        /// Sends a message to the player via chat.
        /// </summary>
        /// <param name="message">The message to be displayed in the chat HUD.</param>
        public void SendMessage(string message)
        {
            SendPacket(new ChatMessagePacket
            {
                Message = message
            });
        }

        private void StartKeepAliveTimer()
        {
            _keepAliveTimer = new Timer(KeepAliveTimer_Callback, null, 10000, 10000);
        }

        /// <summary>
        /// Updates nearby players when Client is hurt.
        /// </summary>
        /// <param name="cause"></param>
        /// <param name="DamageAmount"></param>
        /// <param name="hitBy">The Client hurting the current Client.</param>
        /// <param name="args">First argument should always be the damage amount.</param>
        public void DamageClient(DamageCause cause, double DamageAmount, EntityBase hitBy = null, params object[] args)
        {

            //event start
            EntityDamageEventArgs entevent = new EntityDamageEventArgs(_player, Convert.ToInt16(DamageAmount), null, cause);
            _player.Server.PluginManager.CallEvent(Event.ENTITY_DAMAGE, entevent);
            if (_player.GameMode == 1) { entevent.EventCanceled = true; }
            if (entevent.EventCanceled) return;
            //event end

            switch (cause)
            {
                case DamageCause.BlockExplosion:
                    break;
                case DamageCause.Contact:
                    break;
                case DamageCause.Drowning:
                    break;
                case DamageCause.EntityAttack:
                    if (hitBy != null)
                    {

                    }
                    break;
                case DamageCause.EntityExplosion:
                    break;
                case DamageCause.Fall:
                        _player.Health -= Convert.ToInt16(DamageAmount);
                    break;
                case DamageCause.Fire:
                    break;
                case DamageCause.FireBurn:
                    break;
                case DamageCause.Lava:
                    break;
                case DamageCause.Lightning:
                    break;
                case DamageCause.Projectile:
                    break;
                case DamageCause.Suffocation:
                    break;
                case DamageCause.Void:
                    break;
                default:
                    _player.Health -= 1;
                    break;

            }

            SendPacket(new UpdateHealthPacket
            {
                Health = _player.Health,
                Food = Owner.Food,
                FoodSaturation = Owner.FoodSaturation,
            });

            foreach (Client c in _player.Server.GetNearbyPlayers(_player.World, new AbsWorldCoords(_player.Position.X, _player.Position.Y, _player.Position.Z)))
            {
                if (c == this)
                    continue;

                c.SendPacket(new AnimationPacket // Hurt Animation
                {
                    Animation = 2,
                    PlayerId = _player.EntityId
                });

                c.SendPacket(new EntityStatusPacket // Hurt Action
                {
                    EntityId = _player.EntityId,
                    EntityStatus = 2
                });
            }

            if (_player.Health == 0)
                _player.HandleDeath(hitBy);
        }
    }
}