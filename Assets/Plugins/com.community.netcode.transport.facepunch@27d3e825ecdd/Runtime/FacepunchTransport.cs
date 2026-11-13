using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Steamworks;
using Steamworks.Data;
using Unity.Netcode;
using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;

namespace Netcode.Transports.Facepunch
{
    using SocketConnection = Connection;

    public class FacepunchTransport : NetworkTransport, IConnectionManager, ISocketManager
    {
        private ConnectionManager connectionManager;
        private SocketManager socketManager;
        private Dictionary<ulong, Client> connectedClients;

        [Space]
        [Tooltip("The Steam App ID of your game. Technically you're not allowed to use 480, but Valve doesn't do anything about it so it's fine for testing purposes.")]
        [SerializeField] private uint steamAppId = 480;

        /// <summary>
        /// Project-wide App ID override (set from GameSettings' build channel before scenes load).
        /// When non-zero it replaces the serialized <see cref="steamAppId"/> on every transport instance.
        /// </summary>
        public static uint AppIdOverride;

        /// <summary>The effective Steam App ID (single source of truth for SteamClient.Init).</summary>
        public uint SteamAppId => AppIdOverride != 0 ? AppIdOverride : steamAppId;

        [Tooltip("The Steam ID of the user targeted when joining as a client.")]
        [SerializeField] public ulong targetSteamId;

        [Header("Info")]
        [ReadOnly]
        [Tooltip("When in play mode, this will display your Steam ID.")]
        [SerializeField] private ulong userSteamId;

        private LogLevel LogLevel => NetworkManager.Singleton.LogLevel;

        private class Client
        {
            public SteamId steamId;
            public SocketConnection connection;
        }

        // ── Large-message framing ────────────────────────────────────────────────────────────
        // Steam rejects any single message over 512 KB (k_cbMaxSteamNetworkingSocketsMessageSizeSend)
        // and any send that would overflow the connection's send buffer, returning LimitExceeded.
        // NGO hands large payloads (e.g. a late joiner's full scene synchronization on Day 2+) to the
        // transport as one ReliableFragmentedSequenced message and expects the transport to fragment
        // it, as UnityTransport does. Without this, those sends were silently dropped and the joining
        // client never finished synchronizing.
        //
        // Every message now carries a 1-byte frame header. Reliable payloads larger than
        // MaxChunkPayload are split into ChunkStart (+ total length) / ChunkData frames, queued per
        // connection, and pumped every frame until Steam accepts them. Steam delivers reliable
        // messages in order on a connection, so the receiver reassembles chunks sequentially.
        // Both peers must run a build with this framing.

        private enum FrameType : byte
        {
            Single = 0,
            ChunkStart = 1,
            ChunkData = 2
        }

        private const int MaxChunkPayload = 128 * 1024;
        private const int ChunkStartHeaderSize = 1 + sizeof(int);
        private const int SteamSendBufferBytes = 8 * 1024 * 1024;
        private const int MaxReassemblyBytes = 64 * 1024 * 1024;

        private class Reassembly
        {
            public byte[] buffer;
            public int received;
            public int total;
        }

        private readonly Dictionary<ulong, Queue<byte[]>> outgoingQueues = new Dictionary<ulong, Queue<byte[]>>();
        private readonly Dictionary<ulong, Reassembly> reassemblies = new Dictionary<ulong, Reassembly>();
        private byte[] sendScratch = new byte[4096];

        /// <summary>CLIENT: true once the Steam connection to the host is established.</summary>
        public bool IsClientTransportConnected { get; private set; }

        /// <summary>CLIENT: total payload bytes received from the host since the last StartClient.</summary>
        public long ClientBytesReceived { get; private set; }

