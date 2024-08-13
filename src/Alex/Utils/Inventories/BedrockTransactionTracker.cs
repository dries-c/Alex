using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Alex.Items;
using Alex.Net.Bedrock;
using MiNET.Net;
using MiNET.Utils;
using NLog;
using RocketUI.Input;

namespace Alex.Utils.Inventories
{
	public class CursorInfo
	{
		public Item Item { get; set; }
		public byte InventoryId { get; set; }
		public byte Slot { get; set; }

		public CursorInfo(Item item, byte inventoryId, byte slot)
		{
			Item = item;
			InventoryId = inventoryId;
			Slot = slot;
		}
	}

	public class BedrockTransactionTracker : IInventoryTransactionHandler
	{
		private static readonly Logger Log = LogManager.GetCurrentClassLogger(typeof(BedrockTransactionTracker));

		private ItemStackActionList ActionList { get; set; }


		private bool HoldingShift { get; set; } = false;
		private BedrockClient Client { get; }

		private ConcurrentDictionary<int, ItemStackRequests> _pendingRequests =
			new ConcurrentDictionary<int, ItemStackRequests>();

		public BedrockTransactionTracker(BedrockClient client)
		{
			Client = client;
			ActionList = new ItemStackActionList();
		}

		private int _requestId = 1;

		private int GetRequestId()
		{
			return Interlocked.Increment(ref _requestId);
		}

		private void SendRequests()
		{
			var actions = ActionList;

			if (actions.Count == 0)
				return;

			actions.RequestId = GetRequestId();

			ActionList = new ItemStackActionList();

			McpeItemStackRequest stackRequest = McpeItemStackRequest.CreateObject();
			stackRequest.requests = new ItemStackRequests() { actions };

			_pendingRequests.TryAdd(actions.RequestId, stackRequest.requests);
			Client.SendPacket(stackRequest);

			Log.Info($"Sent request: {actions.RequestId}");
		}

		/// <inheritdoc />
		public void DialogClosed()
		{
			//SendActions();
		}

		private Item SetCursor(Item cursor, byte inventoryId, byte slotId, MouseButton button = MouseButton.Left)
		{
			var currentCursorItem = Client.World.Player.Inventory.UiInventory.Cursor;
			Client.World.Player.Inventory.UiInventory.SetCursor(cursor ?? new ItemAir(), false, slotId, button);

			return currentCursorItem;
		}

