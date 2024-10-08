﻿using System;
using System.IO;
using System.Reflection;
using NLog;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Image = SixLabors.ImageSharp.Image;

namespace Alex.ResourcePackLib.Generic
{
	public sealed class ResourcePackManifest
	{
		private static readonly Image<Rgba32> UnknownPack = null;

		static ResourcePackManifest()
		{
			var embedded =
					EmbeddedResourceUtils.GetApiRequestFile("Alex.ResourcePackLib.Resources.unknown_pack.png");
			if (embedded != null)
				UnknownPack = Image.Load<Rgba32>(embedded);
		}

		public string Name { get; set; }
		public string Description { get; }
		public Image<Rgba32> Icon { get; }
		public ResourcePackType Type { get; }

		internal ResourcePackManifest(Image<Rgba32> icon,
			string name,
			string description,
			ResourcePackType type = ResourcePackType.Unknown)
		{
			Icon = icon;
			Name = name;
			Description = description;
			Type = type;
		}

		public ResourcePackManifest(string name, string description, ResourcePackType type = ResourcePackType.Unknown) :
			this(UnknownPack, name, description, type) { }
	}
	
	public class EmbeddedResourceUtils
	{
		private static readonly Logger Log = LogManager.GetCurrentClassLogger(typeof(EmbeddedResourceUtils));
		
		public static byte[] GetApiRequestFile(string namespaceAndFileName)
		{
			try
			{
				using (MemoryStream ms = new MemoryStream())
				{
					using (var stream = Assembly.GetCallingAssembly().GetManifestResourceStream(namespaceAndFileName))
					{
						int read = 0;

						do
						{
							byte[] buffer = new byte[256];
							read = stream.Read(buffer, 0, buffer.Length);

							ms.Write(buffer, 0, read);
						} while (read > 0);
					}

					return ms.ToArray();
				}
			}
			catch (Exception exception)
			{
				Log.Error(exception, $"Failed to read Embedded Resource {namespaceAndFileName}");

				return null;
			}
		}
	}
}