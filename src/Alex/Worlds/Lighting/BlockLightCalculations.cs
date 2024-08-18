using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using Alex.Common.Utils.Vectors;
using Alex.Worlds.Abstraction;
using Alex.Worlds.Chunks;
using NLog;

namespace Alex.Worlds.Lighting
{
	public class BlockLightCalculations : IDisposable
	{
		private static readonly ILogger Log = LogManager.GetCurrentClassLogger(typeof(BlockLightCalculations));
		private ConcurrentQueue<BlockCoordinates> Queue { get; }
		private IBlockAccess World { get; }
		private CancellationToken CancellationToken { get; }

		public BlockLightCalculations(IBlockAccess level, CancellationToken cancellationToken)
		{
			World = level;
			CancellationToken = cancellationToken;
			Queue = new ConcurrentQueue<BlockCoordinates>();
		}

		public void RecalculateChunk(ChunkColumn chunk)
		{
			for (int i = 0; i < chunk.ChunkSections; i++)
			{
				//section?.ResetLight(true, false);
				chunk[i]?.RemoveInvalidBlocks();
			}

			var chunkpos = new BlockCoordinates(chunk.X << 4, 0, chunk.Z << 4);

			foreach (var lightsource in chunk.GetLightSources().OrderBy(x => x.Y))
			{
				Queue.Enqueue(chunkpos + lightsource);
			}
		}

		internal static readonly BlockCoordinates[] Adjacents = new[]
		{
			new BlockCoordinates(1, 0, 0), new BlockCoordinates(-1, 0, 0), new BlockCoordinates(0, 1, 0),
			new BlockCoordinates(0, -1, 0), new BlockCoordinates(0, 0, 1), new BlockCoordinates(0, 0, -1),
		};

		public int Execute()
		{
			int count = 0;

			while (Queue.TryDequeue(out var coords) && !CancellationToken.IsCancellationRequested)
			{
				var cc = new ChunkCoordinates(coords);
				var c = World.GetChunk(cc);

				if (c != null)
				{
					var lightLevel = c.GetBlocklight(coords.X & 0xf, coords.Y, coords.Z & 0xf);

					foreach (var offset in Adjacents)
					{
						Test(c, cc, coords + offset, lightLevel);
					}

					count++;
				}
			}

			return count;
		}

		public void Enqueue(BlockCoordinates coordinates)
		{
			Queue.Enqueue(coordinates);
		}

		private void Test(ChunkColumn column, ChunkCoordinates chunkCoord, BlockCoordinates target, int lightLevel)
		{
			bool isOtherChunk = false;
			var newChunkCoord = (ChunkCoordinates)target;

			if (chunkCoord.X != newChunkCoord.X || chunkCoord.Z != newChunkCoord.Z)
			{
				isOtherChunk = true;
				column = World.GetChunk(newChunkCoord);
			}

			//var gotBlockLight = World.TryGetBlockLight(target, out var currentLightLevel);
			if (isOtherChunk && column == null)
			{
				//Queue.Enqueue(target);
				return;
			}

			var currentLightLevel = column.GetBlocklight(target.X & 0xf, target.Y, target.Z & 0xf);

			if (currentLightLevel + 2 <= lightLevel)
			{
				var blockState = column.GetBlockState(target.X & 0xf, target.Y, target.Z & 0xf);
				var block = blockState.Block;
				if (block == null) return;
				if ((!block.Solid || block.Transparent))
				{
					SetLightLevel(column, target, (lightLevel - block.Diffusion));
					Queue.Enqueue(target);
				}
			}
		}

		private void SetLightLevel(ChunkColumn column, BlockCoordinates coord, int lightLevel)
		{
			column?.SetBlocklight(coord.X & 0xf, coord.Y, coord.Z & 0xf, (byte)Math.Clamp(lightLevel, 0, 15));
			column?.ScheduleBlockUpdate(coord.X & 0xf, coord.Y, coord.Z & 0xf);
			//if (World.GetBlockLight(coord) + 2 <= lightLevel)
			//	{
			//World.SetBlockLight(coord, (byte) Math.Clamp(lightLevel, 0, 15));
			//	Enqueue(coord);
			//}
		}

		/// <inheritdoc />
		public void Dispose()
		{
			//Queue.Clear();
			//ChunkQueues.Clear();
			Queue.Clear();
		}
	}
}