using System.Collections.Generic;
using Alex.Blocks;
using Alex.Blocks.State;
using Alex.Common.Graphics;
using Alex.Common.Utils.Vectors;
using Alex.Worlds.Abstraction;
using Alex.Worlds.Chunks;
using Microsoft.Xna.Framework;

namespace Alex.Worlds
{
	public class ItemRenderingWorld : IBlockAccess
	{
		private static BlockState Air { get; } = BlockFactory.GetBlockState("minecraft:air");

		private Block Block { get; }

		public ItemRenderingWorld(Block block)
		{
			Block = block;
		}

		public void ResetChunks()
		{
			throw new System.NotImplementedException();
		}

		public void RebuildChunks()
		{
			throw new System.NotImplementedException();
		}

		public void Render(IRenderArgs args)
		{
			throw new System.NotImplementedException();
		}

		public Vector3 GetSpawnPoint()
		{
			throw new System.NotImplementedException();
		}

		public byte GetSkyLight(Vector3 position)
		{
			return 0xf;
		}

		public byte GetSkyLight(float x, float y, float z)
		{
			return 0xf;
		}

		public byte GetSkyLight(int x, int y, int z)
		{
			return 0xf;
		}

		public byte GetBlockLight(Vector3 position)
		{
			return 0;
		}

		public byte GetBlockLight(float x, float y, float z)
		{
			return 0;
		}

		public byte GetBlockLight(int x, int y, int z)
		{
			return 0;
		}

		public Block GetBlock(BlockCoordinates position)
		{
			return Air.Block;
		}

		public Block GetBlock(Vector3 position)
		{
			return Air.Block;
		}

		public Block GetBlock(float x, float y, float z)
		{
			return Air.Block;
		}

		public Block GetBlock(int x, int y, int z)
		{
			return Air.Block;
		}

		public void SetBlock(float x, float y, float z, Block block)
		{
			throw new System.NotImplementedException();
		}

		public void SetBlock(int x, int y, int z, Block block)
		{
			throw new System.NotImplementedException();
		}

		public void SetBlockState(int x, int y, int z, BlockState block)
		{
			throw new System.NotImplementedException();
		}

		public BlockState GetBlockState(int x, int y, int z)
		{
			return Air;
		}

		public BlockState GetBlockState(int x, int y, int z, int storage)
		{
			return Air;
		}

		public IEnumerable<ChunkSection.BlockEntry> GetBlockStates(int positionX, int positionY, int positionZ)
		{
			return new[] { new ChunkSection.BlockEntry(Air, 0) };
		}

		public BlockState GetBlockState(BlockCoordinates coordinates)
		{
			return Air;
		}

		/// <inheritdoc />
		public void SetBlockState(int x,
			int y,
			int z,
			BlockState block,
			int storage,
			BlockUpdatePriority priority = BlockUpdatePriority.High) { }

		/// <inheritdoc />
		public Biome GetBiome(BlockCoordinates coordinates)
		{
			return BiomeUtils.GetBiome(0);
		}

		public int GetBiome(int x, int y, int z)
		{
			return 0;
		}

		public bool HasBlock(int x, int y, int z)
		{
			return true;
		}

		public BlockCoordinates FindBlockPosition(BlockCoordinates coords, out ChunkColumn chunk)
		{
			throw new System.NotImplementedException();
		}

		public ChunkColumn GetChunkColumn(int x, int z)
		{
			throw new System.NotImplementedException();
		}

		public bool IsTransparent(int posX, int posY, int posZ)
		{
			return !Block.Transparent;
		}

		public bool IsSolid(int posX, int posY, int posZ)
		{
			return !Block.Solid;
		}

		public bool IsScheduled(int posX, int posY, int posZ)
		{
			return false;
		}

		public void GetBlockData(int posX, int posY, int posZ, out bool transparent, out bool solid)
		{
			transparent = !Block.Transparent;
			solid = !Block.Solid;
		}

		public ChunkColumn GetChunk(BlockCoordinates coordinates, bool cacheOnly = false)
		{
			throw new System.NotImplementedException();
		}

		public ChunkColumn GetChunk(ChunkCoordinates coordinates, bool cacheOnly = false)
		{
			throw new System.NotImplementedException();
		}

		public void SetSkyLight(BlockCoordinates coordinates, byte skyLight)
		{
			throw new System.NotImplementedException();
		}

		public byte GetSkyLight(BlockCoordinates coordinates)
		{
			return 15;
		}

		public byte GetBlockLight(BlockCoordinates coordinates)
		{
			return 0;
		}

		/// <inheritdoc />
		public void SetBlockLight(BlockCoordinates coordinates, byte blockLight)
		{
			throw new System.NotImplementedException();
		}

		/// <inheritdoc />
		public bool TryGetBlockLight(BlockCoordinates coordinates, out byte blockLight)
		{
			blockLight = 0;

			return true;
		}

		/// <inheritdoc />
		public void GetLight(BlockCoordinates coordinates, out byte blockLight, out byte skyLight)
		{
			skyLight = 0xf;
			blockLight = 0;
		}

		public int GetHeight(BlockCoordinates coordinates)
		{
			throw new System.NotImplementedException();
		}

		public Block GetBlock(BlockCoordinates coord, ChunkColumn tryChunk = null)
		{
			return Air.Block;
		}

		public void SetBlock(Block block,
			bool broadcast = true,
			bool applyPhysics = true,
			bool calculateLight = true,
			ChunkColumn possibleChunk = null)
		{
			throw new System.NotImplementedException();
		}
	}
}