		/// <inheritdoc />
		public void SlotClicked(MouseButton button, byte inventoryId, byte slotId)
		{
			var clickedItem = GetContainerItem(inventoryId, slotId);

			if (clickedItem == null)
			{
				Log.Warn($"Unknown inventory/slot! InventoryId={inventoryId} Slot={slotId}");

				return;
			}

			if (button == MouseButton.Left)
			{
				var previousCursorItem = SetCursor(clickedItem, inventoryId, slotId, button);
				SetContainerItem(inventoryId, slotId, previousCursorItem);

				if (Client.ServerAuthoritiveInventory)
				{
					ActionList.Add(
						new SwapAction()
						{
							Source = new StackRequestSlotInfo()
							{
								Slot = slotId, ContainerId = (ContainerId) inventoryId, StackNetworkId = clickedItem.StackID
							},
							Destination = new StackRequestSlotInfo()
							{
								ContainerId = ContainerId.Cursor,
								Slot = 0,
								StackNetworkId = previousCursorItem?.StackID ?? 0
							}
						});

					SendRequests();
				}
				else
				{
					var newInventoryId = (byte)(inventoryId == (int)ContainerId.Inventory ? 0 : inventoryId);

					//if (newInventoryId == 0 && slotId >= 36 && slotId <= 39)
					//{
					//	newInventoryId = 120;
					//	slotId = (byte) (39 - slotId);
					//}

					var clickedMiNETItem = BedrockClient.GetMiNETItem(clickedItem);
					var previousCursor = BedrockClient.GetMiNETItem(previousCursorItem);

					McpeInventoryTransaction setCursorTransaction = McpeInventoryTransaction.CreateObject();

					setCursorTransaction.transaction = new NormalTransaction()
					{
						HasNetworkIds = true, TransactionRecords = new List<TransactionRecord>() { }
					};

					//Cursor already had an item & clicked slot already has an item.
					//Hence, we are swapping the items
					if (!previousCursorItem.IsAir() && !clickedItem.IsAir())
					{
						//Replace the item in the slot with the current cursor item
						setCursorTransaction.transaction.TransactionRecords.Add(
							new ContainerTransactionRecord()
							{
								InventoryId = newInventoryId,
								Slot = slotId,
								NewItem = previousCursor,
								OldItem = clickedMiNETItem,
								StackNetworkId = previousCursorItem.StackID
							});

						//Set the cursor to the clicked slot's item.
						setCursorTransaction.transaction.TransactionRecords.Add(
							new ContainerTransactionRecord()
							{
								InventoryId = 124,
								Slot = 0,
								NewItem = clickedMiNETItem,
								OldItem = previousCursor,
								StackNetworkId = clickedItem.StackID
							});
					}
					//Cursor is empty, clicked slot is not, hence we are picking an item up.
					else if (previousCursorItem.IsAir() && !clickedItem.IsAir())
					{
						setCursorTransaction.transaction.TransactionRecords.Add(
							new ContainerTransactionRecord()
							{
								InventoryId = newInventoryId,
								Slot = slotId,
								NewItem = previousCursor,
								OldItem = clickedMiNETItem,
								StackNetworkId = previousCursorItem.StackID
							});

						setCursorTransaction.transaction.TransactionRecords.Add(
							new ContainerTransactionRecord()
							{
								InventoryId = 124,
								Slot = 0,
								NewItem = clickedMiNETItem,
								OldItem = previousCursor,
								StackNetworkId = clickedItem.StackID
							});
					}
					//The cursor has an item but clicked slot does not, drop the cursor item into the slot.
					else if (!previousCursorItem.IsAir() && clickedItem.IsAir())
					{
						setCursorTransaction.transaction.TransactionRecords.Add(
							new ContainerTransactionRecord()
							{
								InventoryId = 124,
								Slot = 0,
								NewItem = clickedMiNETItem,
								OldItem = previousCursor,
								StackNetworkId = clickedItem.StackID
							});

						setCursorTransaction.transaction.TransactionRecords.Add(
							new ContainerTransactionRecord()
							{
								InventoryId = newInventoryId,
								Slot = slotId,
								NewItem = previousCursor,
								OldItem = clickedMiNETItem,
								StackNetworkId = previousCursorItem.StackID
							});
					}

					Client.SendPacket(setCursorTransaction);
				}
			}
		}

		/// <inheritdoc />
		public void SlotHover(byte inventoryId, byte slotId) { }

		private Item GetContainerItem(byte containerId, byte slot)
		{
			//if (_player.UsingAnvil && containerId < 3) containerId = 13;

			Item item = null;

			switch (containerId)
			{
				case (byte) ContainerId.CraftingInput: // crafting
					item = Client.World.Player.Inventory.GetCraftingSlot(slot);

					break;

				case (byte) ContainerId.EnchantingInput:
				case (byte) ContainerId.EnchantingMaterial:
				case (byte) ContainerId.LoomInput:
				case (byte) ContainerId.Cursor:
				case (byte) ContainerId.CreatedOutput:
					item = Client.World.Player.Inventory.UiInventory[slot];

					break;

				case (byte) ContainerId.CombinedHotbarAndInventory:
				case (byte) ContainerId.Hotbar:
				case (byte) ContainerId.Inventory:
					item = Client.World.Player.Inventory[slot];

					break;

				case (byte) ContainerId.Offhand:
					item = Client.World.Player.Inventory.OffHand; // _player.Inventory.OffHand;

					break;

				case (byte) ContainerId.Armor:
					item = slot switch
					{
						0 => Client.World.Player.Inventory.Helmet,
						1 => Client.World.Player.Inventory.Chestplate,
						2 => Client.World.Player.Inventory.Leggings,
						3 => Client.World.Player.Inventory.Boots,
						_ => null
					};

					break;

				case (byte) ContainerId.LevelEntity:
					var activeWindow = Client.World.InventoryManager.ActiveWindow;

					if (activeWindow != null)
					{
						item = activeWindow.Inventory[slot];
					}

					//if (_player._openInventory is Inventory inventory) item = inventory.GetSlot((byte) slot);
					break;

				default:
					if (Client.World.InventoryManager.TryGet(containerId, out var inventoryBase))
					{
						item = inventoryBase.Inventory[slot];
					}
					else
					{
						Log.Warn($"(Get) Unknown containerId: {containerId}");
					}

					break;
			}

			return item;
		}

