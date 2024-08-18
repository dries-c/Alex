﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Alex.Blocks.Mapping;
using Alex.Blocks.Minecraft;
using Alex.Blocks.Properties;
using Alex.Blocks.State;
using Alex.Blocks.Storage.Palette;
using Alex.Common.Blocks.Properties;
using Alex.Common.Resources;
using Alex.Common.Utils;
using Alex.Graphics.Models.Blocks;
using Alex.Interfaces.Resources;
using Alex.ResourcePackLib.Json.BlockStates;
using Alex.Utils;
using fNbt;
using Newtonsoft.Json;
using BlockModel = Alex.Graphics.Models.Blocks.BlockModel;
using LightBlock = Alex.Blocks.Minecraft.LightBlock;

namespace Alex.Blocks
{
    public static class BlockFactory
    {
        private static NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger(typeof(BlockFactory));

        public static IReadOnlyDictionary<uint, BlockState> AllBlockstates => RegisteredBlockStates;

        private static readonly ConcurrentDictionary<uint, BlockState> RegisteredBlockStates =
            new ConcurrentDictionary<uint, BlockState>();

        private static readonly ConcurrentDictionary<ResourceLocation, BlockStateVariantMapper> BlockStateByName =
            new ConcurrentDictionary<ResourceLocation, BlockStateVariantMapper>();

        public static readonly ConcurrentDictionary<ResourceLocation, BlockStateVariantMapper> BedrockStates =
            new ConcurrentDictionary<ResourceLocation, BlockStateVariantMapper>();

        public static IPalette<BlockState> BlockPalette { get; } = new DirectPalette<BlockState>(GetBlockState);

        private static bool _builtin = false;

        private static void RegisterBuiltinBlocks(ResourceManager manager)
        {
            if (_builtin)
                return;

            _builtin = true;

            List<BlockState> states = new List<BlockState>();

            for (byte i = 0; i < 15; i++)
            {
                BlockState bs = new BlockState()
                {
                    Default = i == 0, Name = "minecraft:light_block",
                    //VariantMapper = lightBlockVariantMapper,
                };

                bs.States.Add(new PropertyInt("block_light_level", i));

                var block = new LightBlock() { Luminance = i };

                bs.Block = block;
                block.BlockState = bs;

                states.Add(bs);
            }

            var lightBlockVariantMapper = new BlockStateVariantMapper(states);

            BlockStateByName.TryAdd("minecraft:light_block", lightBlockVariantMapper);

            var missingBlock = new MissingBlockState("missing_block");
            BlockStateByName.TryAdd("alex:missing_block", missingBlock.VariantMapper);

            var itemFrameBlock = ItemFrameBlockState.Build();
            BlockStateByName.TryAdd("minecraft:item_frame", itemFrameBlock);
            //RegisteredBlockStates.Add(Block.GetBlockStateID(), StationairyWaterModel);
        }


        internal static int LoadBlockstates(IRegistryManager registryManager,
            ResourceManager resources,
            bool replace,
            bool reportMissing = false,
            IProgressReceiver progressReceiver = null)
        {
            //RuntimeIdTable = TableEntry.FromJson(raw);

            progressReceiver?.UpdateProgress(0, "Loading block models...");

            RegisterBuiltinBlocks(resources);

            return LoadModels(registryManager, resources, replace, reportMissing, progressReceiver);
        }

        private static readonly Regex _blockMappingRegex = new Regex(
            @"(?'key'[\:a-zA-Z_\d][^\[]*)(\[(?'data'.*)\])?", RegexOptions.Compiled);

