using System.Linq;
using System.Runtime.CompilerServices;
using Alex.Blocks;
using Alex.Items;
using Alex.Worlds.Singleplayer;
using Newtonsoft.Json;
using NLog;

namespace Alex.Utils
{
	public static class ItemsExtensions
	{
		private static readonly ILogger Log = LogManager.GetCurrentClassLogger();

		private static JsonSerializerSettings SerializerSettings =
			new JsonSerializerSettings() { ReferenceLoopHandling = ReferenceLoopHandling.Ignore };

		public static Item ToAlexItem(this MiNET.Items.Item item, [CallerMemberName] string source = "")
		{
			if (item == null)
				return new ItemAir();

			Item result = null;


			//Itemstate itemState = null;

			//if (ChunkProcessor.Itemstates != null)
			//	itemState = ChunkProcessor.Itemstates.FirstOrDefault(x => x.Id == item.Id);


			//if (itemState == null)
			//	itemState = MiNET.Items.ItemFactory.Itemstates.FirstOrDefault(x => x.Id == item.Id);

			//if (itemState != null)
			//{
			if (ItemFactory.TryGetItem(item.Id, out result))
			{
				//	Log.Info($"{item.Id} = {JsonConvert.SerializeObject(itemState, SerializerSettings)} || {JsonConvert.SerializeObject(result, SerializerSettings)}");
			}
			else if (result == null && item.LegacyId < 256 && item.LegacyId >= 0) //Block
			{
				//var id = item.Id;
				//var meta = (byte)item.Metadata;
//
				//var reverseMap = MiNET.Worlds.AnvilWorldProvider.Convert.FirstOrDefault(map => map.Value.Item1 == id);
//
				//if (reverseMap.Value != null)
				//{
				//	id = (byte)reverseMap.Key;
				//}
//
				//var res = BlockFactory.GetBlockStateID(id, meta);
//
				//if (AnvilWorldProvider.BlockStateMapper.TryGetValue(res, out var res2))
				//{
				//	var t = BlockFactory.GetBlockState(res2);
//
				//	ItemFactory.TryGetItem(t.Name, out result);
				//}
				Log.Error($"Failed to convert MiNET item to Alex item. ({(item == null ? "" : $"Name={item.Id}, ")} CallingMethod={source}, MiNET={item})");
			}
			else if (ItemFactory.TryGetItem(item.LegacyId, item.Metadata, out result)) { }

			if (result == null || (result.IsAir() && !(item is MiNET.Items.ItemAir)))
			{
				if (!ItemFactory.TryGetBedrockItem(item.Id, item.Metadata, out result))
					Log.Warn(
						$"Failed to convert MiNET item to Alex item. ({(item == null ? "" : $"Name={item.Id}, ")} CallingMethod={source}, MiNET={item})");
			}

			if (result == null)
			{
				return new ItemAir() { Count = 0 };
			}

			result.StackID = item.UniqueId;
			result.Meta = item.Metadata;
			result.Count = item.Count;
			result.Nbt = item.ExtraData;
			result.Id = item.LegacyId;

			return result;
		}
	}
}