		private void SetContainerItem(int containerId, int slot, Item item)
		{
			//if (_player.UsingAnvil && containerId < 3) containerId = 13;

			switch (containerId)
			{
				case (byte) ContainerId.CraftingInput:
					Client.World.Player.Inventory.SetCraftingSlot(slot, item, false);

					break;

				case (byte) ContainerId.EnchantingInput:
				case (byte) ContainerId.EnchantingMaterial:
				case (byte) ContainerId.LoomInput:
				case (byte) ContainerId.Cursor:
				case (byte) ContainerId.CreatedOutput:
					Client.World.Player.Inventory.UiInventory.SetSlot(slot, item, false);

					//Client.World.Player.Inventory.UiInventory[slot] = item;
					break;

				case (byte) ContainerId.CombinedHotbarAndInventory:
				case (byte) ContainerId.Hotbar:
				case (byte) ContainerId.Inventory:
					Client.World.Player.Inventory.SetSlot(slot, item, false);

					//Client.World.Player.Inventory[slot] = item;//.SetSlot(slot, item, true);
					break;

				case (byte) ContainerId.Offhand:
					Client.World.Player.Inventory.OffHand = item;

					break;

				case (byte) ContainerId.Armor:
					switch (slot)
					{
						case 0:
							Client.World.Player.Inventory.Helmet = item;

							break;

						case 1:
							Client.World.Player.Inventory.Chestplate = item;

							break;

						case 2:
							Client.World.Player.Inventory.Leggings = item;

							break;

						case 3:
							Client.World.Player.Inventory.Boots = item;

							break;
					}

					break;

				case (byte) ContainerId.LevelEntity:
					var activeWindow = Client.World.InventoryManager.ActiveWindow;

					if (activeWindow != null)
					{
						activeWindow.Inventory.SetSlot(slot, item, false);
					}

					//if (_player._openInventory is Inventory inventory) inventory.SetSlot(_player, (byte) slot, item);
					break;

				default:
					if (Client.World.InventoryManager.TryGet(containerId, out var inventoryBase))
					{
						inventoryBase.Inventory.SetSlot(slot, item, false);
					}
					else
					{
						Log.Warn($"(Set) Unknown containerId: {containerId}");
					}

					break;
			}
		}

		private bool TryGetContainer(ContainerId containerId, out InventoryBase inventory)
		{
			switch (containerId)
			{
				case ContainerId.CraftingInput:
				case ContainerId.EnchantingInput:
				case ContainerId.EnchantingMaterial:
				case ContainerId.LoomInput:
				case ContainerId.Cursor:
				case ContainerId.CreatedOutput:
					inventory = Client.World.Player.Inventory.UiInventory;

					return true;

				case ContainerId.CombinedHotbarAndInventory:
				case ContainerId.Hotbar:
				case ContainerId.Inventory:
					inventory = Client.World.Player.Inventory;

					return true;

				case ContainerId.Offhand:
					inventory = Client.World.Player.Inventory;

					return true;

				case ContainerId.Armor:
					inventory = Client.World.Player.Inventory;

					return true;

				case ContainerId.LevelEntity: // chest/container
					var activeWindow = Client.World.InventoryManager.ActiveWindow;

					if (activeWindow != null)
					{
						inventory = activeWindow.Inventory;

						return true;
					}

					//if (_player._openInventory is Inventory inventory) inventory.SetSlot(_player, (byte) slot, item);
					break;

				case ContainerId.AnvilInput:
					inventory = Client.World.Player.Inventory;

					return true;
			}

			if (Client.World.InventoryManager.TryGet((int) containerId, out var inventoryBase))
			{
				inventory = inventoryBase.Inventory;

				return true;
			}

			Log.Warn($"Unknown container id: {containerId}");
			inventory = null;

			return false;
		}

		public void HandleResponse(ItemStackResponses responses)
		{
			if (responses.Count == 0)
			{
				Log.Warn($"Empty response");

				return;
			}

			foreach (var response in responses)
			{
				if (_pendingRequests.TryRemove(response.RequestId, out var request))
				{
					foreach (var task in response.ResponseContainerInfos)
					{
						if (TryGetContainer(task.ContainerId, out var inventory))
						{
							foreach (var slot in task.Slots)
							{
								var invSlot = inventory[slot.Slot];

								if (invSlot != null)
								{
									invSlot.Count = slot.Count;
									invSlot.StackID = slot.StackNetworkId;
								}
							}
						}
					}

					if (response.Result != StackResponseStatus.Ok)
					{
						//Revert action
						Log.Warn($"Action {response.RequestId} was not ok, we should revert?");
					}
					else
					{
						Log.Warn($"Action {response.RequestId} was ok!");
					}
				}
				else
				{
					Log.Warn($"Unknown requestId: {response.RequestId}");
				}
			}
		}
	}
}