﻿using System;
using System.Collections.Generic;
using System.Linq;
using Alex.Blocks;
using Alex.Blocks.Minecraft;
using Alex.Common.Blocks;
using Alex.Common.Graphics;
using Alex.Common.Utils;
using Alex.Common.Utils.Vectors;
using Alex.Graphics.Models;
using Alex.Interfaces;
using Alex.Networking.Java.Packets.Play;
using Alex.Worlds;
using fNbt;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Color = SixLabors.ImageSharp.Color;
using Vector3 = Microsoft.Xna.Framework.Vector3;
using NLog;

namespace Alex.Entities.BlockEntities;

public class BannerBlockEntity : BlockEntity
{
	private BlockColor _bannerColor;
	private static readonly Logger Log = LogManager.GetCurrentClassLogger(typeof(BannerBlockEntity));
	private BoneMatrices RootBone { get; set; }

	private byte _rotation = 0;
	private float _yRotation = 0f;

	public IReadOnlyCollection<PatternLayer> Patterns
	{
		get => _patterns;
		private set
		{
			if (_patterns?.SequenceEqual(value) ?? false)
				return;

			_patterns = value;
			_isTextureDirty = true;
		}
	}

	private Image<Rgba32> _canvasTexture;

	public BlockColor BannerColor
	{
		get => _bannerColor;
		set
		{
			if (_bannerColor == value)
				return;

			_bannerColor = value;
			_isTextureDirty = true;
		}
	}

	public byte BannerRotation
	{
		get => _rotation;
		set
		{
			_rotation = Math.Clamp(value, (byte)0, (byte)15);

			_yRotation = _rotation * -22.5f;

			if (RootBone != null)
			{
				var headRotation = RootBone.Rotation;
				headRotation.Y = _yRotation;
				RootBone.Rotation = headRotation;
			}
			//HeadBone.Rotation = headRotation;
		}
	}

	public BannerBlockEntity(World level, IVector3I coordinates) : base(level)
	{
		//        _color = color;
		Type = "minecraft:standing_banner";

		Width = 1f;
		Height = 2f;

		Offset = new Vector3(0.5f, 0f, 0.5f);

		X = coordinates.X;
		Y = coordinates.Y;
		Z = coordinates.Z;

		HideNameTag = true;
		IsAlwaysShowName = false;
		AnimationController.Enabled = true;

		Patterns = Array.Empty<PatternLayer>();
		BannerColor = BlockColor.White;
	}

	/// <inheritdoc />
	protected override void UpdateModelParts()
	{
		base.UpdateModelParts();

		if (ModelRenderer != null && ModelRenderer.GetBoneTransform("root", out var bone))
		{
			var rot = bone.Rotation;
			rot.Y = _yRotation;
			bone.Rotation = rot;
			RootBone = bone;
		}
	}

	private bool _isTextureDirty;
	private IReadOnlyCollection<PatternLayer> _patterns;

	private void UpdateCanvasTexture()
	{
		if (_isTextureDirty)
		{
			_canvasTexture ??= new Image<Rgba32>(64, 64, Color.Black);
			_canvasTexture.Mutate(
				cxt =>
				{
					cxt.Clear(Color.Black);
				});

			ApplyCanvasLayer(new PatternLayer(Common.Utils.BannerColor.FromId((int)BannerColor), BannerPattern.Base));

			if (Patterns?.Count > 0)
			{
				foreach (var layer in Patterns)
				{
					if (layer == null) continue;

					ApplyCanvasLayer(layer);
				}
			}

			_isTextureDirty = false;
		}

		TextureUtils.BitmapToTexture2DAsync(
			this, Alex.Instance.GraphicsDevice, _canvasTexture.Clone(), newTexture => { Texture = newTexture; });
	}

	private void ApplyCanvasLayer(PatternLayer layer)
	{
		using var texture = ResolvePatternMask(layer.Pattern);

		if (texture == null)
		{
			Log.Warn($"Could not resolve pattern mask/texture for {layer.Pattern}");

			return;
		}

		//var color = Color.FromRgba(layer.Color.Color.R, layer.Color.Color.G, layer.Color.Color.B, layer.Color.Color.A);
		var color = layer.Color.Color;

		for (int x = 0; x < texture.Width; x++)
		for (int y = 0; y < texture.Height; y++)
		{
			var c = texture[x, y];

			if (c.A > 128)
			{
				var d = _canvasTexture[x, y];

				d.R = color.R;
				d.G = color.G;
				d.B = color.B;
				d.A = c.A;
				_canvasTexture[x, y] = d;
			}
		}
	}

