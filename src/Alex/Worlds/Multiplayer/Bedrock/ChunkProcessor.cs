using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks.Dataflow;
using Alex.Blocks;
using Alex.Blocks.Mapping;
using Alex.Common.Utils.Collections;
using Alex.Common.World;
using Alex.Net.Bedrock;
using Alex.Utils;
using Alex.Utils.Caching;
using Alex.Utils.Collections;
using Alex.Worlds.Chunks;
using ConcurrentObservableCollections.ConcurrentObservableDictionary;
using fNbt;
using MiNET;
using MiNET.Net;
using MiNET.Utils;
using NLog;
using BlockCoordinates = Alex.Common.Utils.Vectors.BlockCoordinates;
using BlockState = Alex.Blocks.State.BlockState;
using ChunkCoordinates = Alex.Common.Utils.Vectors.ChunkCoordinates;

namespace Alex.Worlds.Multiplayer.Bedrock
{
	public class ChunkProcessor : IDisposable
	{
		private static readonly Logger Log = LogManager.GetCurrentClassLogger(typeof(ChunkProcessor));
		public static ChunkProcessor Instance { get; private set; }

		static ChunkProcessor() { }

		private record BufferItem(KeyValuePair<ulong, byte[]> KeyValuePair, ChunkData ChunkData, SubChunkData SubChunkData);

		//public IReadOnlyDictionary<uint, BlockStateContainer> BlockStateMap { get; set; } =
		//    new Dictionary<uint, BlockStateContainer>();

		public static ItemStates Itemstates { get; set; } = new ItemStates();

		private ConcurrentDictionary<uint, uint> _convertedStates = new ConcurrentDictionary<uint, uint>();

		private CancellationToken CancellationToken { get; }
		//public  bool              ClientSideLighting { get; set; } = true;

		private BedrockClient Client { get; }
		public BlobCache Cache { get; }

		private BufferBlock<BufferItem> _dataQueue;

		private ConcurrentObservableDictionary<BlockCoordinates, bool> _pendingRequests = new ConcurrentObservableDictionary<BlockCoordinates, bool>();
		public ChunkProcessor(BedrockClient client, CancellationToken cancellationToken, BlobCache blobCache)
		{
			Client = client;
			CancellationToken = cancellationToken;
			Cache = blobCache;

			Instance = this;

			var blockOptions = new ExecutionDataflowBlockOptions
			{
				CancellationToken = cancellationToken,
				EnsureOrdered = false,
				NameFormat = "Chunker: {0}-{1}",
				MaxDegreeOfParallelism = 1
			};

			_dataQueue = new BufferBlock<BufferItem>(blockOptions);

			var handleBufferItemBlock = new ActionBlock<BufferItem>(HandleBufferItem, blockOptions);
			var linkOptions = new DataflowLinkOptions { PropagateCompletion = true };
			_dataQueue.LinkTo(handleBufferItemBlock, linkOptions);
			
			_pendingRequests.CollectionChanged += PendingRequestsOnCollectionChanged;
		}

		public void Init(World world)
		{
			world.ChunkManager.OnChunkRemoved += OnChunkRemoved;
		}

		private void OnChunkRemoved(object sender, ChunkRemovedEventArgs e)
		{
			BlockCoordinates[] matches;

			lock (_missingLock)
			{
				matches = _missing.Where(x => x.X == e.Position.X && x.Z == e.Position.Z).ToArray();
			}

			foreach (var match in matches)
			{
				if (_pendingRequests.TryGetValue(match, out var m))
				{
					_pendingRequests.TryRemove(match, out _);
				}
			}
		}

		private void PendingRequestsOnCollectionChanged(object sender, DictionaryChangedEventArgs<BlockCoordinates, bool> e)
		{
			if (e.Action == NotifyCollectionChangedAction.Remove)
			{
				var chunkCoordinates = new ChunkCoordinates(e.Key.X, e.Key.Z);

				if (!_pendingRequests.Any(x => x.Key.X == e.Key.X && x.Key.Z == e.Key.Z))
				{
					var chunkManager = Client?.World?.ChunkManager;

					if (chunkManager == null || !chunkManager.TryGetChunk(chunkCoordinates, out var chunk))
						return;
					
					chunk.CalculateHeight();
					chunkManager.ScheduleChunkUpdate(chunkCoordinates, ScheduleType.Full);
				}
			}
		}