        public static BlockMap LoadBlockMapFromNbt()
        {
            var nbtData = ResourceManager.ReadResource("Alex.Resources.blocks.nbt");

            var file = new NbtFile()
            {
                AllowAlternativeRootTag = true,
                /*BigEndian = true,
                UseVarInt = false*/
            };
            file.LoadFromBuffer(nbtData, 0, nbtData.Length, NbtCompression.GZip);

            Log.Info("Succcessfully loaded block map from NBT!");
            var data = BlockData.FromJson(ResourceManager.ReadStringResource("Alex.Resources.NewBlocks.json"));

            var bm = new BlockMap();
            var tag = file.RootTag as NbtCompound;
            var list = tag["bedrock_mappings"] as NbtList;
            foreach (var kv in data)
            {
                foreach (var state in kv.Value.States)
                {
                    var listEntry = list[(int)state.ID] as NbtCompound;

                    if (listEntry == null)
                    {
                        Log.Warn($"No matching NBT entry for {kv.Key}!");
                        continue;
                    }

                    string entryName = kv.Key;
                    if (state.Properties != null && state.Properties.Count > 0)
                    {
                        var a = string.Join(",", state.Properties.Select(x => $"{x.Key}={x.Value}").ToArray());
                        if (a.Length > 0)
                        {
                            entryName += $"[{a}]";
                        }
                    }

                    var bedrockName = kv.Key;
                    if (listEntry.Contains("bedrock_identifier"))
                    {
                        bedrockName = $"minecraft:{listEntry["bedrock_identifier"].StringValue}";
                    }

                    var bedrockStates = new Dictionary<string, string>();
                    if (listEntry.Contains("state"))
                    {
                        foreach (var t in listEntry["state"] as NbtCompound)
                        {
                            bedrockStates.Add(t.Name, t.StringValue);
                        }
                    }

                    bm.Add(entryName, new BlockMappingEntry()
                    {
                        BedrockIdentifier = bedrockName,
                        BedrockStates = bedrockStates
                    });
                }
            }

            return bm;
        }

