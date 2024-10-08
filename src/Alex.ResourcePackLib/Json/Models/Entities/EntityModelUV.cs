using System;
using Alex.Interfaces;
using Newtonsoft.Json;

namespace Alex.ResourcePackLib.Json.Models.Entities
{
	public class EntityModelUV
	{
		[JsonProperty("north")] public EntityModelUVData North { get; set; }

		[JsonProperty("south")] public EntityModelUVData South { get; set; }

		[JsonProperty("east")] public EntityModelUVData East { get; set; }

		[JsonProperty("west")] public EntityModelUVData West { get; set; }

		[JsonProperty("up")] public EntityModelUVData Up { get; set; }

		[JsonProperty("down")] public EntityModelUVData Down { get; set; }

		public EntityModelUV() : this(Primitives.Factory.Vector2Zero, false) { }

		[JsonIgnore] public bool IsCube { get; }

		public EntityModelUV(IVector2 origin, bool isCube = true)
		{
			IsCube = isCube;
			var model = new EntityModelUVData() { Origin = origin };
			Up = Down = North = West = South = East = model;
		}

		public EntityModelUVData GetFace(BlockFace face)
		{
			switch (face)
			{
				case BlockFace.Down:
					return Down;

				case BlockFace.Up:
					return Up;

				case BlockFace.East:
					return East;


				case BlockFace.West:
					return West;

				case BlockFace.North:
					return North;

				case BlockFace.South:
					return South;
			}

			return null;
		}

		public static EntityModelUV FromIVector2(IVector2 vector) {
			return new EntityModelUV(vector);
		}

		public bool IsOutOfBound(IVector2 textureSize)
		{
			if (IsCube)
			{
				return (Down.Origin.Y >= textureSize.Y);
			}

			foreach (BlockFace face in Enum.GetValues(typeof(BlockFace)))
			{
				var f = GetFace(face);

				if (f.Origin.Y >= textureSize.Y)
					return true;
			}

			return false;
		}
	}

	public class EntityModelUVData
	{
		/// <summary>
		/// Specifies the uv origin for the face. For this face, it is the upper-left corner, when looking at the face with y being up.
		/// </summary>
		[JsonProperty("uv")]
		public IVector2 Origin { get; set; }

		/// <summary>
		/// The face maps this many texels from the uv origin. If not specified, the box dimensions are used instead.
		/// </summary>
		[JsonProperty("uv_size")]
		public IVector2 Size { get; set; } = null;

		public EntityModelUVData Offset(IVector2 amount)
		{
			return new EntityModelUVData() { Origin = VectorUtils.Add(Origin, amount), Size = Size };
		}

		public EntityModelUVData WithSize(IVector2 size)
		{
			return new EntityModelUVData() { Origin = Origin, Size = size };
		}

		public EntityModelUVData WithSize(float x, float y)
		{
			return new EntityModelUVData() { Origin = Origin, Size = Primitives.Factory.Vector2(x, y) };
		}

		public EntityModelUVData WithOptionalSize(float x, float y)
		{
			return new EntityModelUVData() { Origin = Origin, Size = Size ?? Primitives.Factory.Vector2(x, y) };
		}
	}
}