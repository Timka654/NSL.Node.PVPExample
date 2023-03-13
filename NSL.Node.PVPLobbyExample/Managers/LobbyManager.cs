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

        private int MembersForRun => configuration.GetValue<int>("members_for_run", 2);

        private ConcurrentDictionary<Guid, LobbyNetworkClientModel> clientMap = new ConcurrentDictionary<Guid, LobbyNetworkClientModel>();

        private ConcurrentDictionary<Guid, LobbyRoomInfoModel> processingRoomMap = new ConcurrentDictionary<Guid, LobbyRoomInfoModel>();

        internal void BuildNetwork(AspNetWebSocketsServerEndPointBuilder<LobbyNetworkClientModel, WSServerOptions<LobbyNetworkClientModel>> builder)
        {
            builder.AddConnectHandle(OnClientConnectedHandle);

            builder.AddDisconnectHandle(OnClientDisconnectedHandle);

            builder.AddPacketHandle(LobbyPacketEnum.FindOpponent, FindOpponentRequestHandle);
            builder.AddPacketHandle(LobbyPacketEnum.CancelSearch, CancelSearchRequestHandle);
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
            if (client == null)
                return;

            var uid = client?.UID;

            if (uid != default)
            {
                clientMap.Remove(uid.Value, out _);
                CancelSearch(client);
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

        internal Task<bool> BridgeRoomStartupInfoAsync(Guid roomId, NodeRoomStartupInfo startupInfo)
        {
            if (processingRoomMap.TryGetValue(roomId, out var room))
            {
                startupInfo.SetRoomWaitReady(true);
                startupInfo.SetRoomNodeCount(room.MemberCount());

                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }

        internal Task BridgeFinishRoomAsync(Guid roomId, byte[] data)
        {
            return Task.CompletedTask;
        }

        #endregion

        #region PacketHandle

        private void FindOpponentRequestHandle(LobbyNetworkClientModel client, InputPacketBuffer data)
        {
            CancelSearch(client);

            client.SearchToken = new CancellationTokenSource();

            if (client.CurrentRoom == null)
                SearchOpponent(client);
        }

        private void CancelSearchRequestHandle(LobbyNetworkClientModel client, InputPacketBuffer data)
        {
            CancelSearch(client);
        }

        #endregion

        AutoResetEvent startMatchmakeLocker = new AutoResetEvent(true);

        private void CancelSearch(LobbyNetworkClientModel client)
        {
            if (client.SearchToken != default)
                client.SearchToken.Cancel();

            client.SearchToken = null;
        }

        private LobbyRoomInfoModel room;

        private void SearchOpponent(LobbyNetworkClientModel client)
        {
            try
            {
                startMatchmakeLocker.WaitOne(1000);

                client.SearchToken.Token.ThrowIfCancellationRequested();

                if(room == null)
                    room = new LobbyRoomInfoModel();

                client.SearchToken.Token.ThrowIfCancellationRequested();

                room.AddMember(client);

                client.SearchToken.Token.ThrowIfCancellationRequested();

                if (room.MemberCount() == MembersForRun)
                {
                    do
                    {
                        room.Id = Guid.NewGuid();
                    } while (!processingRoomMap.TryAdd(room.Id, room));

                    client.SearchToken.Token.ThrowIfCancellationRequested();

                    room.StartRoom(configuration);
                }
            }
            catch (TaskCanceledException)
            {
                if (room.Id != Guid.Empty)
                    processingRoomMap.Remove(room.Id, out _);
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
