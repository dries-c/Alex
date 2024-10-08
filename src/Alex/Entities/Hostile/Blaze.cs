using Alex.Worlds;
using ConcreteMC.MolangSharp.Attributes;

namespace Alex.Entities.Hostile
{
	public class Blaze : HostileMob
	{
		[MoProperty("is_charged")] public bool IsCharged { get; set; }

		public Blaze(World level) : base(level)
		{
			Height = 1.8;
			Width = 0.6;
		}
	}
}