        /// <summary>
        /// CLIENT: progress of the chunked message currently arriving from the host, if any.
        /// Returns false when no large transfer is in flight.
        /// </summary>
        public bool TryGetIncomingTransferProgress(out int receivedBytes, out int totalBytes)
        {
            if (reassemblies.TryGetValue(ServerClientId, out Reassembly r) && r.buffer != null)
            {
                receivedBytes = r.received;
                totalBytes = r.total;
                return true;
            }

            receivedBytes = 0;
            totalBytes = 0;
            return false;
        }

        /// <summary>Bytes still waiting in this peer's outgoing queue for a connection (0 when drained).</summary>
        public long GetPendingSendBytes(ulong clientId)
        {
            if (!outgoingQueues.TryGetValue(clientId, out Queue<byte[]> queue))
                return 0;

            long total = 0;
            foreach (byte[] frame in queue)
                total += frame.Length;
            return total;
        }

        #region MonoBehaviour Messages

        private void Awake()
        {
            try
            {
                if (!SteamClient.IsValid)
                    SteamClient.Init(SteamAppId, false);
            }
            catch (Exception e)
            {
                if (LogLevel <= LogLevel.Error)
                    Debug.LogError($"[{nameof(FacepunchTransport)}] - Caught an exeption during initialization of Steam client: {e}");
            }
            finally
            {
                StartCoroutine(InitSteamworks());
            }
        }

        private void Update()
        {
            SteamClient.RunCallbacks();
            PumpAllOutgoingQueues();
        }

        private void OnDestroy()
        {
            SteamClient.Shutdown();
        }

        #endregion

        #region NetworkTransport Overrides

        public override ulong ServerClientId => 0;

        public override void DisconnectLocalClient()
        {
            connectionManager?.Connection.Close();

            if (LogLevel <= LogLevel.Developer)
                Debug.Log($"[{nameof(FacepunchTransport)}] - Disconnecting local client.");
        }

        public override void DisconnectRemoteClient(ulong clientId)
        {
            if (connectedClients.TryGetValue(clientId, out Client user))
            {
                // Flush any pending messages before closing the connection
                user.connection.Flush();
                user.connection.Close();
                connectedClients.Remove(clientId);
                ClearPeerState(clientId);

                if (LogLevel <= LogLevel.Developer)
                    Debug.Log($"[{nameof(FacepunchTransport)}] - Disconnecting remote client with ID {clientId}.");
            }
            else if (LogLevel <= LogLevel.Normal)
                Debug.LogWarning($"[{nameof(FacepunchTransport)}] - Failed to disconnect remote client with ID {clientId}, client not connected.");
        }

        public override unsafe ulong GetCurrentRtt(ulong clientId)
        {
            return 0;
        }

        public override void Initialize(NetworkManager networkManager = null)
        {
            connectedClients = new Dictionary<ulong, Client>();
        }

        private SendType NetworkDeliveryToSendType(NetworkDelivery delivery)
        {
            return delivery switch
            {
                NetworkDelivery.Reliable => SendType.Reliable,
                NetworkDelivery.ReliableFragmentedSequenced => SendType.Reliable,
                NetworkDelivery.ReliableSequenced => SendType.Reliable,
                NetworkDelivery.Unreliable => SendType.Unreliable,
                NetworkDelivery.UnreliableSequenced => SendType.Unreliable,
                _ => SendType.Reliable
            };
        }

        public override void Shutdown()
        {
            try
            {
                if (LogLevel <= LogLevel.Developer)
                    Debug.Log($"[{nameof(FacepunchTransport)}] - Shutting down.");

                connectionManager?.Close();
                socketManager?.Close();
            }
            catch (Exception e)
            {
                if (LogLevel <= LogLevel.Error)
                    Debug.LogError($"[{nameof(FacepunchTransport)}] - Caught an exception while shutting down: {e}");
            }
            finally
            {
                outgoingQueues.Clear();
                reassemblies.Clear();
                IsClientTransportConnected = false;
            }
        }

