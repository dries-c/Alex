#region LICENSE

// The contents of this file are subject to the Common Public Attribution
// License Version 1.0. (the "License"); you may not use this file except in
// compliance with the License. You may obtain a copy of the License at
// https://github.com/NiclasOlofsson/MiNET/blob/master/LICENSE.
// The License is based on the Mozilla Public License Version 1.1, but Sections 14
// and 15 have been added to cover use of software over a computer network and
// provide for limited attribution for the Original Developer. In addition, Exhibit A has
// been modified to be consistent with Exhibit B.
// 
// Software distributed under the License is distributed on an "AS IS" basis,
// WITHOUT WARRANTY OF ANY KIND, either express or implied. See the License for
// the specific language governing rights and limitations under the License.
// 
// The Original Code is MiNET.
// 
// The Original Developer is the Initial Developer.  The Initial Developer of
// the Original Code is Niclas Olofsson.
// 
// All portions of the code written by Niclas Olofsson are Copyright (c) 2014-2020 Niclas Olofsson.
// All Rights Reserved.

#endregion

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MiNET;
using MiNET.Net;
using MiNET.Net.RakNet;
using MiNET.Utils;
using MiNET.Utils.Collections;
using MiNET.Utils.IO;
using MiNET.Utils.Nbt;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using NLog;
using ConnectionInfo = Alex.Interfaces.Net.ConnectionInfo;
using LogManager = NLog.LogManager;

namespace Alex.Networking.Bedrock.RakNet
{
	public class RaknetSession : INetworkHandler
	{
		private static readonly ILogger Log = LogManager.GetCurrentClassLogger();

		private readonly RaknetConnection _connection;

		private long _lastOrderingIndex = -1; // That's the first message with wrapper


		private readonly ConcurrentDictionary<long, Packet> _orderingBufferQueue =
			new ConcurrentDictionary<long, Packet>();

		private CancellationTokenSource _cancellationToken;

		public ConnectionInfo ConnectionInfo { get; }

		public ICustomMessageHandler CustomMessageHandler { get; set; }

		public IPEndPoint EndPoint { get; }
		public short MtuSize { get; }
		public long NetworkIdentifier { get; set; }

		//public int DatagramSequenceNumber = -1;
		public int ReliableMessageNumber = -1;
		public int SplitPartId = 0;
		public int OrderingIndex = -1;

		public bool Evicted { get; set; }

		public ConnectionState State { get; set; } = ConnectionState.Unconnected;

		public DateTime LastUpdatedTime { get; set; }
		//public int ResendCount { get; set; }

		public ICongestionManager CongestionManager { get; }

		public const long InactivityTimeout = 30000;

		public ConcurrentDictionary<int, SplitPartPacket[]> Splits { get; } =
			new ConcurrentDictionary<int, SplitPartPacket[]>();

		private ConcurrentQueue<int> OutgoingAckQueue { get; } = new ConcurrentQueue<int>();
		private ConcurrentQueue<int> OutgoingNackQueue { get; } = new ConcurrentQueue<int>();

		public ConcurrentDictionary<int, Datagram> WaitingForAckQueue { get; } =
			new ConcurrentDictionary<int, Datagram>();

		public CompressionManager CompressionManager { get; } = new CompressionManager();
		private HighPrecisionTimer _tickerHighPrecisionTimer;

		public RaknetSession(Interfaces.Net.ConnectionInfo connectionInfo,
			RaknetConnection connection,
			IPEndPoint endPoint,
			short mtuSize,
			ICustomMessageHandler messageHandler = null)
		{
			_connection = connection;
			ConnectionInfo = connectionInfo;
			CustomMessageHandler = messageHandler;
			EndPoint = endPoint;
			MtuSize = mtuSize;

			_cancellationToken = new CancellationTokenSource();

			CongestionManager = new SlidingWindow(mtuSize);
			_tickerHighPrecisionTimer = new HighPrecisionTimer(10, SendTick);
		}

