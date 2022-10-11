﻿using QSB.EchoesOfTheEye.VisionTorch.WorldObjects;
using QSB.Messaging;

namespace QSB.EchoesOfTheEye.VisionTorch.Messages;

public class VisionTorchProjectMessage : QSBWorldObjectMessage<QSBVisionTorchItem, bool>
{
	public VisionTorchProjectMessage(bool projecting) : base(projecting) { }
	public override void OnReceiveRemote() => WorldObject.AttachedObject._mindProjectorTrigger.SetProjectorActive(Data);
}
