using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Alex.Blocks.Mapping;

public class BedrockBlockStateDefinition
{
    public string Name { get; set; }
    public long Version { get; set; }
    public IReadOnlyDictionary<string, StateDefinition> States { get; set; }
}

public class StateDefinition
{
    public string Type { get; set; }
    public string Value { get; set; }
}