		/// <summary>
		///     Main receive entry to this layer. Will receive and handle messages
		///     on RakNet message level. May come from either UDP or TCP, matters not.
		/// </summary>
		/// <param name="message"></param>
		internal void HandleRakMessage(Packet message)
		{
			if (message == null) return;

			// This is not completely finished. Ordering and sequence streams (32 unique channels/streams each)
			// needs to work by their channel index. Right now, it's only one channel per reliability type.
			// According to Dylan order and sequence streams can run on the same channel, but documentation
			// says it can not. So I'll go with documentation until proven wrong.

			switch (message.ReliabilityHeader.Reliability)
			{
				case Reliability.ReliableOrdered:
				case Reliability.ReliableOrderedWithAckReceipt:
					AddToOrderedChannel(message);

					break;

				case Reliability.UnreliableSequenced:
				case Reliability.ReliableSequenced:
					Log.Warn(
						$"Sequenced: {message.ReliabilityHeader.SequencingIndex} | OrderIndex: {message.ReliabilityHeader.OrderingIndex}");

					AddToOrderedChannel(message);

					break;

				case Reliability.Unreliable:
				case Reliability.UnreliableWithAckReceipt:
				case Reliability.Reliable:
				case Reliability.ReliableWithAckReceipt:
					HandlePacket(message);

					break;

				case Reliability.Undefined:
					Log.Error($"Receive packet with undefined reliability");

					break;

				default:
					Log.Warn($"Receive packet with unexpected reliability={message.ReliabilityHeader.Reliability}");

					break;
			}
		}

		private AutoResetEvent _orderingResetEvent = new AutoResetEvent(false);

		private Thread _orderingThread = null;

		private void AddToOrderedChannel(Packet message)
		{
			try
			{
				if (_cancellationToken.Token.IsCancellationRequested) return;

				var current = message.ReliabilityHeader.OrderingIndex.IntValue();

				if (Interlocked.Read(ref _lastOrderingIndex) >= current)
					return;

				if (_orderingThread == null)
				{
					var task = new Task(ProcessOrderedQueue, _cancellationToken.Token, TaskCreationOptions.LongRunning);

					task.Start();
				}

				if (_orderingBufferQueue.TryAdd(current, message))
					_orderingResetEvent.Set();
			}
			catch (Exception e)
			{
				Log.Error(e, "Something went wrong!");
			}
		}

		public bool IsOutOfOrder => _orderingBufferQueue.Count > 1;

		private void ProcessOrderedQueue()
		{
			_orderingThread = Thread.CurrentThread;
			_orderingThread.Name = $"RaknetSession Ordering ({EndPoint})";

			try
			{
				while (!_cancellationToken.IsCancellationRequested && !Evicted)
				{
					var key = Interlocked.Read(ref _lastOrderingIndex) + 1;

					if (_orderingBufferQueue.TryRemove(key, out var value))
					{
						Interlocked.Exchange(ref _lastOrderingIndex, key);
						HandlePacket(value);

						continue;
					}

					_orderingResetEvent.WaitOne();
				}
			}
			catch (ObjectDisposedException)
			{
				// Ignore. Comes from the reset events being waited on while being disposed. Not a problem.
			}
			catch (Exception e)
			{
				Log.Error(e, $"Exit receive handler task for player");
			}

			_orderingThread = null;
		}

		private void HandlePacket(Packet message)
		{
			if (message == null) return;

			try
			{
				//	RakOfflineHandler.TraceReceive(Log, message);
				if (message.Id < (int)DefaultMessageIdTypes.ID_USER_PACKET_ENUM)
				{
					// Standard RakNet online message handlers
					switch (message)
					{
						case ConnectedPing connectedPing:
							HandleConnectedPing(connectedPing);

							break;

						case ConnectedPong connectedPong:
							//HandleConnectedPong(connectedPong);

							break;

						case DetectLostConnections _:
							break;

						case ConnectionRequest connectionRequest:
							HandleConnectionRequest(connectionRequest);

							break;

						case ConnectionRequestAccepted connectionRequestAccepted:
							HandleConnectionRequestAccepted(connectionRequestAccepted);

							break;

						case NewIncomingConnection newIncomingConnection:
							HandleNewIncomingConnection(newIncomingConnection);

							break;

						case DisconnectionNotification _:
							HandleDisconnectionNotification();

							break;

						default:
							Log.Error(
								$"Unhandled packet: {message.GetType().Name} 0x{message.Id:X2} IP {EndPoint.Address}");

							if (Log.IsDebugEnabled)
								Log.Warn($"Unknown packet 0x{message.Id:X2}\n{Packet.HexDump(message.Bytes)}");

							break;
					}
				}
				else
				{
					try
					{
						CustomMessageHandler.HandlePacket(message);
					}
					catch (Exception e)
					{
						// ignore
						if (e is InvalidDataException)
						{
							throw;
						}

						Log.Warn(e, $"Custom message handler error");
					}
				}
			}
			catch (Exception e)
			{
				Log.Error(e, "Packet handling error!");
				Disconnect("Packet handling error!", false);

				throw;
			}
			finally
			{
				message?.PutPool();
			}
		}