		private void HandleBufferItem(BufferItem item)
		{
			if (item.ChunkData != null)
			{
				HandleChunkData(item.ChunkData);

				return;
			}

			if (item.SubChunkData != null)
			{
				HandleSubChunkData(item.SubChunkData);	
				return;
			}

			HandleKv(item.KeyValuePair);
		}

		private void HandleKv(KeyValuePair<ulong, byte[]> kv)
		{
			ulong hash = kv.Key;
			byte[] data = kv.Value;

			if (!Cache.Contains(hash))
				Cache.TryStore(hash, data);

			var chunks = _futureChunks.Where(c => c.SubChunks.Contains(hash) || c.Biome == hash).ToArray();

			foreach (CachedChunk chunk in chunks)
			{
				chunk.TryBuild(Client, this);
			}
		}

		private object _missingLock = new object();
		private List<BlockCoordinates> _missing = new List<BlockCoordinates>();

		private int _pendingCount = 0;
		public void RequestMissing()
		{
			List<BlockCoordinates> missing;

			lock (_missingLock)
			{
				missing = _missing.ToList();
			}

			var basePosition = new ChunkCoordinates(Client.World.Player.KnownPosition);

			foreach (var bc in missing.Where(x => !_pendingRequests.ContainsKey(x))
				        .OrderBy(x => basePosition.DistanceTo(new ChunkCoordinates(x.X, x.Z))))
			{
				McpeSubChunkRequestPacket subChunkRequestPacket = McpeSubChunkRequestPacket.CreateObject();
				subChunkRequestPacket.dimension = (int) Client.World.Dimension;

				subChunkRequestPacket.basePosition =
					new MiNET.Utils.Vectors.BlockCoordinates(basePosition.X, 0, basePosition.Z);

				List<SubChunkPositionOffset> offsets = new List<SubChunkPositionOffset>();

				//foreach (var bc in group)
				{
					var cc = basePosition - new ChunkCoordinates(bc.X, bc.Z);
					
					if (cc.X < sbyte.MinValue || cc.X > sbyte.MaxValue)
						continue;

					if (cc.Z < sbyte.MinValue || cc.Z > sbyte.MaxValue)
						continue;

					//	for (uint y = enqueuedChunk.SubChunkCount; y > 0; y--)
					{
						offsets.Add(
							new SubChunkPositionOffset()
							{
								XOffset = (sbyte) cc.X, YOffset = (sbyte) bc.Y, ZOffset = (sbyte) cc.Z
							});
					}

					//_missing.Remove(bc);
					_pendingRequests.TryAdd(bc, true);
				}

				subChunkRequestPacket.offsets = offsets.ToArray();

				Client.SendPacket(subChunkRequestPacket);
			}
		}

		private void HandleChunkData(ChunkData enqueuedChunk)
		{
			var chunkManager = Client?.World?.ChunkManager;

			if (chunkManager == null)
				return;

			if (enqueuedChunk.CacheEnabled)
			{
				HandleChunkCachePacket(enqueuedChunk);

				return;
			}

			var handledChunk = HandleChunk(enqueuedChunk);

			if (handledChunk != null)
			{
				if (enqueuedChunk.SubChunkRequestMode == SubChunkRequestMode.SubChunkRequestModeLimited)
				{
					if (enqueuedChunk.SubChunkCount > 0)
					{
						handledChunk.IsNew = false;
						chunkManager.AddChunk(handledChunk, new ChunkCoordinates(enqueuedChunk.X, enqueuedChunk.Z), false);
						
						for (uint y = enqueuedChunk.SubChunkCount; y > 0; y--)
						{
							lock (_missingLock)
							{
								var bc = new BlockCoordinates(enqueuedChunk.X, (int) y, enqueuedChunk.Z);
								if (!_missing.Contains(bc))
									_missing.Add(bc);
							}
						}
						//RequestMissing();
					}
					else
					{
						handledChunk?.Dispose();
					}
				}
				else if (enqueuedChunk.SubChunkRequestMode == SubChunkRequestMode.SubChunkRequestModeLegacy)
				{
					chunkManager.AddChunk(handledChunk, new ChunkCoordinates(handledChunk.X, handledChunk.Z), true);
				}
				else
				{
					handledChunk?.Dispose();
				}
			}
		}
		