	private Image<Rgba32> ResolvePatternMask(BannerPattern pattern)
	{
		if (Description?.Textures == null)
		{
			Log.Warn($"Unable to resolve pattern mask due to null entity description!");

			return null;
		}

		var key = pattern.ToString().ToLowerSnakeCase();

		if (Description.Textures.TryGetValue(key, out var patternMaskPath))
		{
			Image<Rgba32> patternMask;

			if (Alex.Instance.Resources.TryGetBedrockBitmap(patternMaskPath, out patternMask)) { }
			else if (Alex.Instance.Resources.TryGetBitmap(patternMaskPath, out patternMask)) { }
			else
			{
				Log.Warn($"Could not resolve texture for pattern {pattern} (Path='{patternMaskPath}')");

				throw new InvalidOperationException();
			}

			if (patternMask == null) return null;
			//
			// var cloned = patternMask.Clone(cxt =>
			// {
			//     cxt.Crop(new Rectangle(0, 0, 64, 64));
			// });

			//            patternMask.Dispose();
			return patternMask;
		}
		else
		{
			Log.Warn($"Entity definition does not contain an entry for '{key}'");
		}

		return null;
	}

	private bool _dirty = false;

	protected override bool BlockChanged(Block oldBlock, Block newBlock)
	{
		var success = false;

		if (newBlock is WallBanner wallBanner)
		{
			Type = "minecraft:wall_banner";
			BannerColor = wallBanner.Color;

			if (newBlock.BlockState.TryGetValue("facing", out var facing))
			{
				if (Enum.TryParse<BlockFace>(facing, true, out var face))
				{
					switch (face)
					{
						case BlockFace.East:
							BannerRotation = 12;

							break;

						case BlockFace.West:
							BannerRotation = 4;

							break;

						case BlockFace.North:
							BannerRotation = 8;

							break;

						case BlockFace.South:
							BannerRotation = 0;

							break;
					}
				}
			}

			success = true;
		}

		if (newBlock is StandingBanner standingBanner)
		{
			Type = "minecraft:standing_banner";
			BannerColor = standingBanner.Color;

			if (newBlock.BlockState.TryGetValue("rotation", out var r))
			{
				if (byte.TryParse(r, out var rot))
				{
					BannerRotation = (byte)rot; // // ((rot + 3) % 15);
				}
			}

			success = true;
		}

		_dirty = true;

		return success;
	}

	protected override void ReadFrom(NbtCompound compound)
	{
		base.ReadFrom(compound);

		if (compound != null)
		{
			if (compound.TryGet<NbtInt>("Base", out var baseColor))
			{
				BannerColor = (BlockColor)baseColor.Value;
			}

			if (compound.TryGet<NbtList>("Patterns", out var patterns))
			{
				var newPatterns = new PatternLayer[patterns.Count];

				for (int i = 0; i < patterns.Count; i++)
				{
					var patternCompound = patterns.Get<NbtCompound>(i);
					BannerColor lColor = Common.Utils.BannerColor.White;
					BannerPattern lPattern = BannerPattern.Base;

					if (patternCompound.TryGet<NbtInt>("Color", out var color))
					{
						lColor = Common.Utils.BannerColor.FromId(color.IntValue);
					}

					if (patternCompound.TryGet<NbtString>("Pattern", out var pattern))
					{
						if (EnumHelper.TryParseUsingEnumMember<BannerPattern>(
							    pattern.StringValue, out var bannerPattern))
						{
							lPattern = bannerPattern;
						}
					}

					newPatterns[i] = new PatternLayer(lColor, lPattern);
				}

				Patterns = newPatterns;
				_dirty = true;
			}
		}
	}

	public override void SetData(BlockEntityActionType action, NbtCompound compound)
	{
		if (action == BlockEntityActionType.SetBannerProperties || action == BlockEntityActionType._Init)
		{
			ReadFrom(compound);
		}
		else
		{
			base.SetData(action, compound);
		}
	}

	/// <inheritdoc />
	public override void OnTick()
	{
		base.OnTick();
	}

	/// <inheritdoc />
	public override void Update(IUpdateArgs args)
	{
		base.Update(args);

		if (Description != null && _dirty)
		{
			_dirty = false;

			World.BackgroundWorker.Enqueue(UpdateCanvasTexture);
		}
	}

	protected override void OnDispose()
	{
		base.OnDispose();

		_canvasTexture?.Dispose();
		_canvasTexture = null;
	}

	public class PatternLayer
	{
		public BannerColor Color { get; }
		public BannerPattern Pattern { get; }

		public PatternLayer(BannerColor color, BannerPattern pattern)
		{
			Color = color;
			Pattern = pattern;
		}
	}
}