		protected virtual void HandleConnectedPing(ConnectedPing message)
		{
			var packet = ConnectedPong.CreateObject();
			packet.sendpingtime = message.sendpingtime;
			packet.sendpongtime = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
			SendPacket(packet);
		}

		protected virtual void HandleConnectionRequest(ConnectionRequest message)
		{
			Log.Debug("Connection request from: {0}", EndPoint.Address);

			var response = ConnectionRequestAccepted.CreateObject();
			response.NoBatch = true;
			response.systemAddress = new IPEndPoint(IPAddress.Loopback, 19132);
			response.systemAddresses = new IPEndPoint[20];
			response.systemAddresses[0] = new IPEndPoint(IPAddress.Loopback, 19132);
			response.incomingTimestamp = message.timestamp;
			response.serverTimestamp = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;

			for (int i = 1; i < 20; i++)
			{
				response.systemAddresses[i] = new IPEndPoint(IPAddress.Any, 19132);
			}

			SendPacket(response);
		}

		protected virtual void HandleNewIncomingConnection(NewIncomingConnection message)
		{
			Log.Debug($"New incoming connection from {EndPoint.Address} {EndPoint.Port}");

			State = ConnectionState.Connected;
		}

		private void HandleConnectionRequestAccepted(ConnectionRequestAccepted message)
		{
			SendNewIncomingConnection();

			State = ConnectionState.Connected;

			CustomMessageHandler?.Connected();
		}

		public void SendNewIncomingConnection()
		{
			var packet = NewIncomingConnection.CreateObject();
			packet.clientendpoint = EndPoint;
			packet.systemAddresses = new IPEndPoint[20];

			for (int i = 0; i < 20; i++)
			{
				packet.systemAddresses[i] = new IPEndPoint(IPAddress.Any, 0);
			}

			SendPacket(packet);
		}


		protected virtual void HandleDisconnectionNotification()
		{
			Disconnect("Server requested disconnect", false);
		}

		public virtual void Disconnect(string reason, bool sendDisconnect = true)
		{
			CustomMessageHandler?.Disconnect(reason, sendDisconnect);
			Close();
		}

		// MCPE Login handling

		private ConcurrentQueue<Packet> _sendQueue = new ConcurrentQueue<Packet>();

		public void SendPacket(Packet packet)
		{
			if (packet == null) return;

			if (State == ConnectionState.Unconnected)
			{
				if (Log.IsDebugEnabled)
					Log.Debug($"Ignoring send of packet {packet.GetType().Name} because session is not connected");

				packet.PutPool();

				return;
			}

			_sendQueue.Enqueue(packet);
		}

		private int _tickCounter = 0;
		private object _updateSync = new object();

		private void SendTick(object obj)
		{
			if (!_syncHack.Wait(0))
				return;

			try

			{
				SendAckQueue();

				if (_tickCounter++ >= 5)
				{
					SendQueue();
					DoResends();
					CheckTimeout();
					_tickCounter = 0;
				}

				SendNackQueue();
			}
			catch (Exception e)
			{
				Log.Warn(e);
			}
			finally
			{
				_syncHack.Release();
				//Monitor.Exit(_updateSync);
			}
		}