		private void HandleSubChunkData(SubChunkData data)
		{
			var bc = new BlockCoordinates(data.X, data.Y, data.Z);
			var cc = new ChunkCoordinates(data.X, data.Z);

			bool keepPending = false;
			try
			{
				var chunkManager = Client?.World?.ChunkManager;

				if (chunkManager == null)
					return;

				if (chunkManager.TryGetChunk(cc, out var chunk))
				{
					switch (data.Result)
					{
						case SubChunkRequestResult.NoSuchChunk:
							keepPending = true;

							break;

						case SubChunkRequestResult.YIndexOutOfBounds:
							Log.Warn($"Got out of bounds! Position={bc}");

							break;
					}

					if (!keepPending)
					{
						lock (_missingLock)
						{
							_missing.Remove(bc);
						}
					}
					
					if (data.BlobHash != null)
					{
						Log.Info($"Blobhash!");

						return;
					}

					if (data.Data == null)
					{
						Log.Error($"Invalid subchunk data!");

						return;
					}

					BedrockChunkSection section = null;
					int subChunkIndex = int.MaxValue;

					using (MemoryStream ms = new MemoryStream(data.Data))
					{
						section = BedrockChunkSection.Read(this, ms, ref subChunkIndex, WorldSettings);
					}

					var sY = data.Y - (WorldSettings.MinY >> 4);

					if (section != null)
					{
						chunk[sY] = section;
						chunk.CalculateHeight();
					}
				}
			}
			finally
			{
				_pendingRequests[bc] = !keepPending;
				_pendingRequests.TryRemove(bc, out _);
			}
		}
		
		public void Clear()
		{
			lock (_missingLock)
			{
				if (_missing.Count > 0)
					_missing.Clear();
			}

			_pendingRequests.Clear();
			
			if (_dataQueue.Count > 0)
			{
				_dataQueue.TryReceiveAll(out _);
			}
		}

		public void HandleChunkData(
			bool cacheEnabled,
			SubChunkRequestMode subChunkRequestMode,
			ulong[] blobs,
			uint subChunkCount,
			byte[] chunkData,
			int cx,
			int cz)
		{
			if (CancellationToken.IsCancellationRequested || subChunkCount < 1)
				return;

			var value = new ChunkData(cacheEnabled, subChunkRequestMode, blobs, subChunkCount, chunkData, cx, cz);

			_dataQueue.Post(new BufferItem(default, value, null));
		}

		public void HandleSubChunkData(SubChunkRequestResult result, ulong? blobHash, byte[] data, int cx, int cy, int cz)
		{
			if (CancellationToken.IsCancellationRequested)
				return;

			var value = new SubChunkData(result, blobHash, data, cx, cy, cz);
			_dataQueue.Post(new BufferItem(default, null, value));
		}

		public ThreadSafeList<CachedChunk> _futureChunks = new ThreadSafeList<CachedChunk>();

		public ConcurrentDictionary<BlockCoordinates, NbtCompound> _futureBlockEntities =
			new ConcurrentDictionary<BlockCoordinates, NbtCompound>();

		public void HandleClientCacheMissResponse(McpeClientCacheMissResponse message)
		{
			foreach (KeyValuePair<ulong, byte[]> kv in message.blobs)
			{
				_dataQueue.Post(new BufferItem(kv, null, null));
			}
		}