        public override void Send(ulong clientId, ArraySegment<byte> data, NetworkDelivery delivery)
        {
            if (!TryGetConnection(clientId, out SocketConnection connection))
            {
                if (LogLevel <= LogLevel.Normal)
                    Debug.LogWarning($"[{nameof(FacepunchTransport)}] - Failed to send packet to remote client with ID {clientId}, client not connected.");
                return;
            }

            SendType sendType = NetworkDeliveryToSendType(delivery);

            // Unreliable traffic is always MTU-sized and must never wait behind a reliable backlog.
            if (sendType != SendType.Reliable)
            {
                int length = WriteSingleFrame(data);
                connection.SendMessage(sendScratch, 0, length, sendType);
                return;
            }

            Queue<byte[]> queue = GetOrCreateQueue(clientId);

            if (data.Count <= MaxChunkPayload)
            {
                // Fast path: nothing queued ahead of it, so ordering is preserved by sending now.
                if (queue.Count == 0)
                {
                    int length = WriteSingleFrame(data);
                    Result result = connection.SendMessage(sendScratch, 0, length, SendType.Reliable);
                    if (result == Result.OK)
                        return;

                    if (result != Result.LimitExceeded)
                    {
                        if (LogLevel <= LogLevel.Error)
                            Debug.LogError($"[{nameof(FacepunchTransport)}] - Reliable send of {length} bytes to client {clientId} failed: {result}.");
                        return;
                    }
                }

                byte[] frame = new byte[data.Count + 1];
                frame[0] = (byte)FrameType.Single;
                Buffer.BlockCopy(data.Array, data.Offset, frame, 1, data.Count);
                queue.Enqueue(frame);
            }
            else
            {
                EnqueueChunks(queue, data);

                if (LogLevel <= LogLevel.Normal)
                {
                    int chunkCount = (data.Count + MaxChunkPayload - 1) / MaxChunkPayload;
                    Debug.Log($"[{nameof(FacepunchTransport)}] - Sending {data.Count / 1024f:0.0} KB message to client {clientId} in {chunkCount} chunks.");
                }
            }

            PumpOutgoingQueue(clientId, connection, queue);
        }

        private bool TryGetConnection(ulong clientId, out SocketConnection connection)
        {
            if (clientId == ServerClientId && connectionManager != null)
            {
                connection = connectionManager.Connection;
                return true;
            }

            if (connectedClients != null && connectedClients.TryGetValue(clientId, out Client user))
            {
                connection = user.connection;
                return true;
            }

            connection = default;
            return false;
        }

        private Queue<byte[]> GetOrCreateQueue(ulong clientId)
        {
            if (!outgoingQueues.TryGetValue(clientId, out Queue<byte[]> queue))
            {
                queue = new Queue<byte[]>();
                outgoingQueues[clientId] = queue;
            }

            return queue;
        }

        /// <summary>Writes a Single frame for <paramref name="data"/> into <see cref="sendScratch"/>; returns its length.</summary>
        private int WriteSingleFrame(ArraySegment<byte> data)
        {
            int length = data.Count + 1;
            if (sendScratch.Length < length)
                sendScratch = new byte[Math.Max(sendScratch.Length * 2, length)];

            sendScratch[0] = (byte)FrameType.Single;
            Buffer.BlockCopy(data.Array, data.Offset, sendScratch, 1, data.Count);
            return length;
        }

        private static void EnqueueChunks(Queue<byte[]> queue, ArraySegment<byte> data)
        {
            int total = data.Count;
            int offset = 0;
            bool first = true;

            while (offset < total)
            {
                int payload = Math.Min(MaxChunkPayload, total - offset);
                int headerSize = first ? ChunkStartHeaderSize : 1;
                byte[] frame = new byte[headerSize + payload];

                if (first)
                {
                    frame[0] = (byte)FrameType.ChunkStart;
                    frame[1] = (byte)total;
                    frame[2] = (byte)(total >> 8);
                    frame[3] = (byte)(total >> 16);
                    frame[4] = (byte)(total >> 24);
                }
                else
                {
                    frame[0] = (byte)FrameType.ChunkData;
                }

                Buffer.BlockCopy(data.Array, data.Offset + offset, frame, headerSize, payload);
                queue.Enqueue(frame);

                offset += payload;
                first = false;
            }
        }

