using System;
using LiteNetLib;
using LiteNetLib.Utils;
using Tormia.Ontology.Realtime.Protocol;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Production socket adapter. It transports opaque authenticated datagrams
    /// only and owns no gameplay, ontology, or Transform state.
    /// </summary>
    internal sealed class OntologyLiteNetUdpClientDriver :
        IAuthorityUdpClientDriver
    {
        private EventBasedNetListener listener;
        private NetManager manager;
        private NetPeer peer;

        public bool IsReady => peer != null &&
            peer.ConnectionState == ConnectionState.Connected;
        public event Action Connected;
        public event Action<string> Disconnected;
        public event Action<string> NetworkFailed;
        public event Action<byte[]> DatagramReceived;

        public bool Start(string host, int port, byte[] connectionBootstrap)
        {
            Close();
            if (string.IsNullOrWhiteSpace(host) || port <= 0 ||
                connectionBootstrap == null || connectionBootstrap.Length == 0)
                return false;

            listener = new EventBasedNetListener();
            listener.PeerConnectedEvent += HandleConnected;
            listener.PeerDisconnectedEvent += HandleDisconnected;
            listener.NetworkErrorEvent += HandleNetworkError;
            listener.NetworkReceiveEvent += HandleReceive;
            manager = new NetManager(listener)
            {
                AutoRecycle = true,
                UnsyncedEvents = false,
                UnconnectedMessagesEnabled = false,
                BroadcastReceiveEnabled = false
            };
            if (!manager.Start())
            {
                Close();
                return false;
            }
            var writer = new NetDataWriter();
            writer.Put(connectionBootstrap);
            peer = manager.Connect(host.Trim(), port, writer);
            return peer != null;
        }

        public bool TrySend(byte[] datagram)
        {
            if (!IsReady || datagram == null || datagram.Length == 0)
                return false;
            peer.Send(datagram, 0, DeliveryMethod.Unreliable);
            return true;
        }

        public void Poll() => manager?.PollEvents();

        public void Close()
        {
            peer = null;
            var active = manager;
            manager = null;
            listener = null;
            if (active == null) return;
            try { active.Stop(); }
            catch { }
        }

        private void HandleConnected(NetPeer value)
        {
            peer = value;
            Connected?.Invoke();
        }

        private void HandleDisconnected(NetPeer _, DisconnectInfo info)
        {
            peer = null;
            Disconnected?.Invoke(info.Reason.ToString());
        }

        private void HandleNetworkError(System.Net.IPEndPoint _,
            System.Net.Sockets.SocketError error) =>
            NetworkFailed?.Invoke(error.ToString());

        private void HandleReceive(NetPeer _, NetPacketReader reader,
            byte channel, DeliveryMethod deliveryMethod)
        {
            if (channel == 0 && deliveryMethod == DeliveryMethod.Unreliable &&
                reader != null && reader.AvailableBytes > 0 &&
                reader.AvailableBytes <= RealtimeWireContract.MaximumDatagramLength)
                DatagramReceived?.Invoke(reader.GetRemainingBytes());
        }
    }
}
