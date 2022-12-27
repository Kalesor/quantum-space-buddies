﻿using Cysharp.Threading.Tasks;
using QSB.EchoesOfTheEye.VisionTorch.WorldObjects;
using QSB.WorldSync;
using System.Threading;

namespace QSB.EchoesOfTheEye.VisionTorch;

public class VisionTorchManager : WorldObjectManager
{
	public override WorldObjectScene WorldObjectScene => WorldObjectScene.SolarSystem;
	public override bool DlcOnly => true;

	public override async UniTask BuildWorldObjects(OWScene scene, CancellationToken ct) =>
		QSBWorldSync.Init<QSBVisionTorchItem, VisionTorchItem>();
}
