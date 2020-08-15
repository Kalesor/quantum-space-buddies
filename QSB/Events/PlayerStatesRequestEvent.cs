﻿using QSB.Messaging;
using QSB.TransformSync;
using System.Linq;

namespace QSB.Events
{
    public class PlayerStatesRequestEvent : QSBEvent<PlayerMessage>
    {
        public override MessageType Type => MessageType.FullStateRequest;

        public override void SetupListener()
        {
            GlobalMessenger.AddListener(EventNames.QSBPlayerStatesRequest, () => SendEvent(CreateMessage()));
        }

        private PlayerMessage CreateMessage() => new PlayerMessage
        {
            SenderId = PlayerTransformSync.LocalInstance.netId.Value
        };

        public override void OnServerReceive(PlayerMessage message)
        {
            PlayerState.LocalInstance.Send();
            foreach (var item in PlayerRegistry.TransformSyncs.Where(x => x.IsReady && x.ReferenceSector != null))
            {
                GlobalMessenger<uint, Sector.Name, string>.FireEvent(EventNames.QSBSectorChange, item.netId.Value, item.ReferenceSector.GetName(), item.ReferenceSector.name);
            }
        }
    }
}
