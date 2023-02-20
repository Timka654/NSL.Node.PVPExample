using Microsoft.Extensions.Configuration;
using NSL.BuilderExtensions.SocketCore;
using NSL.BuilderExtensions.WebSocketsServer.AspNet;
using NSL.Node.BridgeServer.Shared;
using NSL.Node.LobbyServerExample.Shared.Enums;
using NSL.Node.LobbyServerExample.Shared.Models;
using NSL.SocketCore.Extensions.Buffer;
using NSL.SocketCore.Utils.Buffer;
using NSL.WebSockets.Server;
using System.Collections.Concurrent;
using System.Numerics;

namespace NSL.Node.LobbyServerExample.Managers
{
    public class LobbyManager
    {
        private readonly IConfiguration configuration;

        private ConcurrentDictionary<Guid, LobbyNetworkClientModel> clientMap = new ConcurrentDictionary<Guid, LobbyNetworkClientModel>();

        private ConcurrentDictionary<Guid, LobbyRoomInfoModel> roomMap = new ConcurrentDictionary<Guid, LobbyRoomInfoModel>();

        private ConcurrentDictionary<Guid, LobbyRoomInfoModel> processingRoomMap = new ConcurrentDictionary<Guid, LobbyRoomInfoModel>();

        internal void BuildNetwork(AspNetWebSocketsServerEndPointBuilder<LobbyNetworkClientModel, WSServerOptions<LobbyNetworkClientModel>> builder)
        {
            builder.AddConnectHandle(OnClientConnectedHandle);

            builder.AddDisconnectHandle(OnClientDisconnectedHandle);

            builder.AddPacketHandle(LobbyPacketEnum.FindOpponent, FindOpponentRequestHandle);
        }

        public LobbyManager(IConfiguration configuration)
        {
            this.configuration = configuration;
        }

        #region NetworkHandle

        private void OnClientConnectedHandle(LobbyNetworkClientModel client)
        {
            do
            {
                client.UID = Guid.NewGuid();
            } while (!clientMap.TryAdd(client.UID, client));
        }

        private void OnClientDisconnectedHandle(LobbyNetworkClientModel client)
        {
            var uid = client?.UID;

            if (uid != default)
            {
                clientMap.Remove(uid.Value, out _);

                LeaveRoomMember(client);
            }
        }

        #endregion

        #region Bridge

        internal Task<bool> BridgeValidateSessionAsync(Guid roomId, string sessionIdentity)
        {
            var splited = sessionIdentity.Split(':');

            var uid = Guid.Parse(splited.First());

            if (processingRoomMap.TryGetValue(roomId, out var room))
            {
                if (room.ExistsMember(uid))
                    return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }

        internal Task<bool> BridgRoomStartupInfoAsync(Guid roomId, NodeRoomStartupInfo startupInfo)
        {
            if (processingRoomMap.TryGetValue(roomId, out var room))
            {
                startupInfo.SetRoomWaitReady(true);
                startupInfo.SetRoomPlayerCount(room.MemberCount());

                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }

        internal Task BridgFinishRoomAsync(Guid roomId, byte[] data)
        {
            return Task.CompletedTask;
        }

        #endregion

        #region PacketHandle

        private void FindOpponentRequestHandle(LobbyNetworkClientModel client, InputPacketBuffer data)
        {
            if (client.CurrentRoom == null)
                SearchOpponent(client);
        }


        #endregion

        AutoResetEvent startMatchmakeLocker = new AutoResetEvent(true);

        private void SearchOpponent(LobbyNetworkClientModel client)
        {
            startMatchmakeLocker.WaitOne(1000);

            try
            {
                var roomPair = roomMap.FirstOrDefault();

                LobbyRoomInfoModel room;

                if (roomPair.Value != default)
                    room = roomPair.Value;
                else
                {
                    room = new LobbyRoomInfoModel();

                    do
                    {
                        room.Id = Guid.NewGuid();
                    } while (!roomMap.TryAdd(room.Id, room));
                    roomMap.TryAdd(room.Id, room);
                }

                room.AddMember(client);

                if (room.MemberCount() == 2)
                {
                    roomMap.Remove(room.Id, out _);

                    processingRoomMap.TryAdd(room.Id, room);

                    room.StartRoom(configuration);
                }
            }
            catch (Exception)
            {
                throw;
            }
            finally
            {
                startMatchmakeLocker.Set();
            }
        }

        private void LeaveRoomMember(LobbyNetworkClientModel client)
        {
            var room = client.CurrentRoom;

            if (room != default && room.State == LobbyRoomState.Lobby)
            {
                room.LeaveMember(client);
            }
        }
    }
}