        private void PumpAllOutgoingQueues()
        {
            if (outgoingQueues.Count == 0)
                return;

            foreach (KeyValuePair<ulong, Queue<byte[]>> pair in outgoingQueues)
            {
                if (pair.Value.Count == 0)
                    continue;

                if (TryGetConnection(pair.Key, out SocketConnection connection))
                    PumpOutgoingQueue(pair.Key, connection, pair.Value);
                else
                    pair.Value.Clear();
            }
        }

        /// <summary>
        /// Sends queued reliable frames in order until Steam's send buffer is full (LimitExceeded),
        /// in which case the rest wait for the next frame.
        /// </summary>
        private void PumpOutgoingQueue(ulong clientId, SocketConnection connection, Queue<byte[]> queue)
        {
            while (queue.Count > 0)
            {
                byte[] frame = queue.Peek();
                Result result = connection.SendMessage(frame, 0, frame.Length, SendType.Reliable);

                if (result == Result.OK)
                {
                    queue.Dequeue();
                    continue;
                }

                if (result == Result.LimitExceeded)
                    return;

                if (LogLevel <= LogLevel.Error)
                    Debug.LogError($"[{nameof(FacepunchTransport)}] - Reliable send to client {clientId} failed: {result}. Dropping {queue.Count} queued frame(s).");
                queue.Clear();
                return;
            }
        }

        private void ClearPeerState(ulong clientId)
        {
            outgoingQueues.Remove(clientId);
            reassemblies.Remove(clientId);
        }

        /// <summary>Decodes one framed Steam message and forwards complete payloads to Netcode.</summary>
        private unsafe void HandleIncomingFrame(ulong senderId, IntPtr data, int size)
        {
            if (size < 1)
                return;

            byte* src = (byte*)data;
            FrameType type = (FrameType)src[0];

            if (senderId == ServerClientId && connectionManager != null)
                ClientBytesReceived += size;

            switch (type)
            {
                case FrameType.Single:
                {
                    int payloadSize = size - 1;
                    EnsurePayloadCapacity(payloadSize);
                    fixed (byte* payload = payloadCache)
                    {
                        UnsafeUtility.MemCpy(payload, src + 1, payloadSize);
                    }

                    InvokeOnTransportEvent(NetworkEvent.Data, senderId, new ArraySegment<byte>(payloadCache, 0, payloadSize), Time.realtimeSinceStartup);
                    return;
                }

                case FrameType.ChunkStart:
                {
                    if (size < ChunkStartHeaderSize)
                        break;

                    int total = src[1] | (src[2] << 8) | (src[3] << 16) | (src[4] << 24);
                    if (total <= 0 || total > MaxReassemblyBytes)
                    {
                        if (LogLevel <= LogLevel.Error)
                            Debug.LogError($"[{nameof(FacepunchTransport)}] - Rejected chunked message of {total} bytes from client {senderId}.");
                        reassemblies.Remove(senderId);
                        return;
                    }

                    if (!reassemblies.TryGetValue(senderId, out Reassembly r))
                    {
                        r = new Reassembly();
                        reassemblies[senderId] = r;
                    }
                    else if (r.buffer != null && LogLevel <= LogLevel.Normal)
                    {
                        Debug.LogWarning($"[{nameof(FacepunchTransport)}] - New chunked message from client {senderId} started before the previous one completed ({r.received}/{r.total}); discarding it.");
                    }

                    r.buffer = new byte[total];
                    r.total = total;
                    r.received = 0;
                    AppendChunk(senderId, r, src + ChunkStartHeaderSize, size - ChunkStartHeaderSize);
                    return;
                }

                case FrameType.ChunkData:
                {
                    if (!reassemblies.TryGetValue(senderId, out Reassembly r) || r.buffer == null)
                    {
                        if (LogLevel <= LogLevel.Error)
                            Debug.LogError($"[{nameof(FacepunchTransport)}] - Received a message chunk from client {senderId} with no message in progress.");
                        return;
                    }

                    AppendChunk(senderId, r, src + 1, size - 1);
                    return;
                }
            }

            if (LogLevel <= LogLevel.Error)
                Debug.LogError($"[{nameof(FacepunchTransport)}] - Received malformed frame (type {(byte)type}, {size} bytes) from client {senderId}. Is the other player on a different build?");
        }