		private void HandleChunkCachePacket(ChunkData chunkData)
		{
			var chunk = new CachedChunk(chunkData.X, chunkData.Z);
			chunk.SubChunks = new ulong[chunkData.SubChunkCount];
			chunk.Sections = new byte[chunkData.SubChunkCount][];

			var hits = new List<ulong>();
			var misses = new List<ulong>();

			ulong biomeHash = chunkData.Blobs.Last();
			chunk.Biome = biomeHash;

			if (Cache.Contains(biomeHash))
			{
				hits.Add(biomeHash);
			}
			else
			{
				misses.Add(biomeHash);
			}

			for (int i = 0; i < chunkData.SubChunkCount; i++)
			{
				ulong hash = chunkData.Blobs[i];
				chunk.SubChunks[i] = hash;

				if (Cache.TryGet(hash, out byte[] subChunkData))
				{
					chunk.Sections[i] = subChunkData;
					hits.Add(hash);
					//chunk.SubChunkCount--;
				}
				else
				{
					chunk.Sections[i] = null;
					misses.Add(hash);
				}
			}

			if (misses.Count > 0)
				_futureChunks.TryAdd(chunk);

			var status = McpeClientCacheBlobStatus.CreateObject();
			status.hashHits = hits.ToArray();
			status.hashMisses = misses.ToArray();
			Client.SendPacket(status);

			if (chunk.IsComplete)
			{
				chunk.TryBuild(Client, this);
			}
		}

		public WorldSettings WorldSettings { get; set; } = new WorldSettings(384, -64);
		private BedrockChunkColumn HandleChunk(ChunkData chunkData)
		{
			if (chunkData.SubChunkRequestMode != SubChunkRequestMode.SubChunkRequestModeLegacy)
			{
				return new BedrockChunkColumn(chunkData.X, chunkData.Z, WorldSettings);
			}
			
			if (chunkData.SubChunkCount == 0)
				return null;
			
			try
			{
				var chunkColumn = BedrockChunkColumn.ReadFrom(this, chunkData.Data, chunkData.X, chunkData.Z, chunkData.SubChunkCount, WorldSettings);
				
				return chunkColumn;
			}
			catch (Exception ex)
			{
				Log.Error($"Exception in chunk loading: {ex.ToString()}");

				return null;
			}
			finally { }
		}

		public Biome GetBiome(uint id)
		{
			return BiomeUtils.GetBiome(id);
		}

		public BlockState GetBlockState(uint p)
		{
			return BlockFactory.GetBlockState(p);
		}

		public void Dispose()
		{
			_dataQueue.Complete();
			//   _actionQueue?.Clear();
			//	_actionQueue = null;

			//_blobQueue?.Clear();
			//_blobQueue = null;

			_convertedStates?.Clear();
			_convertedStates = null;

			if (Instance != this)
			{
				//BackgroundWorker?.Dispose();
				//BackgroundWorker = null;
			}
		}

		private class ChunkData
		{
			public readonly bool CacheEnabled;
			public readonly SubChunkRequestMode SubChunkRequestMode;
			public readonly ulong[] Blobs;
			public readonly uint SubChunkCount;
			public readonly byte[] Data;
			public readonly int X;
			public readonly int Z;

			public ChunkData(bool cacheEnabled,SubChunkRequestMode subChunkRequestMode, ulong[] blobs, uint subChunkCount, byte[] chunkData, int cx, int cz)
			{
				CacheEnabled = cacheEnabled;
				SubChunkRequestMode = subChunkRequestMode;
				Blobs = blobs;
				SubChunkCount = subChunkCount;
				Data = chunkData;
				X = cx;
				Z = cz;
			}
		}

		private class SubChunkData
		{
			public SubChunkRequestResult Result { get; }
			public ulong? BlobHash { get; }
			public byte[] Data { get; }
			public int X { get; }
			public int Y { get; }
			public int Z { get; }

			public SubChunkData(SubChunkRequestResult result, ulong? blobHash, byte[] data, int cx, int cy, int cz)
			{
				Result = result;
				BlobHash = blobHash;
				Data = data;
				X = cx;
				Y = cy;
				Z = cz;
			}
		}
	}
}