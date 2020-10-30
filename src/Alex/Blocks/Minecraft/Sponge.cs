namespace Alex.Blocks.Minecraft
{
	public class Sponge : Block
	{
		public Sponge() : base()
		{
			Solid = true;
			Transparent = false;
			IsReplacible = false;

			BlockMaterial = Material.Sponge;
			Hardness = 0.6f;
		}
	}
}