        private unsafe void AppendChunk(ulong senderId, Reassembly r, byte* chunk, int length)
        {
            if (length < 0 || r.received + length > r.total)
            {
                if (LogLevel <= LogLevel.Error)
                    Debug.LogError($"[{nameof(FacepunchTransport)}] - Chunk overflow from client {senderId} ({r.received}+{length} > {r.total}); discarding message.");
                r.buffer = null;
                return;
            }

            fixed (byte* dst = r.buffer)
            {
                UnsafeUtility.MemCpy(dst + r.received, chunk, length);
            }

            r.received += length;
            if (r.received < r.total)
                return;

            byte[] complete = r.buffer;
            int total = r.total;
            r.buffer = null;

            InvokeOnTransportEvent(NetworkEvent.Data, senderId, new ArraySegment<byte>(complete, 0, total), Time.realtimeSinceStartup);
        }

        public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
        {
            connectionManager?.Receive();
            socketManager?.Receive();

            clientId = 0;
            receiveTime = Time.realtimeSinceStartup;
            payload = default;
            return NetworkEvent.Nothing;
        }

        public override bool StartClient()
        {
            if (LogLevel <= LogLevel.Developer)
                Debug.Log($"[{nameof(FacepunchTransport)}] - Starting as client.");

            outgoingQueues.Clear();
            reassemblies.Clear();
            IsClientTransportConnected = false;
            ClientBytesReceived = 0;

            connectionManager = SteamNetworkingSockets.ConnectRelay<ConnectionManager>(targetSteamId);
            connectionManager.Interface = this;
            return true;
        }

        public override bool StartServer()
        {
            if (LogLevel <= LogLevel.Developer)
                Debug.Log($"[{nameof(FacepunchTransport)}] - Starting as server.");

            socketManager = SteamNetworkingSockets.CreateRelaySocket<SocketManager>();
            socketManager.Interface = this;
            return true;
        }

        #endregion

        #region ConnectionManager Implementation

        private byte[] payloadCache = new byte[4096];

        private void EnsurePayloadCapacity(int size)
        {
            if (payloadCache.Length >= size)
                return;

            payloadCache = new byte[Math.Max(payloadCache.Length * 2, size)];
        }

        void IConnectionManager.OnConnecting(ConnectionInfo info)
        {
            if (LogLevel <= LogLevel.Developer)
                Debug.Log($"[{nameof(FacepunchTransport)}] - Connecting with Steam user {info.Identity.SteamId}.");
        }

        void IConnectionManager.OnConnected(ConnectionInfo info)
        {
            IsClientTransportConnected = true;
            InvokeOnTransportEvent(NetworkEvent.Connect, ServerClientId, default, Time.realtimeSinceStartup);

            if (LogLevel <= LogLevel.Developer)
                Debug.Log($"[{nameof(FacepunchTransport)}] - Connected with Steam user {info.Identity.SteamId}.");
        }

        void IConnectionManager.OnDisconnected(ConnectionInfo info)
        {
            IsClientTransportConnected = false;
            ClearPeerState(ServerClientId);
            InvokeOnTransportEvent(NetworkEvent.Disconnect, ServerClientId, default, Time.realtimeSinceStartup);

            if (LogLevel <= LogLevel.Developer)
                Debug.Log($"[{nameof(FacepunchTransport)}] - Disconnected Steam user {info.Identity.SteamId}.");
        }