		private void DoResends()
		{
			if (Evicted) return;

			try
			{
				//UpdateSync.Wait();

				if (WaitingForAckQueue.Count == 0) return;

				//	long rto = SlidingWindow.GetRtoForRetransmission();
				//if (rto == 0) return;

				int transmissionBandwidth =
					CongestionManager.GetRetransmissionBandwidth(CurrentTimeMillis(), UnackedBytes);

				if (transmissionBandwidth <= 0)
					return;

				var queue = WaitingForAckQueue;

				foreach (KeyValuePair<int, Datagram> datagramPair in queue)
				{
					if (Evicted) return;

					Datagram datagram = datagramPair.Value;

					if (!datagram.Timer.IsRunning)
					{
						Log.Error($"Timer not running for #{datagram.Header.DatagramSequenceNumber}");
						datagram.Timer.Restart();

						continue;
					}

					//if (session.Rtt == -1) return;

					long elapsedTime = datagram.Timer.ElapsedMilliseconds;
					long datagramTimeout = datagram.RetransmissionTimeOut;
					//datagramTimeout = Math.Min(datagramTimeout, 3000);
					//datagramTimeout = Math.Max(datagramTimeout, 100);

					if ((datagram.ShouldRetransmit))
					{
						if (!Evicted && WaitingForAckQueue.TryRemove(datagramPair.Key, out datagram))
						{
							Interlocked.Add(ref UnackedBytes, -datagram.Size);

							//if (Log.IsDebugEnabled) 
							Log.Warn(
								$"{(datagram.RetransmitImmediate ? "NAK RSND" : "TIMEOUT")}, Resent #{datagramPair.Key}, Transmissions: {datagram.TransmissionCount} Type: {datagram.FirstMessageId} (0x{datagram.FirstMessageId:x2}) ({elapsedTime} > {datagramTimeout})");

							Interlocked.Increment(ref ConnectionInfo.Resends);

							CongestionManager.OnResend(RaknetSession.CurrentTimeMillis(), datagramPair.Key);

							var sent = _connection.SendDatagram(this, datagram);

							Interlocked.Add(ref UnackedBytes, sent);

							transmissionBandwidth -= sent;

							if (transmissionBandwidth <= 0)
								break;
						}
					}
				}
			}
			finally
			{
				//UpdateSync.Release();
			}
		}

		private void CheckTimeout()
		{
			if (Evicted) return;

			ConnectionInfo.Latency = (long)CongestionManager.GetRtt();
			long now = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
			long lastUpdate = LastUpdatedTime.Ticks / TimeSpan.TicksPerMillisecond;

			if (lastUpdate + InactivityTimeout + 3000 < now)
			{
				Evicted = true;
				Disconnect("Network timeout.");
				Close();

				return;
			}

			if (State != ConnectionState.Connected && CustomMessageHandler != null && lastUpdate + 3000 < now)
			{
				Disconnect("Lost connection.");

				return;
			}
		}

		//private ThreadSafeList<int> _nacked = new ThreadSafeList<int>();
		private void SendNackQueue()
		{
			var queue = OutgoingNackQueue;
			int queueCount = queue.Count;

			if (queueCount == 0) return;

			List<int> enqueued = new List<int>();

			for (int i = 0; i < queueCount; i++)
			{
				if (!queue.TryDequeue(out int nack)) break;

				if (!enqueued.Contains(nack))
				{
					enqueued.Add(nack);
					Interlocked.Increment(ref ConnectionInfo.NakSent);
				}
			}

			if (enqueued.Count > 0)
			{
				var acks = Nak.CreateObject();
				acks.ranges = Acks.Slize(enqueued);

				byte[] data = acks.Encode();
				_connection.SendData(data, EndPoint);
				this.CongestionManager.OnSendNack();

				acks.PutPool();
			}
		}

		private void SendAckQueue()
		{
			if (!CongestionManager.ShouldSendAcks(CurrentTimeMillis()))
				return;

			var queue = OutgoingAckQueue;
			int queueCount = queue.Count;

			if (queueCount == 0) return;

			var acks = Acks.CreateObject();

			try
			{
				for (int i = 0; i < queueCount; i++)
				{
					if (!queue.TryDequeue(out int ack)) break;

					if (!acks.acks.Contains(ack))
					{
						acks.acks.Add(ack);
						Interlocked.Increment(ref ConnectionInfo.AckSent);
					}
				}

				if (acks.acks.Count > 0)
				{
					byte[] data = acks.Encode();
					_connection.SendData(data, EndPoint);

					this.CongestionManager.OnSendAck();
				}
			}
			finally
			{
				acks.PutPool();
			}
		}

