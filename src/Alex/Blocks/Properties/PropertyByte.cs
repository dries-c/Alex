using Alex.Blocks.State;
using Alex.Common.Blocks.Properties;

namespace Alex.Blocks.Properties
{
	public class PropertyByte : StateProperty<byte>
	{
		private readonly byte _defaultValue = 0;

		public PropertyByte(string name, byte defaultValue = 0) : base(name)
		{
			_defaultValue = defaultValue;
			Value = defaultValue;
		}

		/// <inheritdoc />
		public override IStateProperty<byte> WithValue(byte value)
		{
			return new PropertyByte(Name, _defaultValue) { Value = value };
		}

		public override byte ParseValue(string value)
		{
			if (byte.TryParse(value, out var result))
			{
				return result;
			}

			return _defaultValue;
		}
	}
}