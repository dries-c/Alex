using System;
using Alex.Common.Graphics;
using Alex.Common.Utils;
using Alex.Graphics.Models.Entity;
using Alex.ResourcePackLib.Json.Models;
using Alex.ResourcePackLib.Json.Models.Items;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Alex.Graphics.Models.Items
{
	public class EntityItemRenderer : IItemRenderer, IDisposable
	{
		private readonly EntityModelRenderer _entityRenderer;
		private readonly Texture2D _texture;

		public EntityItemRenderer(string name, EntityModelRenderer entityRenderer, Texture2D texture)
		{
			Name = name;
			_entityRenderer = entityRenderer;
			_texture = texture;
		}
		
		private DisplayPosition _displayPosition = ResourcePackLib.Json.Models.Items.DisplayPosition.Undefined;

		public DisplayPosition DisplayPosition
		{
			get => _displayPosition;
			set
			{
				var oldDisplayPosition = _displayPosition;
				_displayPosition = value;

				//if (oldDisplayPosition != _displayPosition)
				{
					UpdateDisplay();
				}
			}
		}

		public DisplayElement ActiveDisplayItem { get; set; } = DisplayElement.Default;

		private void UpdateDisplay()
		{
			try
			{
				if (Model.Display.TryGetValue(DisplayPositionHelper.ToString(_displayPosition), out var display))
				{
					ActiveDisplayItem = display;

					return;
				}
			}
			catch(ArgumentOutOfRangeException)
			{
                
			}
            
			//ActiveDisplayItem = DisplayElement.Default;
		}
		
		/// <inheritdoc />
		public IAttached Parent { get; set; }
		public Vector3 Scale { get; set; } = Vector3.One;
		
		protected Matrix GetWorldMatrix(DisplayElement activeDisplayItem, Matrix characterMatrix)
        {
            if ((DisplayPosition & ResourcePackLib.Json.Models.Items.DisplayPosition.Ground) != 0)
            {
                return Matrix.CreateScale(activeDisplayItem.Scale * Scale)
                       * MatrixHelper.CreateRotationDegrees(activeDisplayItem.Rotation)
                       * Matrix.CreateTranslation(activeDisplayItem.Translation)
                       * characterMatrix;
            }
            
            if ((DisplayPosition & ResourcePackLib.Json.Models.Items.DisplayPosition.FirstPerson) != 0)
            {
                var translate = activeDisplayItem.Translation;
                return Matrix.CreateScale(activeDisplayItem.Scale * (Scale / 2f))
                       * MatrixHelper.CreateRotationDegrees(new Vector3(-67.5f, 0f, 0f))
                       * MatrixHelper.CreateRotationDegrees(activeDisplayItem.Rotation)
                       * Matrix.CreateTranslation(new Vector3(translate.X + 4f, translate.Y + 18f, translate.Z - 2f))
                       * characterMatrix;
            }
            
            if ((DisplayPosition & ResourcePackLib.Json.Models.Items.DisplayPosition.ThirdPerson) != 0)
            {
                var translate = activeDisplayItem.Translation;
                return Matrix.CreateScale(activeDisplayItem.Scale * Scale)
                       * MatrixHelper.CreateRotationDegrees(new Vector3(-67.5f, 0f, 0f))
                       * MatrixHelper.CreateRotationDegrees(activeDisplayItem.Rotation)
                       * Matrix.CreateTranslation(new Vector3(translate.X + 2f, translate.Y + (8f), translate.Z - 2f))
                       * characterMatrix;
            }
            
            if ((DisplayPosition & ResourcePackLib.Json.Models.Items.DisplayPosition.Gui) != 0)
            {
                return Matrix.CreateScale(activeDisplayItem.Scale)
                       * MatrixHelper.CreateRotationDegrees(new Vector3(25f, 45f, 0f))
                       * Matrix.CreateTranslation(activeDisplayItem.Translation) 
                       * Matrix.CreateTranslation(new Vector3(0f, 0.25f, 0f))
                       * characterMatrix;
            }

            return characterMatrix;
        }

		/// <inheritdoc />
		public int Render(IRenderArgs args, Microsoft.Xna.Framework.Graphics.Effect effect, Matrix worldMatrix)
		{
			return _entityRenderer.Render(args, true, effect, GetWorldMatrix(ActiveDisplayItem, worldMatrix));
		}

		/// <inheritdoc />
		public void Update(IUpdateArgs args)
		{
			_entityRenderer.Update(args);
		}

		/// <inheritdoc />
		public string Name { get; }

		/// <inheritdoc />
		public void AddChild(IAttached modelBone)
		{
			throw new System.NotImplementedException();
		}

		/// <inheritdoc />
		public void Remove(IAttached modelBone)
		{
			throw new System.NotImplementedException();
		}

		/// <inheritdoc />
		public IAttached Clone()
		{
			return CloneItemRenderer();
		}

		/// <inheritdoc />
		public ResourcePackModelBase Model { get; }

		/// <inheritdoc />
		public bool Cache(ResourceManager pack)
		{
			return true;
		}

		/// <inheritdoc />
		public IItemRenderer CloneItemRenderer()
		{
			return new EntityItemRenderer(Name, _entityRenderer, _texture);
		}


		/// <inheritdoc />
		public void Dispose()
		{
			_entityRenderer?.Dispose();
			_texture?.Dispose();
		}
	}
}