		private SemaphoreSlim _syncHack = new SemaphoreSlim(1, 1);
		public bool _bandwidthExceededStatistic = false;

		[SuppressMessage("ReSharper", "InconsistentlySynchronizedField")]
		private void SendQueue(int millisecondsWait = 0)
		{
			if (_sendQueue.Count == 0) return;
			
			try
			{
				var unacked = UnackedBytes;

				int transmissionBandwidth =
					this.CongestionManager.GetTransmissionBandwidth(unacked, _bandwidthExceededStatistic);

				if (transmissionBandwidth <= 0)
				{
					return;
				}

				var sendList = new List<Packet>();
				int length = _sendQueue.Count;

				int dataSize = 0;
				
				for (int i = 0; i < length; i++)
				{
					if (dataSize >= transmissionBandwidth)
						break;
					
					Packet packet;

					if (!_sendQueue.TryDequeue(out packet))
						break;

					if (packet == null) continue;

					if (State == ConnectionState.Unconnected)
					{
						packet.PutPool();

						continue;
					}
					
					sendList.Add(packet);
					var data = packet.Encode();
					dataSize += data.Length;
				}

				if (sendList.Count == 0) return;

				SendPrepareDirectPackets(sendList);
			}
			catch (Exception e)
			{
				Log.Error(e);
			}
			finally
			{
				//_syncHack.Release();
			}
		}

		public void SendDirectPacket(Packet packet)
		{
			if (packet.ReliabilityHeader.Reliability == Reliability.ReliableOrdered)
				throw new Exception(
					$"Can't send direct messages with ordering. The offending packet was {packet.GetType().Name}");

			if (packet.ReliabilityHeader.Reliability == Reliability.Undefined)
				packet.ReliabilityHeader.Reliability = Reliability.Reliable; // Questionable practice

			foreach (Datagram datagram in Datagram.CreateDatagrams(MtuSize, this, packet))
			{
				var data = _connection.SendDatagram(this, datagram);
				Interlocked.Add(ref UnackedBytes, data);
			}
		}

		public void SendPrepareDirectPacket(Packet packet)
		{
			SendPrepareDirectPackets(new List<Packet> {packet});
		}

		private void SendPrepareDirectPackets(List<Packet> sendList)
		{
			List<Packet> prepareSend = CustomMessageHandler.PrepareSend(sendList);
			Packet[] packets = new Packet[prepareSend.Count];

			for (var index = 0; index < prepareSend.Count; index++)
			{
				var packet = prepareSend[index];
				var message = packet;

				Reliability reliability = message.ReliabilityHeader.Reliability;

				if (reliability == Reliability.Undefined)
					reliability = Reliability.Reliable; // Questionable practice

				if (reliability == Reliability.ReliableOrdered
				    || reliability == Reliability.ReliableOrderedWithAckReceipt)
					message.ReliabilityHeader.OrderingIndex = Interlocked.Increment(ref OrderingIndex);

				packets[index] = message;
			}

			var sentData = 0;

			foreach (Datagram datagram in Datagram.CreateDatagrams(MtuSize, this, packets))
			{
				var data = _connection.SendDatagram(this, datagram);
				sentData += data;
				Interlocked.Add(ref UnackedBytes, data);
			}

			foreach (var packet in packets)
				packet?.PutPool();

			_bandwidthExceededStatistic = _sendQueue.Count > 0;
		}

		public IPEndPoint GetClientEndPoint()
		{
			return EndPoint;
		}

		public long GetNetworkNetworkIdentifier()
		{
			return NetworkIdentifier;
		}

		private bool _closed = false;

