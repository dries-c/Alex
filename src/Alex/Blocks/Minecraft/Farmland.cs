namespace Alex.Blocks.Minecraft
{
	public class Farmland : Block
	{
		public Farmland() : base()
		{
			Solid = true;
			Transparent = true;
			IsReplacible = false;

			Hardness = 0.6f;
		}
	}
}
