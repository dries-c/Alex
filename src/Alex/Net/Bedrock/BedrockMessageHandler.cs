using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading;
using Alex.Common.Utils;
using Alex.Networking.Bedrock.RakNet;
using Microsoft.IO;
using MiNET.Net;
using MiNET.Net.RakNet;
using MiNET.Utils;
using MiNET.Utils.Cryptography;
using MiNET.Utils.IO;
using NLog;

namespace Alex.Net.Bedrock
{
	public class BedrockMessageHandler : ICustomMessageHandler
	{
		private static readonly ILogger Log = LogManager.GetCurrentClassLogger();

		private readonly McpeClientMessageDispatcher _messageDispatcher;

		public Action ConnectionAction { get; set; }
		public Action<string, bool> DisconnectedAction { get; set; }

		private readonly RaknetSession _session;

		public MiNET.Utils.Cryptography.CryptoContext CryptoContext { get; set; }

		private DateTime _lastPacketReceived;
		public TimeSpan TimeSinceLastPacket => DateTime.UtcNow - _lastPacketReceived;

		private BedrockClientPacketHandler PacketHandler { get; }

		public BedrockMessageHandler(RaknetSession session, BedrockClientPacketHandler handler) : base()
		{
			_session = session;
			_messageDispatcher = new McpeClientMessageDispatcher(handler);
			PacketHandler = handler;
		}

		public void Connected()
		{
			ConnectionAction?.Invoke();
		}

		public void Disconnect(string reason, bool sendDisconnect = true)
		{
			DisconnectedAction?.Invoke(reason, sendDisconnect);
		}

		public List<Packet> PrepareSend(List<Packet> packetsToSend)
		{
			var sendList = new List<Packet>();
			var sendInBatch = new List<Packet>();
			foreach (Packet packet in packetsToSend)
			{
				// We must send forced clear messages in single message batch because
				// we can't mix them with un-encrypted messages for obvious reasons.
				// If need be, we could put these in a batch of it's own, but too rare 
				// to bother.
				if (packet.ForceClear)
				{
					var wrapper = McpeWrapper.CreateObject();
					wrapper.ReliabilityHeader.Reliability = Reliability.ReliableOrdered;
					wrapper.ForceClear = true;
					wrapper.payload = _session.CompressionManager.CompressPacketsForWrapper(new List<Packet> {packet});
					wrapper.Encode(); // prepare
					packet.PutPool();
					sendList.Add(wrapper);
					continue;
				}

				if (packet is McpeWrapper)
				{
					packet.ReliabilityHeader.Reliability = Reliability.ReliableOrdered;
					sendList.Add(packet);
					continue;
				}

				if (!packet.IsMcpe)
				{
					packet.ReliabilityHeader.Reliability = packet.ReliabilityHeader.Reliability != Reliability.Undefined ? packet.ReliabilityHeader.Reliability : Reliability.Reliable;
					sendList.Add(packet);
					continue;
				}

				packet.ReliabilityHeader.Reliability = Reliability.ReliableOrdered;

				sendInBatch.Add(packet);
			}

			if (sendInBatch.Count > 0)
			{
				var batch = McpeWrapper.CreateObject();
				batch.ReliabilityHeader.Reliability = Reliability.ReliableOrdered;
				batch.payload = _session.CompressionManager.CompressPacketsForWrapper(sendInBatch);
				batch.Encode(); // prepare
				sendList.Add(batch);
			}

			return sendList;
		}

		public static RecyclableMemoryStreamManager MemoryStreamManager { get; set; } =
			new RecyclableMemoryStreamManager();
		
		public Packet HandleOrderedSend(Packet packet)
		{
			if (!packet.ForceClear && CryptoContext != null && CryptoContext.UseEncryption
			    && packet is McpeWrapper wrapper)
			{
				var encryptedWrapper = McpeWrapper.CreateObject();
				encryptedWrapper.ReliabilityHeader.Reliability = Reliability.ReliableOrdered;
				encryptedWrapper.payload = CryptoUtils.Encrypt(wrapper.payload, CryptoContext);
				encryptedWrapper.Encode();

				return encryptedWrapper;
			}

			return packet;
		}