		public void Close(bool sendDisconnect = true)
		{
			if (_closed)
				return;

			_closed = true;

			_orderingResetEvent?.Dispose();
			_orderingResetEvent = null;

			_tickerHighPrecisionTimer?.Dispose();
			_tickerHighPrecisionTimer = null;

			State = ConnectionState.Unconnected;
			Evicted = true;

			CustomMessageHandler = null;

			SendQueue(500);
			
			if (sendDisconnect)
			{
				// Send with high priority, bypass queue
				SendDirectPacket(DisconnectionNotification.CreateObject());
			}

			_cancellationToken.Cancel();
			_orderingBufferQueue.Clear();

			var ackQueue = WaitingForAckQueue;

			foreach (var kvp in ackQueue)
			{
				if (ackQueue.TryRemove(kvp.Key, out Datagram datagram)) datagram.PutPool();
			}

			var splits = Splits;

			foreach (var kvp in splits)
			{
				if (splits.TryRemove(kvp.Key, out SplitPartPacket[] splitPartPackets))
				{
					if (splitPartPackets == null) continue;

					foreach (SplitPartPacket packet in splitPartPackets)
					{
						packet?.PutPool();
					}
				}
			}

			ackQueue.Clear();
			splits.Clear();

			try
			{
				_cancellationToken.Dispose();
			}
			catch
			{
				// ignored
			}

			if (Log.IsDebugEnabled) Log.Info($"Closed network session");
		}

		private static readonly DateTime Jan1st1970 = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

		public static long CurrentTimeMillis()
		{
			return (long)(DateTime.UtcNow - Jan1st1970).TotalMilliseconds;
		}

		public int UnackedBytes = 0;

		internal void HandleAck(Ack ack)
		{
			var queue = WaitingForAckQueue;

			void Ack(int i)
			{
				if (queue.TryRemove(i, out Datagram datagram))
				{
					Interlocked.Add(ref UnackedBytes, -datagram.Size);

					CongestionManager.OnAck(
						CurrentTimeMillis(), datagram.Timer.ElapsedMilliseconds, i, datagram.Header.IsContinuousSend);

					datagram.PutPool();
				}
				else
				{
					//if (Log.IsDebugEnabled) 
					Log.Warn($"ACK, Failed to remove datagram #{i}");
				}
			}

			foreach ((int start, int end) range in ack.ranges)
			{
				Interlocked.Increment(ref ConnectionInfo.Ack);

				if (range.start == range.end)
				{
					Ack(range.Item1);
				}
				else
				{
					for (int i = range.start; i <= range.end; i++)
					{
						Ack(i);
					}
				}
			}
		}

		internal void HandleNak(Nak nak)
		{
			void Nack(int i)
			{
				if (WaitingForAckQueue.TryGetValue(i, out var datagram))
				{
					CongestionManager.OnNak(CurrentTimeMillis(), i);
					//CalculateRto(datagram);

					datagram.RetransmitImmediate = true;
					datagram.RetransmissionTimeOut = (long)(CongestionManager.GetRtt());
					datagram.Timer.Restart();
				}
			}

			foreach (var range in nak.ranges)
			{
				Interlocked.Increment(ref ConnectionInfo.Nak);

				if (range.Item1 == range.Item2)
				{
					Nack(range.Item1);
				}
				else
				{
					for (int i = range.Item1; i <= range.Item2; i++)
					{
						Nack(i);
					}
				}
			}
		}

		public bool Acknowledge(Datagram datagram)
		{
			var sequenceIndex = datagram.Header.DatagramSequenceNumber.IntValue();
			OutgoingAckQueue.Enqueue(sequenceIndex);

			if (CongestionManager.OnPacketReceived(
				    CurrentTimeMillis(), sequenceIndex, datagram.Header.IsContinuousSend, datagram.Size,
				    out var skippedMessageCount))
			{
				if (skippedMessageCount > 0)
				{
					var start = sequenceIndex - skippedMessageCount;

					for (int i = 0; i < skippedMessageCount; i++)
					{
						OutgoingNackQueue.Enqueue((int)(start + i));
					}
				}

				if (datagram.Header.IsPacketPair)
					CongestionManager.OnGotPacketPair(datagram.Header.DatagramSequenceNumber);

				return true;
			}

			return false;
		}
	}
}