        private static int LoadModels(IRegistryManager registryManager,
            ResourceManager resources,
            bool replace,
            bool reportMissing,
            IProgressReceiver progressReceiver)
        {
            Stopwatch sw = Stopwatch.StartNew();

            //   var raw = ResourceManager.ReadStringResource("Alex.Resources.blockmap.json");
            var mapping = LoadBlockMapFromNbt();

            var raw = ResourceManager.ReadStringResource("Alex.Resources.custom_blockmap.json");
            var mapping2 = JsonConvert.DeserializeObject<BlockMap>(raw);

            if (mapping != null && mapping2 != null)
            {
                foreach (var map in mapping2)
                {
                    if (!mapping.TryAdd(map.Key, map.Value))
                    {
                        Log.Warn(
                            $"Failed to register custom block mapping: {map.Key} -> {map.Value.BedrockIdentifier}");
                    }
                }
            }

            var blockRegistry = registryManager.GetRegistry<Block>();
            //var blockStateRegistry = registryManager.GetRegistry<BlockState>();

            var data = BlockData.FromJson(ResourceManager.ReadStringResource("Alex.Resources.NewBlocks.json"));
            int total = data.Count;
            int done = 0;
            int importCounter = 0;

            void LoadEntry(KeyValuePair<string, BlockData> entry)
            {
                done++;

                if (!resources.TryGetBlockState(entry.Key, out var blockStateResource))
                {
                    if (reportMissing)
                        Log.Warn($"Could not find blockstate with key: {entry.Key}");

                    return;
                }

                //double percentage = 100D * ((double) done / (double) total);blockstate variants
                progressReceiver.UpdateProgress(done, total, $"Importing block models...", entry.Key);

                var model = ResolveModel(resources, blockStateResource, out bool isMultipartModel);

                List<BlockState> states = new List<BlockState>();

                var location = new ResourceLocation(entry.Key);
                IRegistryEntry<Block> registryEntry;

                if (!blockRegistry.TryGet(location, out registryEntry))
                {
                    registryEntry = new UnknownBlock();
                    registryEntry = registryEntry.WithLocation(location); // = entry.Key;
                }
                else
                {
                    registryEntry = registryEntry.WithLocation(location);
                }

                var block = registryEntry.Value;

                if (string.IsNullOrWhiteSpace(block.DisplayName))
                {
                    block.DisplayName = entry.Key;
                }


                Dictionary<string, IStateProperty> stateProperties;

                if (entry.Value.Properties != null)
                {
                    stateProperties = entry.Value.Properties.Select(
                        prop =>
                        {
                            var values = prop.Value;
                            IStateProperty stateProperty;

                            if (block.TryGetStateProperty(prop.Key, out var stateProp))
                            {
                                stateProperty = stateProp;
                            }
                            else
                            {
                                stateProperty = new ValidatedPropertyString(prop.Key, values);
                            }

                            return stateProperty;
                        }).ToDictionary(x => x.Name, e => e, StringComparer.OrdinalIgnoreCase);
                }
                else
                {
                    stateProperties = new Dictionary<string, IStateProperty>();
                }

                foreach (var s in entry.Value.States)
                {
                    if (!replace && RegisteredBlockStates.TryGetValue(s.ID, out BlockState st))
                    {
                        Log.Warn($"Duplicate blockstate id (Existing: {st.Name}[{st.ToString()}]) ");

                        continue;
                    }

                    var localBlock = registryEntry.Value;
                    localBlock.DisplayName = block.DisplayName;

                    BlockState variantState = new BlockState();

                    foreach (var prop in stateProperties)
                    {
                        if (!s.Properties.TryGetValue(prop.Key, out var value))
                            variantState.States.Add(prop.Value);

                        variantState.States.Add(prop.Value.WithValue(value));
                    }

                    variantState.Id = s.ID;
                    variantState.Name = entry.Key;
                    variantState.ModelData = ResolveVariant(blockStateResource, variantState, isMultipartModel);
                    variantState.Block = localBlock;
                    variantState.Default = s.Default;

                    localBlock.BlockState = variantState;

                    states.Add(variantState);
                }

                var variantMap = new BlockStateVariantMapper(states);
                variantMap.Model = model;
                variantMap.IsMultiPart = isMultipartModel;

                if (variantMap.Model == null)
                {
                    Log.Warn($"No model found for {entry.Key}[{variantMap.ToString()}]");
                }

                if (!BlockStateByName.TryAdd(location, variantMap))
                {
                    if (replace)
                    {
                        BlockStateByName[location] = variantMap;
                    }
                    else
                    {
                        Log.Warn($"Failed to add blockstate, key already exists! ({location})");
                    }
                }

                foreach (var variant in variantMap.GetVariants())
                {
                    if (!RegisteredBlockStates.TryAdd(variant.Id, variant))
                    {
                        if (replace)
                        {
                            RegisteredBlockStates[variant.Id] = variant;
                            importCounter++;
                        }
                        else
                        {
                            Log.Warn(
                                $"Failed to add blockstate (variant), key already exists! ({variant.Id} - {variant.Name})");
                        }
                    }
                    else
                    {
                        importCounter++;
                    }
                }
            }

            Parallel.ForEach(data, LoadEntry);

            var blockStateTime = sw.Elapsed;

            int counter = 0;

            sw.Restart();
            foreach (var m in mapping.GroupBy(x => x.Value.BedrockIdentifier))
            {
                progressReceiver?.UpdateProgress(counter, mapping.Count, "Mapping blockstates...", m.Key);
                var location = new ResourceLocation(m.Key);

                List<BlockState> states = new List<BlockState>(m.Count());
                BlockModel blockModel = null;
                bool isMultiPart = false;

                bool first = true;

                foreach (var state in m)
                {
                    var match = _blockMappingRegex.Match(state.Key);
                    var keyMatch = match.Groups["key"];
                    var dataMatch = match.Groups["data"];

                    if (!keyMatch.Success)
                    {
                        Log.Warn($"Entry without key!");

                        continue;
                    }

                    BlockState pcVariant = GetBlockState(keyMatch.Value);

                    if (pcVariant != null)
                    {
                        if (dataMatch.Success)
                        {
                            var properties = new BlockVariantKey(dataMatch.Value);

                            foreach (var prop in properties)
                            {
                                pcVariant = pcVariant.WithProperty(prop.Key, prop.Value);
                            }
                        }
                    }

                    if (pcVariant == null)
                    {
                        Log.Warn($"Map failed: {match.Groups["key"].Value} -> {state.Value.BedrockIdentifier}");

                        continue;
                    }

                    PeBlockState bedrockState = new PeBlockState(pcVariant);

                    if (state.Value.BedrockStates != null && state.Value.BedrockStates.Count > 0)
                    {
                        foreach (var bs in state.Value.BedrockStates)
                        {
                            bedrockState.States.Add(new PropertyString(bs.Key, bs.Value));
                        }
                    }

                    bedrockState.Name = state.Value.BedrockIdentifier;
                    bedrockState.Id = (uint)Interlocked.Increment(ref counter);
                    bedrockState.Default = first;
                    bedrockState.ModelData = pcVariant.ModelData;
                    bedrockState.Block = pcVariant.Block;

                    if (first)
                    {
                        blockModel = pcVariant.VariantMapper.Model;
                        isMultiPart = pcVariant.VariantMapper.IsMultiPart;
                    }

                    first = false;

                    states.Add(bedrockState);
                }

                BedrockStates[m.Key] = new BlockStateVariantMapper(states)
                {
                    Model = blockModel, IsMultiPart = isMultiPart
                };
            }

            //Log.Info($"Loaded {multipartBased} multi-part blockstates!");
            Log.Info($"Loaded {BedrockStates.Count} MC:Java -> MC:Bedrock mappings in {sw.ElapsedMilliseconds}ms...");

            var allBedrockStates = BedrockStates.SelectMany(x => x.Value.GetVariants()).ToList();
            _blockStateLocations = allBedrockStates.ToDictionary(x => x.Id, x => x);

            foreach (var bs in allBedrockStates)
            {
                if (bs.Block == null)
                {
                    Log.Warn($"Block is null for {bs}!");
                }
            }

            return importCounter;
        }

