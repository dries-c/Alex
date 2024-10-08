﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Alex.Common.Resources;
using Alex.Common.Utils;
using Alex.Graphics.Packing;
using Alex.Interfaces.Resources;
using Alex.ResourcePackLib.Json.Textures;
using Alex.Worlds.Singleplayer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using NLog;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Color = SixLabors.ImageSharp.Color;
using TextureInfo = Alex.Graphics.Packing.TextureInfo;

namespace Alex.Graphics
{
	public class AtlasTexturesGeneratedEventArgs : EventArgs
	{
		public Texture2D Texture { get; }

		public AtlasTexturesGeneratedEventArgs(Texture2D texture)
		{
			Texture = texture;
		}
	}

	public class AtlasGenerator
	{
		private static readonly Logger Log = LogManager.GetCurrentClassLogger(typeof(SPWorldProvider));

		private Dictionary<ResourceLocation, Utils.TextureInfo> _atlasLocations =
			new Dictionary<ResourceLocation, Utils.TextureInfo>();

		private Texture2D _textureAtlas;

		public Vector2 AtlasSize { get; private set; }
		public string Selector { get; }

		public EventHandler<AtlasTexturesGeneratedEventArgs> AtlasGenerated;

		public AtlasGenerator(string selector)
		{
			Selector = selector;
		}

		public void Reset()
		{
			// _textureAtlas?.Dispose();
			_atlasLocations = new Dictionary<ResourceLocation, Utils.TextureInfo>();

			AtlasSize = default;
			//  _textureAtlas = default;
		}

		public class ImageEntry
		{
			public Image<Rgba32> Image { get; set; }
			public TextureMeta Meta { get; set; }

			public int Width => Image.Width;
			public int Height => Image.Height;

			public ImageEntry(Image<Rgba32> image, TextureMeta meta)
			{
				Image = image;
				Meta = meta;
			}
		}

		private void GenerateAtlas(GraphicsDevice device,
			IDictionary<ResourceLocation, ImageEntry> blockTextures,
			IProgressReceiver progressReceiver)
		{
			Stopwatch sw = Stopwatch.StartNew();

			Log.Debug($"Generating texture atlas out of {(blockTextures.Count)} bitmaps...");

			long totalSize = 0;

			Image<Rgba32> no = new Image<Rgba32>(TextureWidth, TextureHeight);

			for (int x = 0; x < no.Width; x++)
			{
				var xCheck = x < no.Width / 2;

				for (int y = 0; y < no.Height; y++)
				{
					var yCheck = y < no.Height / 2;

					if ((xCheck && yCheck) || (!xCheck && !yCheck))
					{
						no[x, y] = Color.Purple;
					}
					else
					{
						no[x, y] = Color.Black;
					}
				}
			}

			List<TextureInfo> textures = new List<TextureInfo>();

			textures.Add(
				new TextureInfo()
				{
					Height = no.Height,
					Source = no,
					Width = no.Width,
					ResourceLocation = new ResourceLocation("minecraft", "no_texture")
				});

			foreach (var texture in blockTextures)
			{
				textures.Add(
					new TextureInfo()
					{
						ResourceLocation = texture.Key,
						Height = texture.Value.Height,
						Width = texture.Value.Width,
						Source = texture.Value.Image,
						Meta = texture.Value.Meta
					});
			}

			//var size = textures.Sum(x => Math.Max(x.Width, x.Height)) / TextureWidth;
			// size *= 2;

			//  while (size % TextureWidth != 0)
			//{
			//    size++;
			// }

			ImagePacker packer = new ImagePacker();

			Dictionary<ResourceLocation, Utils.TextureInfo> textureInfos =
				new Dictionary<ResourceLocation, Utils.TextureInfo>();

			var maxTHeight = 0;
			var maxTWidth = 0;
			int w = 0;
			int h = 0;
			foreach (var texture in textures)
			{
				w += texture.Width;
				h += texture.Height;
				
				maxTWidth = Math.Max(texture.Width, maxTWidth);
				maxTHeight = Math.Max(texture.Height, maxTHeight);
			}
			
			var maxHeight = h;
			var maxWidth = w;
			
			packer.PackImage(
				progressReceiver, textures, false, false, maxWidth, maxHeight,
				0, true, out var img, out var resultingMap);

			foreach (var node in resultingMap)
			{
				textureInfos[node.Key] = new Utils.TextureInfo(
					new Vector2(img.Width, img.Height), new Vector2(node.Value.Source.X, node.Value.Source.Y),
					node.Value.Source.Width, node.Value.Source.Height,
					node.Value.Source.Height != node.Value.Source.Width, node.Value.Source.Width / TextureWidth,
					node.Value.Source.Height / node.Value.Source.Width);
			}

			var oldAtlas = _textureAtlas;

			progressReceiver?.UpdateProgress(99, "Finalizing texture...");

			_textureAtlas = GetMipMappedTexture2D(device, img);
			_atlasLocations = textureInfos;

			Log.Debug(
				$"Atlas size: W={_textureAtlas.Width},H={_textureAtlas.Height} | TW: {TextureWidth} TH: {TextureHeight}");

			img?.Dispose();

			oldAtlas?.Dispose();

			AtlasSize = new Vector2(_textureAtlas.Width, _textureAtlas.Height);
			totalSize += _textureAtlas.MemoryUsage();
			sw.Stop();

			Log.Info(
				$"TextureAtlas '{Selector}' generated in {sw.ElapsedMilliseconds}ms! ({FormattingUtils.GetBytesReadable(totalSize, 2)})");

			AtlasGenerated?.Invoke(this, new AtlasTexturesGeneratedEventArgs(_textureAtlas));
		}