        void IConnectionManager.OnMessage(IntPtr data, int size, long messageNum, long recvTime, int channel)
        {
            HandleIncomingFrame(ServerClientId, data, size);
        }

        #endregion

        #region SocketManager Implementation

        void ISocketManager.OnConnecting(SocketConnection connection, ConnectionInfo info)
        {
            if (LogLevel <= LogLevel.Developer)
                Debug.Log($"[{nameof(FacepunchTransport)}] - Accepting connection from Steam user {info.Identity.SteamId}.");

            connection.Accept();
        }

        void ISocketManager.OnConnected(SocketConnection connection, ConnectionInfo info)
        {
            if (!connectedClients.ContainsKey(connection.Id))
            {
                connectedClients.Add(connection.Id, new Client()
                {
                    connection = connection,
                    steamId = info.Identity.SteamId
                });

                InvokeOnTransportEvent(NetworkEvent.Connect, connection.Id, default, Time.realtimeSinceStartup);

                if (LogLevel <= LogLevel.Developer)
                    Debug.Log($"[{nameof(FacepunchTransport)}] - Connected with Steam user {info.Identity.SteamId}.");
            }
            else if (LogLevel <= LogLevel.Normal)
                Debug.LogWarning($"[{nameof(FacepunchTransport)}] - Failed to connect client with ID {connection.Id}, client already connected.");
        }

        void ISocketManager.OnDisconnected(SocketConnection connection, ConnectionInfo info)
        {
            connectedClients.Remove(connection.Id);
            ClearPeerState(connection.Id);

            InvokeOnTransportEvent(NetworkEvent.Disconnect, connection.Id, default, Time.realtimeSinceStartup);

            if (LogLevel <= LogLevel.Developer)
                Debug.Log($"[{nameof(FacepunchTransport)}] - Disconnected Steam user {info.Identity.SteamId}");
        }

        void ISocketManager.OnMessage(SocketConnection connection, NetIdentity identity, IntPtr data, int size, long messageNum, long recvTime, int channel)
        {
            HandleIncomingFrame(connection.Id, data, size);
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// SERVER: returns the Steam-authenticated SteamID of a connected remote client, keyed by
        /// its transport client ID (Steam networking sockets verify this identity — unlike any
        /// SteamID a client writes into its own connection payload).
        /// </summary>
        public bool TryGetSteamIdForTransportClient(ulong transportClientId, out ulong steamId)
        {
            steamId = 0;
            if (connectedClients == null || !connectedClients.TryGetValue(transportClientId, out Client client))
                return false;

            steamId = client.steamId.Value;
            return steamId != 0;
        }

        private IEnumerator InitSteamworks()
        {
            yield return new WaitUntil(() => SteamClient.IsValid);

            SteamNetworkingUtils.InitRelayNetworkAccess();

            // Steam's default 512 KB send buffer makes a multi-MB late-join sync trickle through the
            // outgoing queue; a larger buffer lets Steam pace it at the full connection rate.
            try
            {
                SteamNetworkingUtils.SendBufferSize = SteamSendBufferBytes;
            }
            catch (Exception e)
            {
                if (LogLevel <= LogLevel.Normal)
                    Debug.LogWarning($"[{nameof(FacepunchTransport)}] - Could not raise Steam send buffer size: {e.Message}");
            }

            if (LogLevel <= LogLevel.Developer)
                Debug.Log($"[{nameof(FacepunchTransport)}] - Initialized access to Steam Relay Network.");

            userSteamId = SteamClient.SteamId;

            if (LogLevel <= LogLevel.Developer)
                Debug.Log($"[{nameof(FacepunchTransport)}] - Fetched user Steam ID.");
        }

        #endregion
    }
}
