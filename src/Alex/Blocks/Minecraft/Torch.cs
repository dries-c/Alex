namespace Alex.Blocks.Minecraft
{
	public class Torch : Block
	{
		public Torch(bool wallTorch = false) : base()
		{
			Solid = false;
			Transparent = true;
			IsReplacible = false;

			IsFullBlock = false;
			IsFullCube = false;
			
			LightValue = 14;

			BlockMaterial = Material.Circuits;
		}
	}
}
