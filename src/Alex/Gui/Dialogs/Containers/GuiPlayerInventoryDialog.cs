﻿using Alex.Common.Gui.Graphics;
using Alex.Common.Utils.Vectors;
using Alex.Entities;
using Alex.Gui.Elements.Context3D;
using Alex.Gui.Elements.Inventory;
using Alex.Items;
using Alex.Utils.Inventories;
using Microsoft.Xna.Framework;
using MiNET.Utils;
using NLog;
using RocketUI;

namespace Alex.Gui.Dialogs.Containers
{
	public class GuiPlayerInventoryDialog : GuiInventoryBase
	{
		private static readonly ILogger Log = LogManager.GetCurrentClassLogger();

		protected Player Player { get; }


		private readonly GuiEntityModelView _playerEntityModelView;

		private InventoryContainerItem CraftingOutput { get; }

		public GuiPlayerInventoryDialog(Player player, Inventory inventory) : base(
			inventory, AlexGuiTextures.InventoryPlayerBackground, 176, 166)
		{
			Player = player;

			// Subscribe to events

			if (player != null)
			{
				//    var modelRenderer = player.ModelRenderer;

				var mob = new RemotePlayer(player.Level, null, skin: null);
				//  mob.Skin = player.Skin;
				mob.ModelRenderer = player.ModelRenderer;
				mob.Texture = player.Texture;
				mob.SetInventory(player.Inventory);

				//   mob.RenderLocation = mob.KnownPosition = new PlayerLocation(0, 0, 0, 0f, 0f, 0f);

				ContentContainer.AddChild(
					_playerEntityModelView = new GuiEntityModelView(mob)
					{
						Margin = new Thickness(7, 25),
						Width = 49,
						Height = 70,
						Anchor = Alignment.TopLeft,
						AutoSizeMode = AutoSizeMode.None,
						Background = null,
						BackgroundOverlay = null
					});
			}

			Color color = Color.Blue;

			foreach (var slot in AddSlots(8, 84, 9, 27, inventory.InventoryOffset, inventory.InventoryId))
			{
				//   slot.HighlightedBackground = new Microsoft.Xna.Framework.Color(color, 0.5f);
				slot.Item = Inventory[slot.InventoryIndex];
			}

			color = Color.Aqua;

			foreach (var slot in AddSlots(8, 142, 9, 9, inventory.HotbarOffset, inventory.InventoryId))
			{
				// slot.HighlightedBackground = new Microsoft.Xna.Framework.Color(color, 0.5f);
				slot.Item = Inventory[slot.InventoryIndex];
			}

			foreach (var slot in AddSlots(8, 8, 1, 4, 0, 120)) //todo: figure out why this is a thing
			{
				var inventoryIndex = slot.InventoryIndex;
				Item item = new ItemAir();

				switch (slot.InventoryIndex)
				{
					case 0:
						item = inventory.Helmet;
						inventoryIndex = inventory.HelmetSlot;

						break;

					case 1:
						item = inventory.Chestplate;
						inventoryIndex = inventory.ChestSlot;

						break;

					case 2:
						item = inventory.Leggings;
						inventoryIndex = inventory.LeggingsSlot;

						break;

					case 3:
						item = inventory.Boots;
						inventoryIndex = inventory.BootsSlot;

						break;
				}

				//  slot.HighlightedBackground = new Microsoft.Xna.Framework.Color(Color.Red, 0.5f);
				slot.Item = item;
				slot.InventoryIndex = inventoryIndex;
			}

			var playerInventory = player.Inventory;

			foreach (var slot in AddSlots(98, 18, 2, 4, 1, (int) ContainerId.CraftingInput))
			{
				slot.Item = playerInventory.GetCraftingSlot(slot.InventoryIndex); // Inventory[slot.InventoryIndex];
				//  slot.HighlightedBackground = new Microsoft.Xna.Framework.Color(Color.Purple, 0.5f);
			}

			CraftingOutput = AddSlot(154, 28, 0, (int) ContainerId.CraftingInput);
			//  CraftingOutput.HighlightedBackground = new Microsoft.Xna.Framework.Color(Color.Purple, 0.5f);

			/*var shieldSlot = new InventoryContainerItem()
			{
			    HighlightedBackground = new Microsoft.Xna.Framework.Color(Color.Orange, 0.5f),
			    Anchor = Alignment.TopLeft,
			    Margin =  new Thickness(61, 76),
			    AutoSizeMode = AutoSizeMode.None,
			    Item = Inventory[40],
			    InventoryIndex = 40
			};
			    
			ContentContainer.AddChild(shieldSlot);*/
		}

		private readonly float _playerViewDepth = -512.0f;
		protected override void OnUpdate(GameTime gameTime)
		{
			base.OnUpdate(gameTime);

			if (_playerEntityModelView?.Entity != null)
			{
				var mousePos = Alex.Instance.GuiManager.FocusManager.CursorPosition;
				var playerPos = _playerEntityModelView.RenderBounds.Center.ToVector2();

				var mouseDelta = (new Vector3(playerPos.X, playerPos.Y, _playerViewDepth)
				                  - new Vector3(mousePos.X, mousePos.Y, 0.0f));

				mouseDelta.Normalize();

				var headYaw = (float)mouseDelta.GetYaw();
				var pitch = (float)mouseDelta.GetPitch();
				var yaw = (float)headYaw;

				_playerEntityModelView.SetEntityRotation(yaw, pitch, headYaw);

				if (Inventory != null && Inventory is Inventory inv)
				{
					//  _playerEntityModelView.Entity.ShowItemInHand = true;

					_playerEntityModelView.Entity.SetInventory(inv); // = Inventory;
					//_playerEntityModelView.Entity.Inventory.MainHand = inv.MainHand;
					// _playerEntityModelView.Entity.Inventory.SelectedSlot = inv.SelectedSlot;
				}
			}
		}

		public override void OnClose()
		{
			base.OnClose();

			if (Inventory is Inventory inv)
			{
				inv.TriggerClosedEvent();
			}
		}

		protected override void OnInit(IGuiRenderer renderer)
		{
			base.OnInit(renderer);
		}
	}
}