        public static IReadOnlyDictionary<uint, BlockState> _blockStateLocations;

        private static BlockModel ResolveModel(ResourceManager resources,
            BlockStateResource blockStateResource,
            out bool isMultipartModel)
        {
            isMultipartModel = blockStateResource.Parts.Any(x => x.When != null && x.When.Length > 0);
            string name = blockStateResource.Name;

            if (string.IsNullOrWhiteSpace(name))
            {
                Log.Warn($"State name is null!");

                return null;
            }

            if (name.Contains("water"))
            {
                return new LiquidBlockModel();
            }

            if (name.Contains("lava"))
            {
                return new LiquidBlockModel();
            }

            return new ResourcePackBlockModel(resources);
        }

        private static BlockStateVariant ResolveVariant(BlockStateResource blockStateResource,
            BlockState state,
            bool isMultiPart)
        {
            if (isMultiPart)
            {
                return new BlockStateVariant(MultiPartModelHelper.GetModels(state, blockStateResource));
            }

            int closestMatch = -1;

            KeyValuePair<BlockVariantKey, BlockStateVariant> closest =
                default(KeyValuePair<BlockVariantKey, BlockStateVariant>);

            foreach (var v in blockStateResource.Variants)
            {
                int matches = 0;
                var variant = v.Key;

                if (variant != null)
                {
                    foreach (var kv in state)
                    {
                        if (!variant.TryGetValue(kv.Name, out string vValue) || !vValue.Equals(
                                kv.StringValue, StringComparison.OrdinalIgnoreCase))
                            break;

                        matches++;
                    }
                }

                if (matches > closestMatch)
                {
                    closestMatch = matches;
                    closest = v;

                    if (matches == state.Count)
                        break;
                }
            }

            return closest.Value;
        }

        public static BlockState GetBlockState(string palleteId)
        {
            if (BlockStateByName.TryGetValue(palleteId, out var result))
            {
                return result.GetDefaultState();
            }

            return new MissingBlockState(palleteId);
        }

        public static BlockState GetBlockState(uint palleteId)
        {
            if (RegisteredBlockStates.TryGetValue(palleteId, out var result))
            {
                return result;
            }

            return new MissingBlockState(palleteId.ToString());
        }

        public static uint GetBlockStateID(int id, byte meta)
        {
            if (id < 0) throw new ArgumentOutOfRangeException();

            return (uint)(id << 4 | meta);
        }

        public static void StateIDToRaw(uint stateId, out int id, out byte meta)
        {
            id = (int)(stateId >> 4);
            meta = (byte)(stateId & 0x0F);
        }
    }
}