using Alex.Blocks.Materials;
using Alex.Common.Items;

namespace Alex.Blocks.Minecraft
{
	public class DiamondOre : Block
	{
		public DiamondOre() : base()
		{
			Solid = true;
			Transparent = false;

			BlockMaterial = Material.Ore.Clone().SetRequiredTool(ItemType.PickAxe, ItemMaterial.Iron);
		}
	}
}