		//private object _handlingLock = new object();
		public SemaphoreSlim PacketHandlingSemaphore { get; } = new SemaphoreSlim(1);
		public void HandlePacket(Packet message)
		{
			if (_session.Evicted)
				return;
			
			try
			{
				if (message == null) throw new NullReferenceException();
	
				if (message is McpeWrapper wrapper)
				{
					// Get bytes to process
					ReadOnlyMemory<byte> payload = wrapper.payload;
	
					// Decrypt bytes
	
					if (CryptoContext != null && CryptoContext.UseEncryption)
					{
						// This call copies the entire buffer, but what can we do? It is kind of compensated by not
						// creating a new buffer when parsing the packet (only a mem-slice)
						payload = CryptoUtils.Decrypt(payload, CryptoContext);
					}
	
					// Decompress bytes
	
					//var stream = new MemoryStreamReader(payload.Slice(0, payload.Length - 4)); // slice away adler
					//if (stream.ReadByte() != 0x78)
					//{
					//	if (Log.IsDebugEnabled) Log.Error($"Incorrect ZLib header. Expected 0x78 0x9C 0x{wrapper.Id:X2}\n{Packet.HexDump(wrapper.payload)}");
					//	if (Log.IsDebugEnabled) Log.Error($"Incorrect ZLib header. Decrypted 0x{wrapper.Id:X2}\n{Packet.HexDump(payload)}");
					//	throw new InvalidDataException("Incorrect ZLib header. Expected 0x78 0x9C");
					//}
					//stream.ReadByte();
	
					IEnumerable<Packet> messages;
					try
					{
						messages = (_session?.CompressionManager ?? CompressionManager.NoneCompressionManager).Decompress(payload);
					}
					catch (Exception e)
					{
						if (Log.IsDebugEnabled) Log.Warn($"Error parsing bedrock message \n{Packet.HexDump(payload)}", e);
						throw;
					}
	
					foreach (Packet msg in messages)
					{
						// Temp fix for performance, take 1.
						//var interact = msg as McpeInteract;
						//if (interact?.actionId == 4 && interact.targetRuntimeEntityId == 0) continue;
	
						msg.ReliabilityHeader = new ReliabilityHeader()
						{
							Reliability = wrapper.ReliabilityHeader.Reliability,
							ReliableMessageNumber = wrapper.ReliabilityHeader.ReliableMessageNumber,
							OrderingChannel = wrapper.ReliabilityHeader.OrderingChannel,
							OrderingIndex = wrapper.ReliabilityHeader.OrderingIndex,
						};
	
						//RakOfflineHandler.TraceReceive(Log, msg);
						try
						{
							HandleGamePacket(msg);
						}
						catch (Exception e)
						{
							Log.Warn($"Bedrock message handler error", e);
						}
					}
	
					wrapper.PutPool();
				}
				else if (message is UnknownPacket unknownPacket)
				{
					Log.Warn($"Received unknown packet 0x{unknownPacket.Id:X2}\n{Packet.HexDump(unknownPacket.Message)}");
	
					unknownPacket.PutPool();
				}
				else
				{
					Log.Error($"Unhandled packet: {message.GetType().Name} 0x{message.Id:X2}, IP {_session.EndPoint.Address}");
				}
			}
			finally
			{
				
			}
		}

		private void HandleGamePacket(Packet message)
		{
			if (_session.Evicted)
				return;

			//RaknetSession.TraceReceive(message);

			Stopwatch sw = Stopwatch.StartNew();

			try
			{
				     Log.Info($"Got packet: {message}");
				if (!_messageDispatcher.HandlePacket(message))
				{
					if (!PacketHandler.HandleOtherPackets(message))
					{
						if (message is UnknownPacket unknownPacket)
						{
							Log.Warn($"Received unknown packet 0x{unknownPacket.Id:X2}\n");
						}
					}
				}
			}
			catch (Exception ex)
			{
				// if (message.Id == 39)
				//     return;
				Log.Warn(ex, $"Packet handling error: {message} - {ex.ToString()}");
			}
			finally
			{
				sw.Stop();

				if (sw.ElapsedMilliseconds > 250)
				{
					Log.Warn(
						$"Packet handling took longer than expected! Time elapsed: {sw.ElapsedMilliseconds}ms (Packet={message})");
				}

				_lastPacketReceived = DateTime.UtcNow;
			}
		}
	}
}