using Microsoft.Xna.Framework;

namespace Alex.Graphics.Models.Entity.Animations
{
	public class ResetAnimation : ModelBoneAnimation
	{
		/// <inheritdoc />
		public ResetAnimation(EntityModelRenderer.ModelBone bone) : base(bone)
		{
			
		}

		/// <inheritdoc />
		public override bool IsFinished()
		{
			return true;
		}

		/// <inheritdoc />
		public override void Reset()
		{
			//Bone.Rotation = Bone.Definition.Rotation ?? Vector3.Zero;
			//Bone.Position = Bone.EntityModelBone.;
			//base.Reset();
		}
	}
}