		private Texture2D GetMipMappedTexture2D(GraphicsDevice device, Image image)
		{
			Texture2D texture = new Texture2D(device, image.Width, image.Height, true, SurfaceFormat.Color);

			for (int level = 0; level < Alex.MipMapLevel; level++)
			{
				int mipWidth = (int)System.Math.Max(1, image.Width >> level);
				int mipHeight = (int)System.Math.Max(1, image.Height >> level);

				if (mipWidth < TextureWidth || mipHeight < TextureHeight)
				{
					Alex.MipMapLevel = level - 1;

					break;
				}

				var bmp = image.CloneAs<Rgba32>(); //.CloneAs<Rgba32>();

				try
				{
					bmp.Mutate(x => x.Resize(mipWidth, mipHeight, KnownResamplers.NearestNeighbor, true));

					uint[] colorData = new uint[bmp.Height * bmp.Width];

					int idx = 0;

					for (int row = 0; row < bmp.Height; row++)
					{
						var rowData = bmp.DangerousGetPixelRowMemory(row);
						var rowSpan = rowData.Span;
						for (int col = 0; col < rowSpan.Length; col++)
						{
							colorData[idx] = rowSpan[col].Rgba;
							idx++;
						}
					}
					/*if (bmp.TryGetSinglePixelSpan(out var pixelSpan))
					{
					   // colorData = new uint[pixelSpan.Length];

						for (int i = 0; i < pixelSpan.Length; i++)
						{
							colorData[i] = pixelSpan[i].Rgba;
						}
					}
					else
					{
						throw new Exception("Could not get image data!");
					}*/

					//TODO: Resample per texture instead of whole texture map.

					texture.SetData(level, null, colorData, 0, colorData.Length);
					//  if (save)
					//	bmp.SaveAsPng(Path.Combine("atlas", $"{name}-{level}.png"));
				}
				finally
				{
					bmp.Dispose();
				}
			}

			texture.Tag = Tag;

			return texture;
		}

		public int TextureWidth { get; private set; } = 16;
		public int TextureHeight { get; private set; } = 16;

		public void LoadResources(GraphicsDevice device,
			Dictionary<ResourceLocation, ImageEntry> loadedTextures,
			ResourceManager resources,
			IProgressReceiver progressReceiver,
			bool build)
		{
			Reset();
			int textureWidth = TextureWidth, textureHeight = TextureHeight;

			//GetTextures(resources, loadedTextures, progressReceiver);

			foreach (var image in loadedTextures.ToArray())
			{
				var texture = image.Value;

				if ((texture.Width > textureWidth && texture.Width % 16 == 0)
				    && (texture.Height > textureHeight && texture.Height % 16 == 0))
				{
					if (texture.Width == texture.Height)
					{
						textureWidth = texture.Width;
						textureHeight = texture.Height;
					}
				}
			}

			TextureHeight = textureHeight;
			TextureWidth = textureWidth;

			if (build) GenerateAtlas(device, loadedTextures, progressReceiver);
		}

		public static readonly object Tag = "AtlasGeneratorTag!";

		public Texture2D GetAtlas(bool clone = true)
		{
			return _textureAtlas;
		}

		public Utils.TextureInfo GetAtlasLocation(ResourceLocation file)
		{
			if (_atlasLocations.TryGetValue(file, out var atlasInfo))
			{
				return atlasInfo;
			}

			return _atlasLocations
				["no_texture"]; // new TextureInfo(AtlasSize, Vector2.Zero, TextureWidth, TextureHeight, false, false);
		}
	}
}