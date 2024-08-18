using System;
using System.Collections.Generic;
using System.IO;
using Alex.Interfaces.Resources;
using Alex.ResourcePackLib.Abstraction;
using Alex.ResourcePackLib.IO.Abstract;
using Alex.ResourcePackLib.Json;
using Alex.ResourcePackLib.Json.Bedrock;
using Alex.ResourcePackLib.Json.Models.Entities;
using Alex.ResourcePackLib.Json.Textures;
using NLog;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Alex.ResourcePackLib.Bedrock
{
	public class SkinModule : MCPackModule, ITextureProvider
	{
		private static readonly ILogger Log = LogManager.GetCurrentClassLogger();

		private static PngDecoder PngDecoder { get; } = PngDecoder.Instance; /*new PngDecoder() { IgnoreMetadata = true };*/

		/// <inheritdoc />
		public override string Name
		{
			get
			{
				return Info?.LocalizationName ?? "Unknown";
			}
		}


		public MCPackSkins Info { get; private set; }
		//public LoadedSkin[] Skins { get; private set; }

		public IReadOnlyDictionary<string, EntityModel> EntityModels { get; private set; }

		/// <inheritdoc />
		internal SkinModule(IFilesystem entry) : base(entry) { }

		/// <inheritdoc />
		internal override bool Load()
		{
			try
			{
				var archive = Entry;

				//using (var archive = new ZipFileSystem(Entry.Open(), Entry.Name))
				{
					var skinsEntry = archive.GetEntry("skins.json");

					if (skinsEntry == null)
						return false;

					Info = MCJsonConvert.DeserializeObject<MCPackSkins>(skinsEntry.ReadAsString());

					var geometryEntry = archive.GetEntry("geometry.json");

					if (geometryEntry != null)
					{
						ProcessGeometryJson(geometryEntry);
					}
					else
					{
						EntityModels = new Dictionary<string, EntityModel>();
					}
				}

				//Skins = skins.ToArray();

				return true;
			}
			catch (InvalidDataException ex)
			{
				Log.Debug(ex, $"Could not load module.");
			}

			return false;
		}

		private void ProcessGeometryJson(IFile entry)
		{
			try
			{
				Dictionary<string, EntityModel> entityModels = new Dictionary<string, EntityModel>();
				MCBedrockResourcePack.LoadEntityModel(entry.ReadAsString(), entityModels);
				entityModels = MCBedrockResourcePack.ProcessEntityModels(entityModels);

				EntityModels = entityModels;
			}
			catch (Exception exception)
			{
				Log.Error(exception, "Could not process skinpack geometry.");
			}
		}

		/// <inheritdoc />
		public bool TryGetBitmap(ResourceLocation textureName, out Image<Rgba32> bitmap)
		{
			bitmap = null;
			var textureEntry = Entry.GetEntry(textureName.Path);

			if (textureEntry == null)
				return false;

			Image<Rgba32> img;

			using (var s = textureEntry.Open())
			{
				//img = new Bitmap(s);
				img = Image.Load<Rgba32>(s.ReadToSpan(textureEntry.Length));
			}

			bitmap = img;

			return true;
		}

		/// <inheritdoc />
		public bool TryGetTextureMeta(ResourceLocation textureName, out TextureMeta meta)
		{
			throw new System.NotImplementedException();
		}
	}

	public class LoadedSkin
	{
		public string Name { get; }
		public EntityModel Model { get; }
		public Image<Rgba32> Texture { get; }

		public LoadedSkin(string name, EntityModel model, Image<Rgba32> texture)
		{
			Name = name;
			Model = model;
			Texture = texture;
		}
	}
}