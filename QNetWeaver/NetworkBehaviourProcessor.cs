﻿using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace QNetWeaver
{
	internal class NetworkBehaviourProcessor
	{
		public NetworkBehaviourProcessor(TypeDefinition td)
		{
			Weaver.DLog(td, "NetworkBehaviourProcessor", new object[0]);
			this.m_td = td;
		}

		public void Process()
		{
			if (this.m_td.HasGenericParameters)
			{
				Weaver.fail = true;
				Log.Error("NetworkBehaviour " + this.m_td.Name + " cannot have generic parameters");
			}
			else
			{
				Weaver.DLog(this.m_td, "Process Start", new object[0]);
				this.ProcessVersion();
				this.ProcessSyncVars();
				Weaver.ResetRecursionCount();
				this.ProcessMethods();
				this.ProcessEvents();
				if (!Weaver.fail)
				{
					this.GenerateNetworkSettings();
					this.GenerateConstants();
					Weaver.ResetRecursionCount();
					this.GenerateSerialization();
					if (!Weaver.fail)
					{
						this.GenerateDeSerialization();
						this.GeneratePreStartClient();
						Weaver.DLog(this.m_td, "Process Done", new object[0]);
					}
				}
			}
		}

		private static void WriteClientActiveCheck(ILProcessor worker, string mdName, Instruction label, string errString)
		{
			worker.Append(worker.Create(OpCodes.Call, Weaver.NetworkClientGetActive));
			worker.Append(worker.Create(OpCodes.Brtrue, label));
			worker.Append(worker.Create(OpCodes.Ldstr, errString + " " + mdName + " called on server."));
			worker.Append(worker.Create(OpCodes.Call, Weaver.logErrorReference));
			worker.Append(worker.Create(OpCodes.Ret));
			worker.Append(label);
		}

		private static void WriteServerActiveCheck(ILProcessor worker, string mdName, Instruction label, string errString)
		{
			worker.Append(worker.Create(OpCodes.Call, Weaver.NetworkServerGetActive));
			worker.Append(worker.Create(OpCodes.Brtrue, label));
			worker.Append(worker.Create(OpCodes.Ldstr, errString + " " + mdName + " called on client."));
			worker.Append(worker.Create(OpCodes.Call, Weaver.logErrorReference));
			worker.Append(worker.Create(OpCodes.Ret));
			worker.Append(label);
		}

		private static void WriteSetupLocals(ILProcessor worker)
		{
			worker.Body.InitLocals = true;
			worker.Body.Variables.Add(new VariableDefinition(Weaver.scriptDef.MainModule.ImportReference(Weaver.NetworkWriterType)));
		}

		private static void WriteCreateWriter(ILProcessor worker)
		{
			worker.Append(worker.Create(OpCodes.Newobj, Weaver.NetworkWriterCtor));
			worker.Append(worker.Create(OpCodes.Stloc_0));
			worker.Append(worker.Create(OpCodes.Ldloc_0));
		}

		private static void WriteMessageSize(ILProcessor worker)
		{
			worker.Append(worker.Create(OpCodes.Ldc_I4_0));
			worker.Append(worker.Create(OpCodes.Callvirt, Weaver.NetworkWriterWriteInt16));
		}

		private static void WriteMessageId(ILProcessor worker, int msgId)
		{
			worker.Append(worker.Create(OpCodes.Ldloc_0));
			worker.Append(worker.Create(OpCodes.Ldc_I4, msgId));
			worker.Append(worker.Create(OpCodes.Conv_U2));
			worker.Append(worker.Create(OpCodes.Callvirt, Weaver.NetworkWriterWriteInt16));
		}

		private static bool WriteArguments(ILProcessor worker, MethodDefinition md, string errString, bool skipFirst)
		{
			short num = 1;
			foreach (var parameterDefinition in md.Parameters)
			{
				if (num == 1 && skipFirst)
				{
					num += 1;
				}
				else
				{
					var writeFunc = Weaver.GetWriteFunc(parameterDefinition.ParameterType);
					if (writeFunc == null)
					{
						Log.Error(string.Concat(new object[]
						{
							"WriteArguments for ",
							md.Name,
							" type ",
							parameterDefinition.ParameterType,
							" not supported"
						}));
						Weaver.fail = true;
						return false;
					}
					worker.Append(worker.Create(OpCodes.Ldloc_0));
					worker.Append(worker.Create(OpCodes.Ldarg, (int)num));
					worker.Append(worker.Create(OpCodes.Call, writeFunc));
					num += 1;
				}
			}
			return true;
		}

		private void ProcessVersion()
		{
			foreach (var methodDefinition in this.m_td.Methods)
			{
				if (methodDefinition.Name == "UNetVersion")
				{
					return;
				}
			}
			var methodDefinition2 = new MethodDefinition("UNetVersion", MethodAttributes.Private, Weaver.voidType);
			var ilprocessor = methodDefinition2.Body.GetILProcessor();
			ilprocessor.Append(ilprocessor.Create(OpCodes.Ret));
			this.m_td.Methods.Add(methodDefinition2);
		}

		private void GenerateConstants()
		{
			if (this.m_Cmds.Count != 0 || this.m_Rpcs.Count != 0 || this.m_TargetRpcs.Count != 0 || this.m_Events.Count != 0 || this.m_SyncLists.Count != 0)
			{
				Weaver.DLog(this.m_td, "  GenerateConstants ", new object[0]);
				MethodDefinition methodDefinition = null;
				var flag = false;
				foreach (var methodDefinition2 in this.m_td.Methods)
				{
					if (methodDefinition2.Name == ".cctor")
					{
						methodDefinition = methodDefinition2;
						flag = true;
					}
				}
				if (methodDefinition != null)
				{
					if (methodDefinition.Body.Instructions.Count != 0)
					{
						var instruction = methodDefinition.Body.Instructions[methodDefinition.Body.Instructions.Count - 1];
						if (!(instruction.OpCode == OpCodes.Ret))
						{
							Log.Error("No cctor for " + this.m_td.Name);
							Weaver.fail = true;
							return;
						}
						methodDefinition.Body.Instructions.RemoveAt(methodDefinition.Body.Instructions.Count - 1);
					}
				}
				else
				{
					methodDefinition = new MethodDefinition(".cctor", MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, Weaver.voidType);
				}
				MethodDefinition methodDefinition3 = null;
				foreach (var methodDefinition4 in this.m_td.Methods)
				{
					if (methodDefinition4.Name == ".ctor")
					{
						methodDefinition3 = methodDefinition4;
						var instruction2 = methodDefinition3.Body.Instructions[methodDefinition3.Body.Instructions.Count - 1];
						if (instruction2.OpCode == OpCodes.Ret)
						{
							methodDefinition3.Body.Instructions.RemoveAt(methodDefinition3.Body.Instructions.Count - 1);
							break;
						}
						Weaver.fail = true;
						Log.Error("No ctor for " + this.m_td.Name);
						return;
					}
				}
				if (methodDefinition3 == null)
				{
					Weaver.fail = true;
					Log.Error("No ctor for " + this.m_td.Name);
				}
				else
				{
					var ilprocessor = methodDefinition3.Body.GetILProcessor();
					var ilprocessor2 = methodDefinition.Body.GetILProcessor();
					var num = 0;
					foreach (var methodDefinition5 in this.m_Cmds)
					{
						var field = Weaver.ResolveField(this.m_td, "kCmd" + methodDefinition5.Name);
						var hashCode = NetworkBehaviourProcessor.GetHashCode(this.m_td.Name + ":Cmd:" + methodDefinition5.Name);
						ilprocessor2.Append(ilprocessor2.Create(OpCodes.Ldc_I4, hashCode));
						ilprocessor2.Append(ilprocessor2.Create(OpCodes.Stsfld, field));
						this.GenerateCommandDelegate(ilprocessor2, Weaver.registerCommandDelegateReference, this.m_CmdInvocationFuncs[num], field);
						num++;
					}
					var num2 = 0;
					foreach (var methodDefinition6 in this.m_Rpcs)
					{
						var field2 = Weaver.ResolveField(this.m_td, "kRpc" + methodDefinition6.Name);
						var hashCode2 = NetworkBehaviourProcessor.GetHashCode(this.m_td.Name + ":Rpc:" + methodDefinition6.Name);
						ilprocessor2.Append(ilprocessor2.Create(OpCodes.Ldc_I4, hashCode2));
						ilprocessor2.Append(ilprocessor2.Create(OpCodes.Stsfld, field2));
						this.GenerateCommandDelegate(ilprocessor2, Weaver.registerRpcDelegateReference, this.m_RpcInvocationFuncs[num2], field2);
						num2++;
					}
					var num3 = 0;
					foreach (var methodDefinition7 in this.m_TargetRpcs)
					{
						var field3 = Weaver.ResolveField(this.m_td, "kTargetRpc" + methodDefinition7.Name);
						var hashCode3 = NetworkBehaviourProcessor.GetHashCode(this.m_td.Name + ":TargetRpc:" + methodDefinition7.Name);
						ilprocessor2.Append(ilprocessor2.Create(OpCodes.Ldc_I4, hashCode3));
						ilprocessor2.Append(ilprocessor2.Create(OpCodes.Stsfld, field3));
						this.GenerateCommandDelegate(ilprocessor2, Weaver.registerRpcDelegateReference, this.m_TargetRpcInvocationFuncs[num3], field3);
						num3++;
					}
					var num4 = 0;
					foreach (var eventDefinition in this.m_Events)
					{
						var field4 = Weaver.ResolveField(this.m_td, "kEvent" + eventDefinition.Name);
						var hashCode4 = NetworkBehaviourProcessor.GetHashCode(this.m_td.Name + ":Event:" + eventDefinition.Name);
						ilprocessor2.Append(ilprocessor2.Create(OpCodes.Ldc_I4, hashCode4));
						ilprocessor2.Append(ilprocessor2.Create(OpCodes.Stsfld, field4));
						this.GenerateCommandDelegate(ilprocessor2, Weaver.registerEventDelegateReference, this.m_EventInvocationFuncs[num4], field4);
						num4++;
					}
					var num5 = 0;
					foreach (var fieldDefinition in this.m_SyncLists)
					{
						var field5 = Weaver.ResolveField(this.m_td, "kList" + fieldDefinition.Name);
						var hashCode5 = NetworkBehaviourProcessor.GetHashCode(this.m_td.Name + ":List:" + fieldDefinition.Name);
						ilprocessor2.Append(ilprocessor2.Create(OpCodes.Ldc_I4, hashCode5));
						ilprocessor2.Append(ilprocessor2.Create(OpCodes.Stsfld, field5));
						this.GenerateSyncListInstanceInitializer(ilprocessor, fieldDefinition);
						this.GenerateCommandDelegate(ilprocessor2, Weaver.registerSyncListDelegateReference, this.m_SyncListInvocationFuncs[num5], field5);
						num5++;
					}
					ilprocessor2.Append(ilprocessor2.Create(OpCodes.Ldstr, this.m_td.Name));
					ilprocessor2.Append(ilprocessor2.Create(OpCodes.Ldc_I4, this.m_QosChannel));
					ilprocessor2.Append(ilprocessor2.Create(OpCodes.Call, Weaver.RegisterBehaviourReference));
					ilprocessor2.Append(ilprocessor2.Create(OpCodes.Ret));
					if (!flag)
					{
						this.m_td.Methods.Add(methodDefinition);
					}
					ilprocessor.Append(ilprocessor.Create(OpCodes.Ret));
					this.m_td.Attributes = (this.m_td.Attributes & ~TypeAttributes.BeforeFieldInit);
					if (this.m_SyncLists.Count != 0)
					{
						MethodDefinition methodDefinition8 = null;
						var flag2 = false;
						foreach (var methodDefinition9 in this.m_td.Methods)
						{
							if (methodDefinition9.Name == "Awake")
							{
								methodDefinition8 = methodDefinition9;
								flag2 = true;
							}
						}
						if (methodDefinition8 != null)
						{
							if (methodDefinition8.Body.Instructions.Count != 0)
							{
								var instruction3 = methodDefinition8.Body.Instructions[methodDefinition8.Body.Instructions.Count - 1];
								if (!(instruction3.OpCode == OpCodes.Ret))
								{
									Log.Error("No awake for " + this.m_td.Name);
									Weaver.fail = true;
									return;
								}
								methodDefinition8.Body.Instructions.RemoveAt(methodDefinition8.Body.Instructions.Count - 1);
							}
						}
						else
						{
							methodDefinition8 = new MethodDefinition("Awake", MethodAttributes.Private, Weaver.voidType);
						}
						var ilprocessor3 = methodDefinition8.Body.GetILProcessor();
						if (!flag2)
						{
							this.CheckForCustomBaseClassAwakeMethod(ilprocessor3);
						}
						var num6 = 0;
						foreach (var fd in this.m_SyncLists)
						{
							this.GenerateSyncListInitializer(ilprocessor3, fd, num6);
							num6++;
						}
						ilprocessor3.Append(ilprocessor3.Create(OpCodes.Ret));
						if (!flag2)
						{
							this.m_td.Methods.Add(methodDefinition8);
						}
					}
				}
			}
		}

		private void CheckForCustomBaseClassAwakeMethod(ILProcessor awakeWorker)
		{
			var baseType = this.m_td.BaseType;
			while (baseType.FullName != Weaver.NetworkBehaviourType.FullName)
			{
				var methodDefinition = Enumerable.FirstOrDefault<MethodDefinition>(baseType.Resolve().Methods, (MethodDefinition x) => x.Name == "Awake" && !x.HasParameters);
				if (methodDefinition != null)
				{
					awakeWorker.Append(awakeWorker.Create(OpCodes.Ldarg_0));
					awakeWorker.Append(awakeWorker.Create(OpCodes.Call, methodDefinition));
					break;
				}
				baseType = baseType.Resolve().BaseType;
			}
		}

		private void GenerateSyncListInstanceInitializer(ILProcessor ctorWorker, FieldDefinition fd)
		{
			foreach (var instruction in ctorWorker.Body.Instructions)
			{
				if (instruction.OpCode.Code == Code.Stfld)
				{
					var fieldDefinition = (FieldDefinition)instruction.Operand;
					if (fieldDefinition.DeclaringType == fd.DeclaringType && fieldDefinition.Name == fd.Name)
					{
						return;
					}
				}
			}
			var method = Weaver.scriptDef.MainModule.ImportReference(Enumerable.First<MethodDefinition>(fd.FieldType.Resolve().Methods, (MethodDefinition x) => x.Name == ".ctor" && !x.HasParameters));
			ctorWorker.Append(ctorWorker.Create(OpCodes.Ldarg_0));
			ctorWorker.Append(ctorWorker.Create(OpCodes.Newobj, method));
			ctorWorker.Append(ctorWorker.Create(OpCodes.Stfld, fd));
		}

		private void GenerateCommandDelegate(ILProcessor awakeWorker, MethodReference registerMethod, MethodDefinition func, FieldReference field)
		{
			awakeWorker.Append(awakeWorker.Create(OpCodes.Ldtoken, this.m_td));
			awakeWorker.Append(awakeWorker.Create(OpCodes.Call, Weaver.getTypeFromHandleReference));
			awakeWorker.Append(awakeWorker.Create(OpCodes.Ldsfld, field));
			awakeWorker.Append(awakeWorker.Create(OpCodes.Ldnull));
			awakeWorker.Append(awakeWorker.Create(OpCodes.Ldftn, func));
			awakeWorker.Append(awakeWorker.Create(OpCodes.Newobj, Weaver.CmdDelegateConstructor));
			awakeWorker.Append(awakeWorker.Create(OpCodes.Call, registerMethod));
		}

		private void GenerateSyncListInitializer(ILProcessor awakeWorker, FieldReference fd, int index)
		{
			awakeWorker.Append(awakeWorker.Create(OpCodes.Ldarg_0));
			awakeWorker.Append(awakeWorker.Create(OpCodes.Ldfld, fd));
			awakeWorker.Append(awakeWorker.Create(OpCodes.Ldarg_0));
			awakeWorker.Append(awakeWorker.Create(OpCodes.Ldsfld, this.m_SyncListStaticFields[index]));
			var genericInstanceType = (GenericInstanceType)fd.FieldType.Resolve().BaseType;
			genericInstanceType = (GenericInstanceType)Weaver.scriptDef.MainModule.ImportReference(genericInstanceType);
			var typeReference = genericInstanceType.GenericArguments[0];
			var method = Helpers.MakeHostInstanceGeneric(Weaver.SyncListInitBehaviourReference, new TypeReference[]
			{
				typeReference
			});
			awakeWorker.Append(awakeWorker.Create(OpCodes.Callvirt, method));
			Weaver.scriptDef.MainModule.ImportReference(method);
		}

		private void GenerateSerialization()
		{
			Weaver.DLog(this.m_td, "  NetworkBehaviour GenerateSerialization", new object[0]);
			foreach (var methodDefinition in this.m_td.Methods)
			{
				if (methodDefinition.Name == "OnSerialize")
				{
					Weaver.DLog(this.m_td, "    Abort - is OnSerialize", new object[0]);
					return;
				}
			}
			var methodDefinition2 = new MethodDefinition("OnSerialize", MethodAttributes.FamANDAssem | MethodAttributes.Family | MethodAttributes.Virtual | MethodAttributes.HideBySig, Weaver.boolType);
			methodDefinition2.Parameters.Add(new ParameterDefinition("writer", ParameterAttributes.None, Weaver.scriptDef.MainModule.ImportReference(Weaver.NetworkWriterType)));
			methodDefinition2.Parameters.Add(new ParameterDefinition("forceAll", ParameterAttributes.None, Weaver.boolType));
			var ilprocessor = methodDefinition2.Body.GetILProcessor();
			methodDefinition2.Body.InitLocals = true;
			var item = new VariableDefinition(Weaver.boolType);
			methodDefinition2.Body.Variables.Add(item);
			var flag = false;

			if (this.m_td.BaseType.FullName != Weaver.NetworkBehaviourType.FullName)
			{
				var methodReference = Weaver.ResolveMethod(this.m_td.BaseType, "OnSerialize");
				if (methodReference != null)
				{
					var item2 = new VariableDefinition(Weaver.boolType);
					methodDefinition2.Body.Variables.Add(item2);
					ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_0));
					ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_1));
					ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_2));
					ilprocessor.Append(ilprocessor.Create(OpCodes.Call, methodReference));
					ilprocessor.Append(ilprocessor.Create(OpCodes.Stloc_1));
					flag = true;
				}
			}

			if (this.m_SyncVars.Count == 0)
			{
				Weaver.DLog(this.m_td, "    No syncvars", new object[0]);
				if (flag)
				{
					ilprocessor.Append(ilprocessor.Create(OpCodes.Ldloc_0));
					ilprocessor.Append(ilprocessor.Create(OpCodes.Ldloc_1));
					ilprocessor.Append(ilprocessor.Create(OpCodes.Or));
				}
				else
				{
					ilprocessor.Append(ilprocessor.Create(OpCodes.Ldloc_0));
				}
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ret));
				this.m_td.Methods.Add(methodDefinition2);
			}
			else
			{
				Weaver.DLog(this.m_td, "    Syncvars exist", new object[0]);
				var instruction = ilprocessor.Create(OpCodes.Nop);
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_2));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Brfalse, instruction));
				foreach (var fieldDefinition in this.m_SyncVars)
				{
					Weaver.DLog(this.m_td, $"    For {fieldDefinition.Name}", new object[0]);
					ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_1));
					ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_0));
					ilprocessor.Append(ilprocessor.Create(OpCodes.Ldfld, fieldDefinition));
					var writeFunc = Weaver.GetWriteFunc(fieldDefinition.FieldType);
					if (writeFunc == null)
					{
						Weaver.fail = true;
						Log.Error(string.Concat(new object[]
						{
							"GenerateSerialization for ",
							this.m_td.Name,
							" unknown type [",
							fieldDefinition.FieldType,
							"]. UNet [SyncVar] member variables must be basic types."
						}));
						return;
					}
					ilprocessor.Append(ilprocessor.Create(OpCodes.Call, writeFunc));
				}
				Weaver.DLog(this.m_td, $"    Finish foreach 1", new object[0]);
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ldc_I4_1));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ret));
				ilprocessor.Append(instruction);
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ldc_I4_0));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Stloc_0));
				var num = Weaver.GetSyncVarStart(this.m_td.BaseType.FullName);
				foreach (var fieldDefinition2 in this.m_SyncVars)
				{
					Weaver.DLog(this.m_td, $"    For {fieldDefinition2.Name}", new object[0]);
					var instruction2 = ilprocessor.Create(OpCodes.Nop);
					Weaver.DLog(this.m_td, $"    Got instruction2", new object[0]);
					ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_0));
					Weaver.DLog(this.m_td, $"    call dirtbits reference", new object[0]);
					ilprocessor.Append(ilprocessor.Create(OpCodes.Call, Weaver.NetworkBehaviourDirtyBitsReference));
					Weaver.DLog(this.m_td, $"    finish call dirtbits reference", new object[0]);
					ilprocessor.Append(ilprocessor.Create(OpCodes.Ldc_I4, 1 << num));
					ilprocessor.Append(ilprocessor.Create(OpCodes.And));
					ilprocessor.Append(ilprocessor.Create(OpCodes.Brfalse, instruction2));
					Weaver.DLog(this.m_td, $"    writing dirtycheck", new object[0]);
					NetworkBehaviourProcessor.WriteDirtyCheck(ilprocessor, true);
					Weaver.DLog(this.m_td, $"    done writing dirtycheck", new object[0]);
					ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_1));
					ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_0));
					ilprocessor.Append(ilprocessor.Create(OpCodes.Ldfld, fieldDefinition2));
					Weaver.DLog(this.m_td, $"    Getting writeFunc2", new object[0]);
					var writeFunc2 = Weaver.GetWriteFunc(fieldDefinition2.FieldType);
					Weaver.DLog(this.m_td, $"    Got writeFunc2", new object[0]);
					if (writeFunc2 == null)
					{
						Log.Error(string.Concat(new object[]
						{
							"GenerateSerialization for ",
							this.m_td.Name,
							" unknown type [",
							fieldDefinition2.FieldType,
							"]. UNet [SyncVar] member variables must be basic types."
						}));
						Weaver.fail = true;
						return;
					}
					ilprocessor.Append(ilprocessor.Create(OpCodes.Call, writeFunc2));
					ilprocessor.Append(instruction2);
					num++;
				}
				Weaver.DLog(this.m_td, $"    Finish foreach 2", new object[0]);
				NetworkBehaviourProcessor.WriteDirtyCheck(ilprocessor, false);
				if (Weaver.generateLogErrors)
				{
					ilprocessor.Append(ilprocessor.Create(OpCodes.Ldstr, "Injected Serialize " + this.m_td.Name));
					ilprocessor.Append(ilprocessor.Create(OpCodes.Call, Weaver.logErrorReference));
				}
				if (flag)
				{
					ilprocessor.Append(ilprocessor.Create(OpCodes.Ldloc_0));
					ilprocessor.Append(ilprocessor.Create(OpCodes.Ldloc_1));
					ilprocessor.Append(ilprocessor.Create(OpCodes.Or));
				}
				else
				{
					ilprocessor.Append(ilprocessor.Create(OpCodes.Ldloc_0));
				}
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ret));
				this.m_td.Methods.Add(methodDefinition2);
				Weaver.DLog(this.m_td, $"    Finish", new object[0]);
			}
			Weaver.DLog(this.m_td, $"  Finish", new object[0]);
		}

		private static void WriteDirtyCheck(ILProcessor serWorker, bool reset)
		{
			var instruction = serWorker.Create(OpCodes.Nop);
			serWorker.Append(serWorker.Create(OpCodes.Ldloc_0));
			serWorker.Append(serWorker.Create(OpCodes.Brtrue, instruction));
			serWorker.Append(serWorker.Create(OpCodes.Ldarg_1));
			serWorker.Append(serWorker.Create(OpCodes.Ldarg_0));
			serWorker.Append(serWorker.Create(OpCodes.Call, Weaver.NetworkBehaviourDirtyBitsReference));
			serWorker.Append(serWorker.Create(OpCodes.Callvirt, Weaver.NetworkWriterWritePacked32));
			if (reset)
			{
				serWorker.Append(serWorker.Create(OpCodes.Ldc_I4_1));
				serWorker.Append(serWorker.Create(OpCodes.Stloc_0));
			}
			serWorker.Append(instruction);
		}

		private static int GetChannelId(FieldDefinition field)
		{
			var result = 0;
			foreach (var customAttribute in field.CustomAttributes)
			{
				if (customAttribute.AttributeType.FullName == Weaver.SyncVarType.FullName)
				{
					foreach (var customAttributeNamedArgument in customAttribute.Fields)
					{
						if (customAttributeNamedArgument.Name == "channel")
						{
							result = (int)customAttributeNamedArgument.Argument.Value;
							break;
						}
					}
				}
			}
			return result;
		}

		private bool CheckForHookFunction(FieldDefinition syncVar, out MethodDefinition foundMethod)
		{
			foundMethod = null;
			foreach (var customAttribute in syncVar.CustomAttributes)
			{
				if (customAttribute.AttributeType.FullName == Weaver.SyncVarType.FullName)
				{
					foreach (var customAttributeNamedArgument in customAttribute.Fields)
					{
						if (customAttributeNamedArgument.Name == "hook")
						{
							var text = customAttributeNamedArgument.Argument.Value as string;
							foreach (var methodDefinition in this.m_td.Methods)
							{
								if (methodDefinition.Name == text)
								{
									if (methodDefinition.Parameters.Count != 1)
									{
										Log.Error("SyncVar Hook function " + text + " must have one argument " + this.m_td.Name);
										Weaver.fail = true;
										return false;
									}
									if (methodDefinition.Parameters[0].ParameterType != syncVar.FieldType)
									{
										Log.Error("SyncVar Hook function " + text + " has wrong type signature for " + this.m_td.Name);
										Weaver.fail = true;
										return false;
									}
									foundMethod = methodDefinition;
									return true;
								}
							}
							Log.Error("SyncVar Hook function " + text + " not found for " + this.m_td.Name);
							Weaver.fail = true;
							return false;
						}
					}
				}
			}
			return true;
		}

		private void GenerateNetworkChannelSetting(int channel)
		{
			var methodDefinition = new MethodDefinition("GetNetworkChannel", MethodAttributes.FamANDAssem | MethodAttributes.Family | MethodAttributes.Virtual | MethodAttributes.HideBySig, Weaver.int32Type);
			var ilprocessor = methodDefinition.Body.GetILProcessor();
			ilprocessor.Append(ilprocessor.Create(OpCodes.Ldc_I4, channel));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Ret));
			this.m_td.Methods.Add(methodDefinition);
		}

		private void GenerateNetworkIntervalSetting(float interval)
		{
			var methodDefinition = new MethodDefinition("GetNetworkSendInterval", MethodAttributes.FamANDAssem | MethodAttributes.Family | MethodAttributes.Virtual | MethodAttributes.HideBySig, Weaver.singleType);
			var ilprocessor = methodDefinition.Body.GetILProcessor();
			ilprocessor.Append(ilprocessor.Create(OpCodes.Ldc_R4, interval));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Ret));
			this.m_td.Methods.Add(methodDefinition);
		}

		private void GenerateNetworkSettings()
		{
			foreach (var customAttribute in this.m_td.CustomAttributes)
			{
				if (customAttribute.AttributeType.FullName == Weaver.NetworkSettingsType.FullName)
				{
					foreach (var customAttributeNamedArgument in customAttribute.Fields)
					{
						if (customAttributeNamedArgument.Name == "channel")
						{
							if ((int)customAttributeNamedArgument.Argument.Value == 0)
							{
								continue;
							}
							if (this.HasMethod("GetNetworkChannel"))
							{
								Log.Error("GetNetworkChannel, is already implemented, please make sure you either use NetworkSettings or GetNetworkChannel");
								Weaver.fail = true;
								return;
							}
							this.m_QosChannel = (int)customAttributeNamedArgument.Argument.Value;
							this.GenerateNetworkChannelSetting(this.m_QosChannel);
						}
						if (customAttributeNamedArgument.Name == "sendInterval")
						{
							if (Math.Abs((float)customAttributeNamedArgument.Argument.Value - 0.1f) > 1E-05f)
							{
								if (this.HasMethod("GetNetworkSendInterval"))
								{
									Log.Error("GetNetworkSendInterval, is already implemented, please make sure you either use NetworkSettings or GetNetworkSendInterval");
									Weaver.fail = true;
									return;
								}
								this.GenerateNetworkIntervalSetting((float)customAttributeNamedArgument.Argument.Value);
							}
						}
					}
				}
			}
		}

		private void GeneratePreStartClient()
		{
			this.m_NetIdFieldCounter = 0;
			MethodDefinition methodDefinition = null;
			ILProcessor ilprocessor = null;
			foreach (var methodDefinition2 in this.m_td.Methods)
			{
				if (methodDefinition2.Name == "PreStartClient")
				{
					return;
				}
			}
			foreach (var fieldDefinition in this.m_SyncVars)
			{
				if (fieldDefinition.FieldType.FullName == Weaver.gameObjectType.FullName)
				{
					if (methodDefinition == null)
					{
						methodDefinition = new MethodDefinition("PreStartClient", MethodAttributes.FamANDAssem | MethodAttributes.Family | MethodAttributes.Virtual | MethodAttributes.HideBySig, Weaver.voidType);
						ilprocessor = methodDefinition.Body.GetILProcessor();
					}
					var field = this.m_SyncVarNetIds[this.m_NetIdFieldCounter];
					this.m_NetIdFieldCounter++;
					var instruction = ilprocessor.Create(OpCodes.Nop);
					ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_0));
					ilprocessor.Append(ilprocessor.Create(OpCodes.Ldflda, field));
					ilprocessor.Append(ilprocessor.Create(OpCodes.Call, Weaver.NetworkInstanceIsEmpty));
					ilprocessor.Append(ilprocessor.Create(OpCodes.Brtrue, instruction));
					ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_0));
					ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_0));
					ilprocessor.Append(ilprocessor.Create(OpCodes.Ldfld, field));
					ilprocessor.Append(ilprocessor.Create(OpCodes.Call, Weaver.FindLocalObjectReference));
					ilprocessor.Append(ilprocessor.Create(OpCodes.Stfld, fieldDefinition));
					ilprocessor.Append(instruction);
				}
			}
			if (methodDefinition != null)
			{
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ret));
				this.m_td.Methods.Add(methodDefinition);
			}
		}

		private void GenerateDeSerialization()
		{
			Weaver.DLog(this.m_td, "  GenerateDeSerialization", new object[0]);
			this.m_NetIdFieldCounter = 0;
			foreach (var methodDefinition in this.m_td.Methods)
			{
				if (methodDefinition.Name == "OnDeserialize")
				{
					return;
				}
			}
			var methodDefinition2 = new MethodDefinition("OnDeserialize", MethodAttributes.FamANDAssem | MethodAttributes.Family | MethodAttributes.Virtual | MethodAttributes.HideBySig, Weaver.voidType);
			methodDefinition2.Parameters.Add(new ParameterDefinition("reader", ParameterAttributes.None, Weaver.scriptDef.MainModule.ImportReference(Weaver.NetworkReaderType)));
			methodDefinition2.Parameters.Add(new ParameterDefinition("initialState", ParameterAttributes.None, Weaver.boolType));
			var ilprocessor = methodDefinition2.Body.GetILProcessor();
			if (this.m_td.BaseType.FullName != Weaver.NetworkBehaviourType.FullName)
			{
				var methodReference = Weaver.ResolveMethod(this.m_td.BaseType, "OnDeserialize");
				if (methodReference != null)
				{
					ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_0));
					ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_1));
					ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_2));
					ilprocessor.Append(ilprocessor.Create(OpCodes.Call, methodReference));
				}
			}
			if (this.m_SyncVars.Count == 0)
			{
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ret));
				this.m_td.Methods.Add(methodDefinition2);
			}
			else
			{
				var instruction = ilprocessor.Create(OpCodes.Nop);
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_2));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Brfalse, instruction));
				foreach (var fieldDefinition in this.m_SyncVars)
				{
					var readByReferenceFunc = Weaver.GetReadByReferenceFunc(fieldDefinition.FieldType);
					if (readByReferenceFunc != null)
					{
						ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_1));
						ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_0));
						ilprocessor.Append(ilprocessor.Create(OpCodes.Ldfld, fieldDefinition));
						ilprocessor.Append(ilprocessor.Create(OpCodes.Call, readByReferenceFunc));
					}
					else
					{
						ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_0));
						ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_1));
						if (fieldDefinition.FieldType.FullName == Weaver.gameObjectType.FullName)
						{
							var field = this.m_SyncVarNetIds[this.m_NetIdFieldCounter];
							this.m_NetIdFieldCounter++;
							ilprocessor.Append(ilprocessor.Create(OpCodes.Callvirt, Weaver.NetworkReaderReadNetworkInstanceId));
							ilprocessor.Append(ilprocessor.Create(OpCodes.Stfld, field));
						}
						else
						{
							var readFunc = Weaver.GetReadFunc(fieldDefinition.FieldType);
							if (readFunc == null)
							{
								Log.Error(string.Concat(new object[]
								{
									"GenerateDeSerialization for ",
									this.m_td.Name,
									" unknown type [",
									fieldDefinition.FieldType,
									"]. UNet [SyncVar] member variables must be basic types."
								}));
								Weaver.fail = true;
								return;
							}
							ilprocessor.Append(ilprocessor.Create(OpCodes.Call, readFunc));
							ilprocessor.Append(ilprocessor.Create(OpCodes.Stfld, fieldDefinition));
						}
					}
				}
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ret));
				ilprocessor.Append(instruction);
				methodDefinition2.Body.InitLocals = true;
				var item = new VariableDefinition(Weaver.int32Type);
				methodDefinition2.Body.Variables.Add(item);
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_1));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Callvirt, Weaver.NetworkReaderReadPacked32));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Stloc_0));
				var num = Weaver.GetSyncVarStart(this.m_td.BaseType.FullName);
				foreach (var fieldDefinition2 in this.m_SyncVars)
				{
					var instruction2 = ilprocessor.Create(OpCodes.Nop);
					ilprocessor.Append(ilprocessor.Create(OpCodes.Ldloc_0));
					ilprocessor.Append(ilprocessor.Create(OpCodes.Ldc_I4, 1 << num));
					ilprocessor.Append(ilprocessor.Create(OpCodes.And));
					ilprocessor.Append(ilprocessor.Create(OpCodes.Brfalse, instruction2));
					var readByReferenceFunc2 = Weaver.GetReadByReferenceFunc(fieldDefinition2.FieldType);
					if (readByReferenceFunc2 != null)
					{
						ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_1));
						ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_0));
						ilprocessor.Append(ilprocessor.Create(OpCodes.Ldfld, fieldDefinition2));
						ilprocessor.Append(ilprocessor.Create(OpCodes.Call, readByReferenceFunc2));
					}
					else
					{
						var readFunc2 = Weaver.GetReadFunc(fieldDefinition2.FieldType);
						if (readFunc2 == null)
						{
							Log.Error(string.Concat(new object[]
							{
								"GenerateDeSerialization for ",
								this.m_td.Name,
								" unknown type [",
								fieldDefinition2.FieldType,
								"]. UNet [SyncVar] member variables must be basic types."
							}));
							Weaver.fail = true;
							return;
						}
						MethodDefinition methodDefinition3;
						if (!this.CheckForHookFunction(fieldDefinition2, out methodDefinition3))
						{
							return;
						}
						if (methodDefinition3 == null)
						{
							ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_0));
							ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_1));
							ilprocessor.Append(ilprocessor.Create(OpCodes.Call, readFunc2));
							ilprocessor.Append(ilprocessor.Create(OpCodes.Stfld, fieldDefinition2));
						}
						else
						{
							ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_0));
							ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_1));
							ilprocessor.Append(ilprocessor.Create(OpCodes.Call, readFunc2));
							ilprocessor.Append(ilprocessor.Create(OpCodes.Call, methodDefinition3));
						}
					}
					ilprocessor.Append(instruction2);
					num++;
				}
				if (Weaver.generateLogErrors)
				{
					ilprocessor.Append(ilprocessor.Create(OpCodes.Ldstr, "Injected Deserialize " + this.m_td.Name));
					ilprocessor.Append(ilprocessor.Create(OpCodes.Call, Weaver.logErrorReference));
				}
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ret));
				this.m_td.Methods.Add(methodDefinition2);
			}
		}

		private bool ProcessNetworkReaderParameters(MethodDefinition md, ILProcessor worker, bool skipFirst)
		{
			var num = 0;
			foreach (var parameterDefinition in md.Parameters)
			{
				if (num++ != 0 || !skipFirst)
				{
					var readFunc = Weaver.GetReadFunc(parameterDefinition.ParameterType);
					if (readFunc == null)
					{
						Log.Error(string.Concat(new object[]
						{
							"ProcessNetworkReaderParameters for ",
							this.m_td.Name,
							":",
							md.Name,
							" type ",
							parameterDefinition.ParameterType,
							" not supported"
						}));
						Weaver.fail = true;
						return false;
					}
					worker.Append(worker.Create(OpCodes.Ldarg_1));
					worker.Append(worker.Create(OpCodes.Call, readFunc));
					if (parameterDefinition.ParameterType.FullName == Weaver.singleType.FullName)
					{
						worker.Append(worker.Create(OpCodes.Conv_R4));
					}
					else if (parameterDefinition.ParameterType.FullName == Weaver.doubleType.FullName)
					{
						worker.Append(worker.Create(OpCodes.Conv_R8));
					}
				}
			}
			return true;
		}

		private MethodDefinition ProcessCommandInvoke(MethodDefinition md)
		{
			var methodDefinition = new MethodDefinition("InvokeCmd" + md.Name, MethodAttributes.Family | MethodAttributes.Static | MethodAttributes.HideBySig, Weaver.voidType);
			var ilprocessor = methodDefinition.Body.GetILProcessor();
			var label = ilprocessor.Create(OpCodes.Nop);
			NetworkBehaviourProcessor.WriteServerActiveCheck(ilprocessor, md.Name, label, "Command");
			ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_0));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Castclass, this.m_td));
			MethodDefinition result;
			if (!this.ProcessNetworkReaderParameters(md, ilprocessor, false))
			{
				result = null;
			}
			else
			{
				ilprocessor.Append(ilprocessor.Create(OpCodes.Callvirt, md));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ret));
				NetworkBehaviourProcessor.AddInvokeParameters(methodDefinition.Parameters);
				result = methodDefinition;
			}
			return result;
		}

		private static void AddInvokeParameters(ICollection<ParameterDefinition> collection)
		{
			collection.Add(new ParameterDefinition("obj", ParameterAttributes.None, Weaver.NetworkBehaviourType2));
			collection.Add(new ParameterDefinition("reader", ParameterAttributes.None, Weaver.scriptDef.MainModule.ImportReference(Weaver.NetworkReaderType)));
		}

		private MethodDefinition ProcessCommandCall(MethodDefinition md, CustomAttribute ca)
		{
			var methodDefinition = new MethodDefinition("Call" + md.Name, MethodAttributes.FamANDAssem | MethodAttributes.Family | MethodAttributes.HideBySig, Weaver.voidType);
			foreach (var parameterDefinition in md.Parameters)
			{
				methodDefinition.Parameters.Add(new ParameterDefinition(parameterDefinition.Name, ParameterAttributes.None, parameterDefinition.ParameterType));
			}
			var ilprocessor = methodDefinition.Body.GetILProcessor();
			var label = ilprocessor.Create(OpCodes.Nop);
			NetworkBehaviourProcessor.WriteSetupLocals(ilprocessor);
			if (Weaver.generateLogErrors)
			{
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ldstr, "Call Command function " + md.Name));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Call, Weaver.logErrorReference));
			}
			NetworkBehaviourProcessor.WriteClientActiveCheck(ilprocessor, md.Name, label, "Command function");
			var instruction = ilprocessor.Create(OpCodes.Nop);
			ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_0));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Call, Weaver.UBehaviourIsServer));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Brfalse, instruction));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_0));
			for (var i = 0; i < md.Parameters.Count; i++)
			{
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg, i + 1));
			}
			ilprocessor.Append(ilprocessor.Create(OpCodes.Call, md));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Ret));
			ilprocessor.Append(instruction);
			NetworkBehaviourProcessor.WriteCreateWriter(ilprocessor);
			NetworkBehaviourProcessor.WriteMessageSize(ilprocessor);
			NetworkBehaviourProcessor.WriteMessageId(ilprocessor, 5);
			var fieldDefinition = new FieldDefinition("kCmd" + md.Name, FieldAttributes.Private | FieldAttributes.Static, Weaver.int32Type);
			this.m_td.Fields.Add(fieldDefinition);
			ilprocessor.Append(ilprocessor.Create(OpCodes.Ldloc_0));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Ldsfld, fieldDefinition));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Callvirt, Weaver.NetworkWriterWritePacked32));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Ldloc_0));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_0));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Call, Weaver.getComponentReference));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Callvirt, Weaver.getUNetIdReference));
			var writeFunc = Weaver.GetWriteFunc(Weaver.NetworkInstanceIdType);
			ilprocessor.Append(ilprocessor.Create(OpCodes.Callvirt, writeFunc));
			MethodDefinition result;
			if (!NetworkBehaviourProcessor.WriteArguments(ilprocessor, md, "Command", false))
			{
				result = null;
			}
			else
			{
				var value = 0;
				foreach (var customAttributeNamedArgument in ca.Fields)
				{
					if (customAttributeNamedArgument.Name == "channel")
					{
						value = (int)customAttributeNamedArgument.Argument.Value;
					}
				}
				var text = md.Name;
				var num = text.IndexOf("InvokeCmd");
				if (num > -1)
				{
					text = text.Substring("InvokeCmd".Length);
				}
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_0));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ldloc_0));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ldc_I4, value));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ldstr, text));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Call, Weaver.sendCommandInternal));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ret));
				result = methodDefinition;
			}
			return result;
		}

		private MethodDefinition ProcessTargetRpcInvoke(MethodDefinition md)
		{
			var methodDefinition = new MethodDefinition("InvokeRpc" + md.Name, MethodAttributes.Family | MethodAttributes.Static | MethodAttributes.HideBySig, Weaver.voidType);
			var ilprocessor = methodDefinition.Body.GetILProcessor();
			var label = ilprocessor.Create(OpCodes.Nop);
			NetworkBehaviourProcessor.WriteClientActiveCheck(ilprocessor, md.Name, label, "TargetRPC");
			ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_0));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Castclass, this.m_td));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Call, Weaver.ReadyConnectionReference));
			MethodDefinition result;
			if (!this.ProcessNetworkReaderParameters(md, ilprocessor, true))
			{
				result = null;
			}
			else
			{
				ilprocessor.Append(ilprocessor.Create(OpCodes.Callvirt, md));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ret));
				NetworkBehaviourProcessor.AddInvokeParameters(methodDefinition.Parameters);
				result = methodDefinition;
			}
			return result;
		}

		private MethodDefinition ProcessRpcInvoke(MethodDefinition md)
		{
			var methodDefinition = new MethodDefinition("InvokeRpc" + md.Name, MethodAttributes.Family | MethodAttributes.Static | MethodAttributes.HideBySig, Weaver.voidType);
			var ilprocessor = methodDefinition.Body.GetILProcessor();
			var label = ilprocessor.Create(OpCodes.Nop);
			NetworkBehaviourProcessor.WriteClientActiveCheck(ilprocessor, md.Name, label, "RPC");
			ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_0));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Castclass, this.m_td));
			MethodDefinition result;
			if (!this.ProcessNetworkReaderParameters(md, ilprocessor, false))
			{
				result = null;
			}
			else
			{
				ilprocessor.Append(ilprocessor.Create(OpCodes.Callvirt, md));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ret));
				NetworkBehaviourProcessor.AddInvokeParameters(methodDefinition.Parameters);
				result = methodDefinition;
			}
			return result;
		}

		private MethodDefinition ProcessTargetRpcCall(MethodDefinition md, CustomAttribute ca)
		{
			var methodDefinition = new MethodDefinition("Call" + md.Name, MethodAttributes.FamANDAssem | MethodAttributes.Family | MethodAttributes.HideBySig, Weaver.voidType);
			foreach (var parameterDefinition in md.Parameters)
			{
				methodDefinition.Parameters.Add(new ParameterDefinition(parameterDefinition.Name, ParameterAttributes.None, parameterDefinition.ParameterType));
			}
			var ilprocessor = methodDefinition.Body.GetILProcessor();
			var label = ilprocessor.Create(OpCodes.Nop);
			NetworkBehaviourProcessor.WriteSetupLocals(ilprocessor);
			NetworkBehaviourProcessor.WriteServerActiveCheck(ilprocessor, md.Name, label, "TargetRPC Function");
			var instruction = ilprocessor.Create(OpCodes.Nop);
			ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_1));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Isinst, Weaver.ULocalConnectionToServerType));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Brfalse, instruction));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Ldstr, string.Format("TargetRPC Function {0} called on connection to server", md.Name)));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Call, Weaver.logErrorReference));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Ret));
			ilprocessor.Append(instruction);
			NetworkBehaviourProcessor.WriteCreateWriter(ilprocessor);
			NetworkBehaviourProcessor.WriteMessageSize(ilprocessor);
			NetworkBehaviourProcessor.WriteMessageId(ilprocessor, 2);
			var fieldDefinition = new FieldDefinition("kTargetRpc" + md.Name, FieldAttributes.Private | FieldAttributes.Static, Weaver.int32Type);
			this.m_td.Fields.Add(fieldDefinition);
			ilprocessor.Append(ilprocessor.Create(OpCodes.Ldloc_0));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Ldsfld, fieldDefinition));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Callvirt, Weaver.NetworkWriterWritePacked32));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Ldloc_0));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_0));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Call, Weaver.getComponentReference));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Callvirt, Weaver.getUNetIdReference));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Callvirt, Weaver.NetworkWriterWriteNetworkInstanceId));
			MethodDefinition result;
			if (!NetworkBehaviourProcessor.WriteArguments(ilprocessor, md, "TargetRPC", true))
			{
				result = null;
			}
			else
			{
				var value = 0;
				foreach (var customAttributeNamedArgument in ca.Fields)
				{
					if (customAttributeNamedArgument.Name == "channel")
					{
						value = (int)customAttributeNamedArgument.Argument.Value;
					}
				}
				var text = md.Name;
				var num = text.IndexOf("InvokeTargetRpc");
				if (num > -1)
				{
					text = text.Substring("InvokeTargetRpc".Length);
				}
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_0));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_1));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ldloc_0));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ldc_I4, value));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ldstr, text));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Callvirt, Weaver.sendTargetRpcInternal));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ret));
				result = methodDefinition;
			}
			return result;
		}

		private MethodDefinition ProcessRpcCall(MethodDefinition md, CustomAttribute ca)
		{
			var methodDefinition = new MethodDefinition("Call" + md.Name, MethodAttributes.FamANDAssem | MethodAttributes.Family | MethodAttributes.HideBySig, Weaver.voidType);
			foreach (var parameterDefinition in md.Parameters)
			{
				methodDefinition.Parameters.Add(new ParameterDefinition(parameterDefinition.Name, ParameterAttributes.None, parameterDefinition.ParameterType));
			}
			var ilprocessor = methodDefinition.Body.GetILProcessor();
			var label = ilprocessor.Create(OpCodes.Nop);
			NetworkBehaviourProcessor.WriteSetupLocals(ilprocessor);
			NetworkBehaviourProcessor.WriteServerActiveCheck(ilprocessor, md.Name, label, "RPC Function");
			NetworkBehaviourProcessor.WriteCreateWriter(ilprocessor);
			NetworkBehaviourProcessor.WriteMessageSize(ilprocessor);
			NetworkBehaviourProcessor.WriteMessageId(ilprocessor, 2);
			var fieldDefinition = new FieldDefinition("kRpc" + md.Name, FieldAttributes.Private | FieldAttributes.Static, Weaver.int32Type);
			this.m_td.Fields.Add(fieldDefinition);
			ilprocessor.Append(ilprocessor.Create(OpCodes.Ldloc_0));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Ldsfld, fieldDefinition));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Callvirt, Weaver.NetworkWriterWritePacked32));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Ldloc_0));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_0));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Call, Weaver.getComponentReference));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Callvirt, Weaver.getUNetIdReference));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Callvirt, Weaver.NetworkWriterWriteNetworkInstanceId));
			MethodDefinition result;
			if (!NetworkBehaviourProcessor.WriteArguments(ilprocessor, md, "RPC", false))
			{
				result = null;
			}
			else
			{
				var value = 0;
				foreach (var customAttributeNamedArgument in ca.Fields)
				{
					if (customAttributeNamedArgument.Name == "channel")
					{
						value = (int)customAttributeNamedArgument.Argument.Value;
					}
				}
				var text = md.Name;
				var num = text.IndexOf("InvokeRpc");
				if (num > -1)
				{
					text = text.Substring("InvokeRpc".Length);
				}
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_0));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ldloc_0));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ldc_I4, value));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ldstr, text));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Callvirt, Weaver.sendRpcInternal));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ret));
				result = methodDefinition;
			}
			return result;
		}

		private bool ProcessMethodsValidateFunction(MethodReference md, CustomAttribute ca, string actionType)
		{
			bool result;
			if (md.ReturnType.FullName == Weaver.IEnumeratorType.FullName)
			{
				Log.Error(string.Concat(new string[]
				{
					actionType,
					" function [",
					this.m_td.FullName,
					":",
					md.Name,
					"] cannot be a coroutine"
				}));
				Weaver.fail = true;
				result = false;
			}
			else if (md.ReturnType.FullName != Weaver.voidType.FullName)
			{
				Log.Error(string.Concat(new string[]
				{
					actionType,
					" function [",
					this.m_td.FullName,
					":",
					md.Name,
					"] must have a void return type."
				}));
				Weaver.fail = true;
				result = false;
			}
			else if (md.HasGenericParameters)
			{
				Log.Error(string.Concat(new string[]
				{
					actionType,
					" [",
					this.m_td.FullName,
					":",
					md.Name,
					"] cannot have generic parameters"
				}));
				Weaver.fail = true;
				result = false;
			}
			else
			{
				result = true;
			}
			return result;
		}

		private bool ProcessMethodsValidateParameters(MethodReference md, CustomAttribute ca, string actionType)
		{
			var i = 0;
			while (i < md.Parameters.Count)
			{
				var parameterDefinition = md.Parameters[i];
				bool result;
				if (parameterDefinition.IsOut)
				{
					Log.Error(string.Concat(new string[]
					{
						actionType,
						" function [",
						this.m_td.FullName,
						":",
						md.Name,
						"] cannot have out parameters"
					}));
					Weaver.fail = true;
					result = false;
				}
				else if (parameterDefinition.IsOptional)
				{
					Log.Error(string.Concat(new string[]
					{
						actionType,
						"function [",
						this.m_td.FullName,
						":",
						md.Name,
						"] cannot have optional parameters"
					}));
					Weaver.fail = true;
					result = false;
				}
				else if (parameterDefinition.ParameterType.Resolve().IsAbstract)
				{
					Log.Error(string.Concat(new string[]
					{
						actionType,
						" function [",
						this.m_td.FullName,
						":",
						md.Name,
						"] cannot have abstract parameters"
					}));
					Weaver.fail = true;
					result = false;
				}
				else if (parameterDefinition.ParameterType.IsByReference)
				{
					Log.Error(string.Concat(new string[]
					{
						actionType,
						" function [",
						this.m_td.FullName,
						":",
						md.Name,
						"] cannot have ref parameters"
					}));
					Weaver.fail = true;
					result = false;
				}
				else
				{
					if (!(parameterDefinition.ParameterType.FullName == Weaver.NetworkConnectionType.FullName) || (ca.AttributeType.FullName == Weaver.TargetRpcType.FullName && i == 0))
					{
						if (Weaver.IsDerivedFrom(parameterDefinition.ParameterType.Resolve(), Weaver.ComponentType))
						{
							if (parameterDefinition.ParameterType.FullName != Weaver.NetworkIdentityType.FullName)
							{
								Log.Error(string.Concat(new string[]
								{
									actionType,
									" function [",
									this.m_td.FullName,
									":",
									md.Name,
									"] parameter [",
									parameterDefinition.Name,
									"] is of the type [",
									parameterDefinition.ParameterType.Name,
									"] which is a Component. You cannot pass a Component to a remote call. Try passing data from within the component."
								}));
								Weaver.fail = true;
								return false;
							}
						}
						i++;
						continue;
					}
					Log.Error(string.Concat(new string[]
					{
						actionType,
						" [",
						this.m_td.FullName,
						":",
						md.Name,
						"] cannot use a NetworkConnection as a parameter. To access a player object's connection on the server use connectionToClient"
					}));
					Log.Error("Name: " + ca.AttributeType.FullName + " parameter: " + md.Parameters[0].ParameterType.FullName);
					Weaver.fail = true;
					result = false;
				}
				return result;
			}
			return true;
		}

		private bool ProcessMethodsValidateCommand(MethodDefinition md, CustomAttribute ca)
		{
			bool result;
			if (md.Name.Length > 2 && md.Name.Substring(0, 3) != "Cmd")
			{
				Log.Error(string.Concat(new string[]
				{
					"Command function [",
					this.m_td.FullName,
					":",
					md.Name,
					"] doesnt have 'Cmd' prefix"
				}));
				Weaver.fail = true;
				result = false;
			}
			else if (md.IsStatic)
			{
				Log.Error(string.Concat(new string[]
				{
					"Command function [",
					this.m_td.FullName,
					":",
					md.Name,
					"] cant be a static method"
				}));
				Weaver.fail = true;
				result = false;
			}
			else
			{
				result = (this.ProcessMethodsValidateFunction(md, ca, "Command") && this.ProcessMethodsValidateParameters(md, ca, "Command"));
			}
			return result;
		}

		private bool ProcessMethodsValidateTargetRpc(MethodDefinition md, CustomAttribute ca)
		{
			var length = "Target".Length;
			bool result;
			if (md.Name.Length > length && md.Name.Substring(0, length) != "Target")
			{
				Log.Error(string.Concat(new string[]
				{
					"Target Rpc function [",
					this.m_td.FullName,
					":",
					md.Name,
					"] doesnt have 'Target' prefix"
				}));
				Weaver.fail = true;
				result = false;
			}
			else if (md.IsStatic)
			{
				Log.Error(string.Concat(new string[]
				{
					"TargetRpc function [",
					this.m_td.FullName,
					":",
					md.Name,
					"] cant be a static method"
				}));
				Weaver.fail = true;
				result = false;
			}
			else if (!this.ProcessMethodsValidateFunction(md, ca, "Target Rpc"))
			{
				result = false;
			}
			else if (md.Parameters.Count < 1)
			{
				Log.Error(string.Concat(new string[]
				{
					"Target Rpc function [",
					this.m_td.FullName,
					":",
					md.Name,
					"] must have a NetworkConnection as the first parameter"
				}));
				Weaver.fail = true;
				result = false;
			}
			else if (md.Parameters[0].ParameterType.FullName != Weaver.NetworkConnectionType.FullName)
			{
				Log.Error(string.Concat(new string[]
				{
					"Target Rpc function [",
					this.m_td.FullName,
					":",
					md.Name,
					"] first parameter must be a NetworkConnection"
				}));
				Weaver.fail = true;
				result = false;
			}
			else
			{
				result = this.ProcessMethodsValidateParameters(md, ca, "Target Rpc");
			}
			return result;
		}

		private bool ProcessMethodsValidateRpc(MethodDefinition md, CustomAttribute ca)
		{
			bool result;
			if (md.Name.Length > 2 && md.Name.Substring(0, 3) != "Rpc")
			{
				Log.Error(string.Concat(new string[]
				{
					"Rpc function [",
					this.m_td.FullName,
					":",
					md.Name,
					"] doesnt have 'Rpc' prefix"
				}));
				Weaver.fail = true;
				result = false;
			}
			else if (md.IsStatic)
			{
				Log.Error(string.Concat(new string[]
				{
					"ClientRpc function [",
					this.m_td.FullName,
					":",
					md.Name,
					"] cant be a static method"
				}));
				Weaver.fail = true;
				result = false;
			}
			else
			{
				result = (this.ProcessMethodsValidateFunction(md, ca, "Rpc") && this.ProcessMethodsValidateParameters(md, ca, "Rpc"));
			}
			return result;
		}

		private void ProcessMethods()
		{
			var hashSet = new HashSet<string>();
			foreach (var methodDefinition in this.m_td.Methods)
			{
				Weaver.ResetRecursionCount();
				foreach (var customAttribute in methodDefinition.CustomAttributes)
				{
					if (customAttribute.AttributeType.FullName == Weaver.CommandType.FullName)
					{
						if (!this.ProcessMethodsValidateCommand(methodDefinition, customAttribute))
						{
							return;
						}
						if (hashSet.Contains(methodDefinition.Name))
						{
							Log.Error(string.Concat(new string[]
							{
								"Duplicate Command name [",
								this.m_td.FullName,
								":",
								methodDefinition.Name,
								"]"
							}));
							Weaver.fail = true;
							return;
						}
						hashSet.Add(methodDefinition.Name);
						this.m_Cmds.Add(methodDefinition);
						var methodDefinition2 = this.ProcessCommandInvoke(methodDefinition);
						if (methodDefinition2 != null)
						{
							this.m_CmdInvocationFuncs.Add(methodDefinition2);
						}
						var methodDefinition3 = this.ProcessCommandCall(methodDefinition, customAttribute);
						if (methodDefinition3 != null)
						{
							this.m_CmdCallFuncs.Add(methodDefinition3);
							Weaver.lists.replacedMethods.Add(methodDefinition);
							Weaver.lists.replacementMethods.Add(methodDefinition3);
						}
						break;
					}
					else if (customAttribute.AttributeType.FullName == Weaver.TargetRpcType.FullName)
					{
						if (!this.ProcessMethodsValidateTargetRpc(methodDefinition, customAttribute))
						{
							return;
						}
						if (hashSet.Contains(methodDefinition.Name))
						{
							Log.Error(string.Concat(new string[]
							{
								"Duplicate Target Rpc name [",
								this.m_td.FullName,
								":",
								methodDefinition.Name,
								"]"
							}));
							Weaver.fail = true;
							return;
						}
						hashSet.Add(methodDefinition.Name);
						this.m_TargetRpcs.Add(methodDefinition);
						var methodDefinition4 = this.ProcessTargetRpcInvoke(methodDefinition);
						if (methodDefinition4 != null)
						{
							this.m_TargetRpcInvocationFuncs.Add(methodDefinition4);
						}
						var methodDefinition5 = this.ProcessTargetRpcCall(methodDefinition, customAttribute);
						if (methodDefinition5 != null)
						{
							this.m_TargetRpcCallFuncs.Add(methodDefinition5);
							Weaver.lists.replacedMethods.Add(methodDefinition);
							Weaver.lists.replacementMethods.Add(methodDefinition5);
						}
						break;
					}
					else if (customAttribute.AttributeType.FullName == Weaver.ClientRpcType.FullName)
					{
						if (!this.ProcessMethodsValidateRpc(methodDefinition, customAttribute))
						{
							return;
						}
						if (hashSet.Contains(methodDefinition.Name))
						{
							Log.Error(string.Concat(new string[]
							{
								"Duplicate ClientRpc name [",
								this.m_td.FullName,
								":",
								methodDefinition.Name,
								"]"
							}));
							Weaver.fail = true;
							return;
						}
						hashSet.Add(methodDefinition.Name);
						this.m_Rpcs.Add(methodDefinition);
						var methodDefinition6 = this.ProcessRpcInvoke(methodDefinition);
						if (methodDefinition6 != null)
						{
							this.m_RpcInvocationFuncs.Add(methodDefinition6);
						}
						var methodDefinition7 = this.ProcessRpcCall(methodDefinition, customAttribute);
						if (methodDefinition7 != null)
						{
							this.m_RpcCallFuncs.Add(methodDefinition7);
							Weaver.lists.replacedMethods.Add(methodDefinition);
							Weaver.lists.replacementMethods.Add(methodDefinition7);
						}
						break;
					}
				}
			}
			foreach (var item in this.m_CmdInvocationFuncs)
			{
				this.m_td.Methods.Add(item);
			}
			foreach (var item2 in this.m_CmdCallFuncs)
			{
				this.m_td.Methods.Add(item2);
			}
			foreach (var item3 in this.m_RpcInvocationFuncs)
			{
				this.m_td.Methods.Add(item3);
			}
			foreach (var item4 in this.m_TargetRpcInvocationFuncs)
			{
				this.m_td.Methods.Add(item4);
			}
			foreach (var item5 in this.m_RpcCallFuncs)
			{
				this.m_td.Methods.Add(item5);
			}
			foreach (var item6 in this.m_TargetRpcCallFuncs)
			{
				this.m_td.Methods.Add(item6);
			}
		}

		private MethodDefinition ProcessEventInvoke(EventDefinition ed)
		{
			FieldDefinition fieldDefinition = null;
			foreach (var fieldDefinition2 in this.m_td.Fields)
			{
				if (fieldDefinition2.FullName == ed.FullName)
				{
					fieldDefinition = fieldDefinition2;
					break;
				}
			}
			MethodDefinition result;
			if (fieldDefinition == null)
			{
				Weaver.DLog(this.m_td, "ERROR: no event field?!", new object[0]);
				Weaver.fail = true;
				result = null;
			}
			else
			{
				var methodDefinition = new MethodDefinition("InvokeSyncEvent" + ed.Name, MethodAttributes.Family | MethodAttributes.Static | MethodAttributes.HideBySig, Weaver.voidType);
				var ilprocessor = methodDefinition.Body.GetILProcessor();
				var label = ilprocessor.Create(OpCodes.Nop);
				var instruction = ilprocessor.Create(OpCodes.Nop);
				NetworkBehaviourProcessor.WriteClientActiveCheck(ilprocessor, ed.Name, label, "Event");
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_0));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Castclass, this.m_td));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ldfld, fieldDefinition));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Brtrue, instruction));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ret));
				ilprocessor.Append(instruction);
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_0));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Castclass, this.m_td));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ldfld, fieldDefinition));
				var methodReference = Weaver.ResolveMethod(fieldDefinition.FieldType, "Invoke");
				if (!this.ProcessNetworkReaderParameters(methodReference.Resolve(), ilprocessor, false))
				{
					result = null;
				}
				else
				{
					ilprocessor.Append(ilprocessor.Create(OpCodes.Callvirt, methodReference));
					ilprocessor.Append(ilprocessor.Create(OpCodes.Ret));
					NetworkBehaviourProcessor.AddInvokeParameters(methodDefinition.Parameters);
					result = methodDefinition;
				}
			}
			return result;
		}

		private MethodDefinition ProcessEventCall(EventDefinition ed, CustomAttribute ca)
		{
			var methodReference = Weaver.ResolveMethod(ed.EventType, "Invoke");
			var methodDefinition = new MethodDefinition("Call" + ed.Name, MethodAttributes.FamANDAssem | MethodAttributes.Family | MethodAttributes.HideBySig, Weaver.voidType);
			foreach (var parameterDefinition in methodReference.Parameters)
			{
				methodDefinition.Parameters.Add(new ParameterDefinition(parameterDefinition.Name, ParameterAttributes.None, parameterDefinition.ParameterType));
			}
			var ilprocessor = methodDefinition.Body.GetILProcessor();
			var label = ilprocessor.Create(OpCodes.Nop);
			NetworkBehaviourProcessor.WriteSetupLocals(ilprocessor);
			NetworkBehaviourProcessor.WriteServerActiveCheck(ilprocessor, ed.Name, label, "Event");
			NetworkBehaviourProcessor.WriteCreateWriter(ilprocessor);
			NetworkBehaviourProcessor.WriteMessageSize(ilprocessor);
			NetworkBehaviourProcessor.WriteMessageId(ilprocessor, 7);
			var fieldDefinition = new FieldDefinition("kEvent" + ed.Name, FieldAttributes.Private | FieldAttributes.Static, Weaver.int32Type);
			this.m_td.Fields.Add(fieldDefinition);
			ilprocessor.Append(ilprocessor.Create(OpCodes.Ldloc_0));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Ldsfld, fieldDefinition));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Callvirt, Weaver.NetworkWriterWritePacked32));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Ldloc_0));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_0));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Call, Weaver.getComponentReference));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Callvirt, Weaver.getUNetIdReference));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Callvirt, Weaver.NetworkWriterWriteNetworkInstanceId));
			MethodDefinition result;
			if (!NetworkBehaviourProcessor.WriteArguments(ilprocessor, methodReference.Resolve(), "SyncEvent", false))
			{
				result = null;
			}
			else
			{
				var value = 0;
				foreach (var customAttributeNamedArgument in ca.Fields)
				{
					if (customAttributeNamedArgument.Name == "channel")
					{
						value = (int)customAttributeNamedArgument.Argument.Value;
					}
				}
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_0));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ldloc_0));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ldc_I4, value));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ldstr, ed.Name));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Call, Weaver.sendEventInternal));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ret));
				result = methodDefinition;
			}
			return result;
		}

		private void ProcessEvents()
		{
			foreach (var eventDefinition in this.m_td.Events)
			{
				foreach (var customAttribute in eventDefinition.CustomAttributes)
				{
					if (customAttribute.AttributeType.FullName == Weaver.SyncEventType.FullName)
					{
						if (eventDefinition.Name.Length > 4 && eventDefinition.Name.Substring(0, 5) != "Event")
						{
							Log.Error(string.Concat(new string[]
							{
								"Event  [",
								this.m_td.FullName,
								":",
								eventDefinition.FullName,
								"] doesnt have 'Event' prefix"
							}));
							Weaver.fail = true;
							return;
						}
						if (eventDefinition.EventType.Resolve().HasGenericParameters)
						{
							Log.Error(string.Concat(new string[]
							{
								"Event  [",
								this.m_td.FullName,
								":",
								eventDefinition.FullName,
								"] cannot have generic parameters"
							}));
							Weaver.fail = true;
							return;
						}
						this.m_Events.Add(eventDefinition);
						var methodDefinition = this.ProcessEventInvoke(eventDefinition);
						if (methodDefinition == null)
						{
							return;
						}
						this.m_td.Methods.Add(methodDefinition);
						this.m_EventInvocationFuncs.Add(methodDefinition);
						Weaver.DLog(this.m_td, "ProcessEvent " + eventDefinition, new object[0]);
						var methodDefinition2 = this.ProcessEventCall(eventDefinition, customAttribute);
						this.m_td.Methods.Add(methodDefinition2);
						Weaver.lists.replacedEvents.Add(eventDefinition);
						Weaver.lists.replacementEvents.Add(methodDefinition2);
						Weaver.DLog(this.m_td, "  Event: " + eventDefinition.Name, new object[0]);
						break;
					}
				}
			}
		}

		private static MethodDefinition ProcessSyncVarGet(FieldDefinition fd, string originalName)
		{
			var methodDefinition = new MethodDefinition("get_Network" + originalName, MethodAttributes.FamANDAssem | MethodAttributes.Family | MethodAttributes.HideBySig | MethodAttributes.SpecialName, fd.FieldType);
			var ilprocessor = methodDefinition.Body.GetILProcessor();
			ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_0));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Ldfld, fd));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Ret));
			methodDefinition.Body.Variables.Add(new VariableDefinition(fd.FieldType));
			methodDefinition.Body.InitLocals = true;
			methodDefinition.SemanticsAttributes = MethodSemanticsAttributes.Getter;
			return methodDefinition;
		}

		private MethodDefinition ProcessSyncVarSet(FieldDefinition fd, string originalName, int dirtyBit, FieldDefinition netFieldId)
		{
			var methodDefinition = new MethodDefinition("set_Network" + originalName, MethodAttributes.FamANDAssem | MethodAttributes.Family | MethodAttributes.HideBySig | MethodAttributes.SpecialName, Weaver.voidType);
			var ilprocessor = methodDefinition.Body.GetILProcessor();
			ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_0));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_1));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_0));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Ldflda, fd));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Ldc_I4, dirtyBit));
			MethodDefinition methodDefinition2;
			this.CheckForHookFunction(fd, out methodDefinition2);
			if (methodDefinition2 != null)
			{
				var instruction = ilprocessor.Create(OpCodes.Nop);
				ilprocessor.Append(ilprocessor.Create(OpCodes.Call, Weaver.NetworkServerGetLocalClientActive));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Brfalse, instruction));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_0));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Call, Weaver.getSyncVarHookGuard));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Brtrue, instruction));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_0));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ldc_I4_1));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Call, Weaver.setSyncVarHookGuard));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_0));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_1));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Call, methodDefinition2));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_0));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ldc_I4_0));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Call, Weaver.setSyncVarHookGuard));
				ilprocessor.Append(instruction);
			}
			if (fd.FieldType.FullName == Weaver.gameObjectType.FullName)
			{
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_0));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Ldflda, netFieldId));
				ilprocessor.Append(ilprocessor.Create(OpCodes.Call, Weaver.setSyncVarGameObjectReference));
			}
			else
			{
				var genericInstanceMethod = new GenericInstanceMethod(Weaver.setSyncVarReference);
				genericInstanceMethod.GenericArguments.Add(fd.FieldType);
				ilprocessor.Append(ilprocessor.Create(OpCodes.Call, genericInstanceMethod));
			}
			ilprocessor.Append(ilprocessor.Create(OpCodes.Ret));
			methodDefinition.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.In, fd.FieldType));
			methodDefinition.SemanticsAttributes = MethodSemanticsAttributes.Setter;
			return methodDefinition;
		}

		private void ProcessSyncVar(FieldDefinition fd, int dirtyBit)
		{
			var name = fd.Name;
			Weaver.lists.replacedFields.Add(fd);
			Weaver.DLog(m_td, $"Found SyncVar {fd.Name} of type {fd.FieldType}", new object[0]);
			FieldDefinition fieldDefinition = null;
			if (fd.FieldType.FullName == Weaver.gameObjectType.FullName)
			{
				fieldDefinition = new FieldDefinition("___" + fd.Name + "NetId", FieldAttributes.Private, Weaver.NetworkInstanceIdType);
				this.m_SyncVarNetIds.Add(fieldDefinition);
				Weaver.lists.netIdFields.Add(fieldDefinition);
			}
			var methodDefinition = NetworkBehaviourProcessor.ProcessSyncVarGet(fd, name);
			var methodDefinition2 = this.ProcessSyncVarSet(fd, name, dirtyBit, fieldDefinition);
			var item = new PropertyDefinition("Network" + name, PropertyAttributes.None, fd.FieldType)
			{
				GetMethod = methodDefinition,
				SetMethod = methodDefinition2
			};
			this.m_td.Methods.Add(methodDefinition);
			this.m_td.Methods.Add(methodDefinition2);
			this.m_td.Properties.Add(item);
			Weaver.lists.replacementProperties.Add(methodDefinition2);
		}

		private static MethodDefinition ProcessSyncListInvoke(FieldDefinition fd)
		{
			var methodDefinition = new MethodDefinition("InvokeSyncList" + fd.Name, MethodAttributes.Family | MethodAttributes.Static | MethodAttributes.HideBySig, Weaver.voidType);
			var ilprocessor = methodDefinition.Body.GetILProcessor();
			var label = ilprocessor.Create(OpCodes.Nop);
			NetworkBehaviourProcessor.WriteClientActiveCheck(ilprocessor, fd.Name, label, "SyncList");
			ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_0));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Castclass, fd.DeclaringType));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Ldfld, fd));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Ldarg_1));
			var genericInstanceType = (GenericInstanceType)fd.FieldType.Resolve().BaseType;
			genericInstanceType = (GenericInstanceType)Weaver.scriptDef.MainModule.ImportReference(genericInstanceType);
			var typeReference = genericInstanceType.GenericArguments[0];
			var method = Helpers.MakeHostInstanceGeneric(Weaver.SyncListInitHandleMsg, new TypeReference[]
			{
				typeReference
			});
			ilprocessor.Append(ilprocessor.Create(OpCodes.Callvirt, method));
			ilprocessor.Append(ilprocessor.Create(OpCodes.Ret));
			NetworkBehaviourProcessor.AddInvokeParameters(methodDefinition.Parameters);
			return methodDefinition;
		}

		private FieldDefinition ProcessSyncList(FieldDefinition fd, int dirtyBit)
		{
			var methodDefinition = NetworkBehaviourProcessor.ProcessSyncListInvoke(fd);
			this.m_SyncListInvocationFuncs.Add(methodDefinition);
			return new FieldDefinition("kList" + fd.Name, FieldAttributes.Private | FieldAttributes.Static, Weaver.int32Type);
		}

		private void ProcessSyncVars()
		{
			var num = 0;
			var num2 = Weaver.GetSyncVarStart(this.m_td.BaseType.FullName);
			this.m_SyncVarNetIds.Clear();
			var list = new List<FieldDefinition>();
			foreach (var fieldDefinition in this.m_td.Fields)
			{
				foreach (var customAttribute in fieldDefinition.CustomAttributes)
				{
					if (customAttribute.AttributeType.FullName == Weaver.SyncVarType.FullName)
					{
						var typeDefinition = fieldDefinition.FieldType.Resolve();
						if (Weaver.IsDerivedFrom(typeDefinition, Weaver.NetworkBehaviourType))
						{
							Log.Error("SyncVar [" + fieldDefinition.FullName + "] cannot be derived from NetworkBehaviour.");
							Weaver.fail = true;
							return;
						}
						if (Weaver.IsDerivedFrom(typeDefinition, Weaver.ScriptableObjectType))
						{
							Log.Error("SyncVar [" + fieldDefinition.FullName + "] cannot be derived from ScriptableObject.");
							Weaver.fail = true;
							return;
						}
						if ((ushort)(fieldDefinition.Attributes & FieldAttributes.Static) != 0)
						{
							Log.Error("SyncVar [" + fieldDefinition.FullName + "] cannot be static.");
							Weaver.fail = true;
							return;
						}
						if (typeDefinition.HasGenericParameters)
						{
							Log.Error("SyncVar [" + fieldDefinition.FullName + "] cannot have generic parameters.");
							Weaver.fail = true;
							return;
						}
						if (typeDefinition.IsInterface)
						{
							Log.Error("SyncVar [" + fieldDefinition.FullName + "] cannot be an interface.");
							Weaver.fail = true;
							return;
						}
						var name = typeDefinition.Module.Name;
						if (name != Weaver.scriptDef.MainModule.Name && name != Weaver.UnityAssemblyDefinition.MainModule.Name && name != Weaver.QNetAssemblyDefinition.MainModule.Name && name != Weaver.corLib.Name && name != "System.Runtime.dll")
						{
							Log.Error(string.Concat(new string[]
							{
								"SyncVar [",
								fieldDefinition.FullName,
								"] from ",
								typeDefinition.Module.ToString(),
								" cannot be a different module."
							}));
							Weaver.fail = true;
							return;
						}
						if (fieldDefinition.FieldType.IsArray)
						{
							Log.Error("SyncVar [" + fieldDefinition.FullName + "] cannot be an array. Use a SyncList instead.");
							Weaver.fail = true;
							return;
						}
						if (Helpers.InheritsFromSyncList(fieldDefinition.FieldType))
						{
							Log.Warning(string.Format("Script class [{0}] has [SyncVar] attribute on SyncList field {1}, SyncLists should not be marked with SyncVar.", this.m_td.FullName, fieldDefinition.Name));
							break;
						}
						this.m_SyncVars.Add(fieldDefinition);
						this.ProcessSyncVar(fieldDefinition, 1 << num2);
						num2++;
						num++;
						if (num2 == 32)
						{
							Log.Error(string.Concat(new object[]
							{
								"Script class [",
								this.m_td.FullName,
								"] has too many SyncVars (",
								32,
								"). (This could include base classes)"
							}));
							Weaver.fail = true;
							return;
						}
						break;
					}
				}
				if (fieldDefinition.FieldType.FullName.Contains("UnityEngine.Networking.SyncListStruct"))
				{
					Log.Error("SyncListStruct member variable [" + fieldDefinition.FullName + "] must use a dervied class, like \"class MySyncList : SyncListStruct<MyStruct> {}\".");
					Weaver.fail = true;
					return;
				}
				if (Weaver.IsDerivedFrom(fieldDefinition.FieldType.Resolve(), Weaver.SyncListType))
				{
					if (fieldDefinition.IsStatic)
					{
						Log.Error(string.Concat(new string[]
						{
							"SyncList [",
							this.m_td.FullName,
							":",
							fieldDefinition.FullName,
							"] cannot be a static"
						}));
						Weaver.fail = true;
						return;
					}
					this.m_SyncVars.Add(fieldDefinition);
					this.m_SyncLists.Add(fieldDefinition);
					list.Add(this.ProcessSyncList(fieldDefinition, 1 << num2));
					num2++;
					num++;
					if (num2 == 32)
					{
						Log.Error(string.Concat(new object[]
						{
							"Script class [",
							this.m_td.FullName,
							"] has too many SyncVars (",
							32,
							"). (This could include base classes)"
						}));
						Weaver.fail = true;
						return;
					}
				}
			}
			foreach (var fieldDefinition2 in list)
			{
				this.m_td.Fields.Add(fieldDefinition2);
				this.m_SyncListStaticFields.Add(fieldDefinition2);
			}
			foreach (var item in this.m_SyncVarNetIds)
			{
				this.m_td.Fields.Add(item);
			}
			foreach (var item2 in this.m_SyncListInvocationFuncs)
			{
				this.m_td.Methods.Add(item2);
			}
			Weaver.SetNumSyncVars(this.m_td.FullName, num);
		}

		private static int GetHashCode(string s)
		{
			var assembly = typeof(Unity.UNetWeaver.Program).Assembly;
			var networkProcessorType = assembly.GetType("NetworkBehaviourProcessor");
			return (int)networkProcessorType.GetMethod("GetHashCode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).Invoke(null, new object[] { s });
		}

		private bool HasMethod(string name)
		{
			foreach (var methodDefinition in this.m_td.Methods)
			{
				if (methodDefinition.Name == name)
				{
					return true;
				}
			}
			return false;
		}

		private List<FieldDefinition> m_SyncVars = new List<FieldDefinition>();

		private List<FieldDefinition> m_SyncLists = new List<FieldDefinition>();

		private List<FieldDefinition> m_SyncVarNetIds = new List<FieldDefinition>();

		private List<MethodDefinition> m_Cmds = new List<MethodDefinition>();

		private List<MethodDefinition> m_Rpcs = new List<MethodDefinition>();

		private List<MethodDefinition> m_TargetRpcs = new List<MethodDefinition>();

		private List<EventDefinition> m_Events = new List<EventDefinition>();

		private List<FieldDefinition> m_SyncListStaticFields = new List<FieldDefinition>();

		private List<MethodDefinition> m_CmdInvocationFuncs = new List<MethodDefinition>();

		private List<MethodDefinition> m_SyncListInvocationFuncs = new List<MethodDefinition>();

		private List<MethodDefinition> m_RpcInvocationFuncs = new List<MethodDefinition>();

		private List<MethodDefinition> m_TargetRpcInvocationFuncs = new List<MethodDefinition>();

		private List<MethodDefinition> m_EventInvocationFuncs = new List<MethodDefinition>();

		private List<MethodDefinition> m_CmdCallFuncs = new List<MethodDefinition>();

		private List<MethodDefinition> m_RpcCallFuncs = new List<MethodDefinition>();

		private List<MethodDefinition> m_TargetRpcCallFuncs = new List<MethodDefinition>();

		private const int k_SyncVarLimit = 32;

		private int m_QosChannel;

		private TypeDefinition m_td;

		private int m_NetIdFieldCounter;

		private const string k_CmdPrefix = "InvokeCmd";

		private const string k_RpcPrefix = "InvokeRpc";

		private const string k_TargetRpcPrefix = "InvokeTargetRpc";
	}
}
