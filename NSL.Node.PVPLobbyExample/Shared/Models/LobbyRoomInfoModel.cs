using NSL.Node.LobbyServerExample.Shared.Enums;
using NSL.SocketCore.Utils.Buffer;
using System.Collections.Concurrent;

namespace NSL.Node.LobbyServerExample.Shared.Models
{
    public class LobbyRoomInfoModel
    {
        public Guid Id { get; set; }

        public LobbyRoomState State { get; set; }

        private ConcurrentDictionary<Guid, LobbyRoomMemberModel> members = new ConcurrentDictionary<Guid, LobbyRoomMemberModel>();

        public void AddMember(LobbyNetworkClientModel client)
        {
            var member = new LobbyRoomMemberModel()
            {
                Client = client
            };

            members.TryAdd(member.Client.UID, member);

            client.CurrentRoom = this;
        }

        public void LeaveMember(LobbyNetworkClientModel client)
        {
            if (members.TryRemove(client.UID, out var member))
            {
                client.CurrentRoom = default;
            }
        }

        public void StartRoom(IConfiguration configuration)
        {
            State = LobbyRoomState.Processing;

            BroadcastStartRoom(configuration);
        }

        public bool ExistsMember(Guid uid)
        {
            return members.ContainsKey(uid);
        }

        public int MemberCount()
            => members.Count;

        #region Broadcast

        private void BroadcastStartRoom(IConfiguration configuration)
        {
            foreach (var item in members)
            {
                var packet = OutputPacketBuffer.Create(LobbyPacketEnum.StartupRoomInfo);

                packet.WriteGuid(Id);
                packet.WriteString16($"{item.Value.Client.UID}"); // split data with ':' char
                packet.WriteString16(configuration.GetValue<string>("bridge:server:identity"));
                packet.WriteCollection(
                    Enumerable.Repeat(configuration.GetValue<string>("bridge:server:clients_endpoint"), 1),
                    item => packet.WriteString16(item));
                packet.WriteInt32(members.Count);

                item.Value.Client.Network.Send(packet);
            }
        }

        #endregion
    }

    public class LobbyRoomMemberModel
    {
        public LobbyNetworkClientModel Client { get; set; }
    }

    public enum LobbyRoomState
    {
        Lobby,
        Processing,
        Runned
    }
}
