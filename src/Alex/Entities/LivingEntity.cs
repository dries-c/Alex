﻿using Alex.API.Utils;
using Alex.Items;
using Alex.Net;
using Alex.Networking.Java.Packets.Play;
using Alex.Worlds;

namespace Alex.Entities
{
	public class LivingEntity : Entity
	{
		public bool IsLeftHanded { get; set; } = false;
		
		private PhysicsComponent PhysicsComponent { get; }
		/// <inheritdoc />
		public LivingEntity(World level) : base(
			level)
		{
			EntityComponents.Push(PhysicsComponent = new PhysicsComponent(this));
		}

		public Item GetItemInHand(bool mainHand)
		{
			return mainHand ? Inventory.MainHand : Inventory.OffHand;
		}
		
		//TODO: Handle hand animations
		
		/// <inheritdoc />
		protected override void HandleJavaMeta(MetaDataEntry entry)
		{
			base.HandleJavaMeta(entry);

			if (entry.Index == 7 && entry is MetadataByte data)
			{
				bool handActive = (data.Value & 0x01) != 0;

				if (handActive)
				{
					bool offHandActive = (data.Value & 0x02) != 0;
					var item = GetItemInHand(!offHandActive);

					if (item != null)
					{
						if (item is ItemEdible) //Food or drink
						{
							IsEating = true;
						}
						else if (item.ItemType == ItemType.Sword || item.ItemType == ItemType.Shield)
						{
							IsBlocking = true;
						} 
						else if (item.ItemType == ItemType.AnyTool)
						{
							
						}
					}
				}
				else
				{
					IsBlocking = false;
					IsEating = false;
				}
			}
			else if (entry.Index == 8 && entry is MetadataFloat flt)
			{
				HealthManager.Health = flt.Value;
			}
		}
	}
}
