﻿using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Xna.Framework.Graphics;
using NLog;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors;
using Valve.VR;
using Image = SixLabors.ImageSharp.Image;
using Rectangle = System.Drawing.Rectangle;

namespace Alex.Common.Utils
{
	public static class TextureUtils
	{
		public delegate void TextureCreated(Texture2D texture);

		private static readonly Logger Log = LogManager.GetCurrentClassLogger(typeof(TextureUtils));
		public static Thread RenderThread { get; set; }
		public static Action<Action, object> QueueOnRenderThread { get; set; }

		public static Texture2D BitmapToTexture2D<TImageFormat>(GraphicsDevice device,
			Image<TImageFormat> bmp,
			[CallerMemberName] string caller = "Image Converter") where TImageFormat : unmanaged, IPixel<TImageFormat>
		{
			return BitmapToTexture2D(caller, device, bmp);
		}

		private static uint[] GetPixelData<TImageFormat>(this Image<TImageFormat> image)
			where TImageFormat : unmanaged, IPixel<TImageFormat>
		{
			uint[] colorData;

			if (image.DangerousTryGetSinglePixelMemory(out var ps))
			{
				var pixelSpan = ps.Span;
				colorData = new uint[pixelSpan.Length];

				Rgba32 dest = new Rgba32();

				for (int i = 0; i < pixelSpan.Length; i++)
				{
					pixelSpan[i].ToRgba32(ref dest);
					colorData[i] = dest.Rgba;
				}
			}
			else
			{
				throw new Exception("Could not get image data!");
			}

			return colorData;
		}
		
		private static Texture2D Execute<TImageFormat>(GraphicsDevice device, Image<TImageFormat> image, object tag) where TImageFormat : unmanaged, IPixel<TImageFormat>
		{
			var r = new Texture2D(device, image.Width, image.Height);

			r.SetData(image.GetPixelData());
			r.Tag = tag;

			return r;
		}

		public static void BitmapToTexture2DAsync<TImageFormat>(object owner,
			GraphicsDevice device,
			Image<TImageFormat> image,
			TextureCreated onTextureCreated,
			string name = null) where TImageFormat : unmanaged, IPixel<TImageFormat>
		{
			if (image == null)
				return;

		

			/*if (Thread.CurrentThread == RenderThread)
			{
				var result = Execute();
				onTextureCreated?.Invoke(result);
			}
			else
			{*/
				QueueOnRenderThread(
					() =>
					{
						var result = Execute(device, image, owner);
						onTextureCreated?.Invoke(result);
					}, name ?? owner);
			//}
		}

		public static Texture2D BitmapToTexture2D<TImageFormat>(object owner,
			GraphicsDevice device,
			Image<TImageFormat> image, string name = null) where TImageFormat : unmanaged, IPixel<TImageFormat>
		{
			Texture2D result = null;
			
			if (Thread.CurrentThread == RenderThread)
			{
				result = Execute(device, image, owner);
			}
			else
			{
				AutoResetEvent resetEvent = new AutoResetEvent(false);

				QueueOnRenderThread(
					() =>
					{
						result = Execute(device, image, owner);

						resetEvent.Set();
					}, name ?? owner);

				resetEvent.WaitOne();
				resetEvent.Dispose();
			}

			//byteSize = result.MemoryUsage();

			return result;
		}

		public static Texture2D ImageToTexture2D(object owner, GraphicsDevice device, byte[] bmp)
		{
			//using (MemoryStream s = new MemoryStream(bmp))
			{
				using (var image = Image.Load<Rgba32>(bmp))
					return BitmapToTexture2D(owner, device, image);
			}
		}

		/*public static void ImageToTexture2DAsync(object owner,
			GraphicsDevice device,
			byte[] bmp,
			TextureCreated textureCreated)
		{
			//using (MemoryStream s = new MemoryStream(bmp))
			{
				var image = Image.Load(bmp);

				BitmapToTexture2DAsync(
					owner, device, image, (t) =>
					{
						image.Dispose();
						textureCreated?.Invoke(t);
					});
			}
		}*/

		public static Texture2D Slice<TImageFormat>(this Image<TImageFormat> bmp,
			GraphicsDevice graphics,
			Rectangle region) where TImageFormat : unmanaged, IPixel<TImageFormat>
		{
			return BitmapToTexture2D(
				graphics,
				bmp.Clone(
					context => context.Crop(
						new SixLabors.ImageSharp.Rectangle(region.X, region.Y, region.Width, region.Height))));
		}

		public static void ClearRegion<TImageFormat>(ref Image<TImageFormat> target, Rectangle rectangle)
			where TImageFormat : unmanaged, IPixel<TImageFormat>
		{
			target.Mutate<TImageFormat>(
				x =>
				{
					x.Clear(
						SixLabors.ImageSharp.Color.White.WithAlpha(0f),
						new RectangleF(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height));
					// x.Fill(SixLabors.ImageSharp.Color.White, new RectangleF(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height));
				});
		}

		public static void CopyRegionIntoImage<TImageFormat>(Image<TImageFormat> srcBitmap,
			System.Drawing.Rectangle srcRegion,
			ref Image<TImageFormat> destBitmap,
			System.Drawing.Rectangle destRegion,
			bool interpolate = false,
			float interpolateProgress = 0.5f,
			bool clear = false) where TImageFormat : unmanaged, IPixel<TImageFormat>
		{
			try
			{
				using (var newImage = srcBitmap.Clone(
					       x =>
					       {
						       x.Crop(
							       new SixLabors.ImageSharp.Rectangle(
								       srcRegion.X, srcRegion.Y, srcRegion.Width, srcRegion.Height));
					       }))
				{
					var nwImage = newImage;

					/*if (interpolate){
						for (int x = 0; x < destRegion.Width; x++)
						{
							for (int y = 0; y < destRegion.Height; y++)
							{
								var sourceColor = nwImage[x, y];
								var destinationColor = destBitmap[destRegion.X + x, destRegion.Y + y];
								destBitmap[destRegion.X + x, destRegion.Y + y] = new Rgba32(Vector4.Lerp(destinationColor.ToVector4(), sourceColor.ToVector4(), interpolateProgress));//, destinationColor.G, destinationColor.B, destinationColor.A);
							}
						}
					}
					else*/
					{
						destBitmap.Mutate<TImageFormat>(
							(context) =>
							{
								if (clear)
								{
									context.Clear(
										SixLabors.ImageSharp.Color.White.WithAlpha(0f),
										new RectangleF(
											destRegion.X, destRegion.Y, destRegion.Width, destRegion.Height));
								}

								if (interpolate)
								{
									context.ApplyProcessor(
										new InterpolateProcessor(
											nwImage,
											new SixLabors.ImageSharp.Point(
												destRegion.Location.X, destRegion.Location.Y),
											PixelColorBlendingMode.Normal,
											nwImage.Configuration.GetGraphicsOptions().AlphaCompositionMode
											/*nwImage.GetConfiguration().GetGraphicsOptions().AlphaCompositionMode*/,
											interpolateProgress));

									/*context.DrawImage(
										nwImage, new SixLabors.ImageSharp.Point(destRegion.Location.X, destRegion.Location.Y),
										PixelColorBlendingMode.Normal, interpolateProgress);*/
								}
								else
								{
									context.DrawImage(
										nwImage,
										new SixLabors.ImageSharp.Point(destRegion.Location.X, destRegion.Location.Y),
										PixelColorBlendingMode.Normal, 1f);
								}
							});
					}
				}
			}
			catch (Exception ex)
			{
				Log.Error(ex, $"An error occured!");
			}
		}
	}

	/// <summary>Combines two images together by blending the pixels.</summary>
	/// <typeparam name="TPixelBg">The pixel format of destination image.</typeparam>
	/// <typeparam name="TPixelFg">The pixel format of source image.</typeparam>
	internal class InterpolateProcessor<TPixelBg, TPixelFg> : ImageProcessor<TPixelBg>
		where TPixelBg : unmanaged, IPixel<TPixelBg> where TPixelFg : unmanaged, IPixel<TPixelFg>
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="T:SixLabors.ImageSharp.Processing.Processors.Drawing.DrawImageProcessor`2" /> class.
		/// </summary>
		/// <param name="configuration">The configuration which allows altering default behaviour or extending the library.</param>
		/// <param name="image">The foreground <see cref="T:SixLabors.ImageSharp.Image`1" /> to blend with the currently processing image.</param>
		/// <param name="source">The source <see cref="T:SixLabors.ImageSharp.Image`1" /> for the current processor instance.</param>
		/// <param name="sourceRectangle">The source area to process for the current processor instance.</param>
		/// <param name="location">The location to draw the blended image.</param>
		/// <param name="colorBlendingMode">The blending mode to use when drawing the image.</param>
		/// <param name="alphaCompositionMode">The Alpha blending mode to use when drawing the image.</param>
		/// <param name="interpolationValue">The interpolation progress. Must be between 0 and 1.</param>
		public InterpolateProcessor(Configuration configuration,
			SixLabors.ImageSharp.Image<TPixelFg> image,
			SixLabors.ImageSharp.Image<TPixelBg> source,
			SixLabors.ImageSharp.Rectangle sourceRectangle,
			Point location,
			PixelColorBlendingMode colorBlendingMode,
			PixelAlphaCompositionMode alphaCompositionMode,
			float interpolationValue) : base(configuration, source, sourceRectangle)
		{
			//	Guard.MustBeBetweenOrEqualTo(opacity, 0.0f, 1f, nameof(opacity));
			this.Image = image;
			this.InterpolationValue = interpolationValue;
			this.Blender = PixelOperations<TPixelBg>.Instance.GetPixelBlender(colorBlendingMode, alphaCompositionMode);
			this.Location = location;
		}

		/// <summary>Gets the image to blend</summary>
		public SixLabors.ImageSharp.Image<TPixelFg> Image { get; }

		/// <summary>Gets the opacity of the image to blend</summary>
		public float InterpolationValue { get; }

		/// <summary>Gets the pixel blender</summary>
		public PixelBlender<TPixelBg> Blender { get; }

		/// <summary>Gets the location to draw the blended image</summary>
		public Point Location { get; }

		/// <inheritdoc />
		protected override void OnFrameApply(ImageFrame<TPixelBg> source)
		{
			SixLabors.ImageSharp.Rectangle sourceRectangle = this.SourceRectangle;
			Configuration configuration = this.Configuration;
			SixLabors.ImageSharp.Image<TPixelFg> image = this.Image;
			PixelBlender<TPixelBg> blender = this.Blender;
			int y = this.Location.Y;
			SixLabors.ImageSharp.Rectangle rectangle1 = image.Bounds;
			int num = Math.Max(this.Location.X, sourceRectangle.X);
			int right = Math.Min(this.Location.X + rectangle1.Width, sourceRectangle.Right);
			int targetX = num - this.Location.X;
			int top = Math.Max(this.Location.Y, sourceRectangle.Y);
			int bottom = Math.Min(this.Location.Y + rectangle1.Height, sourceRectangle.Bottom);
			int width = right - num;

			SixLabors.ImageSharp.Rectangle
				rectangle2 = SixLabors.ImageSharp.Rectangle.FromLTRB(num, top, right, bottom);

			if (rectangle2.Width <= 0 || rectangle2.Height <= 0)
				throw new ImageProcessingException(
					"Cannot draw image because the source image does not overlap the target image.");

			InterpolateProcessor<TPixelBg, TPixelFg>.RowOperation operation =
				new InterpolateProcessor<TPixelBg, TPixelFg>.RowOperation(
					source, image, blender, configuration, num, width, y, targetX, InterpolationValue);

			ParallelRowIterator.IterateRows<InterpolateProcessor<TPixelBg, TPixelFg>.RowOperation>(
				configuration, rectangle2, in operation);
		}

		/// <summary>
		/// A <see langword="struct" /> implementing the draw logic for <see cref="T:SixLabors.ImageSharp.Processing.Processors.Drawing.DrawImageProcessor`2" />.
		/// </summary>
		private readonly struct RowOperation : IRowOperation
		{
			private readonly ImageFrame<TPixelBg> sourceFrame;
			private readonly SixLabors.ImageSharp.Image<TPixelFg> targetImage;
			private readonly PixelBlender<TPixelBg> blender;
			private readonly Configuration configuration;
			private readonly int minX;
			private readonly int width;
			private readonly int locationY;
			private readonly int targetX;
			private readonly float interpolationValue;

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			public RowOperation(ImageFrame<TPixelBg> sourceFrame,
				SixLabors.ImageSharp.Image<TPixelFg> targetImage,
				PixelBlender<TPixelBg> blender,
				Configuration configuration,
				int minX,
				int width,
				int locationY,
				int targetX,
				float interpolationValue)
			{
				this.sourceFrame = sourceFrame;
				this.targetImage = targetImage;
				this.blender = blender;
				this.configuration = configuration;
				this.minX = minX;
				this.width = width;
				this.locationY = locationY;
				this.targetX = targetX;
				this.interpolationValue = interpolationValue;
			}

			/// <inheritdoc />
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			public void Invoke(int y)
			{
				Span<TPixelBg> destination = this.sourceFrame.DangerousGetPixelRowMemory(y).Span.Slice(this.minX, this.width);

				Span<TPixelFg> span = this.targetImage.DangerousGetPixelRowMemory(y - this.locationY).Span
				   .Slice(this.targetX, this.width);

				//Vector4.Lerp(span[i].ToVector4(), destination[i].ToVector4(), interpolationValue)

				for (int i = 0; i < destination.Length; i++)
				{
					var currentValue = destination[i];
					var sourceValue = span[i];

					span[i].FromVector4(
						Vector4.Lerp(sourceValue.ToVector4(), currentValue.ToVector4(), interpolationValue));
				}
				/*this.blender.Blend<TPixelFg>(
					this.configuration, destination, (ReadOnlySpan<TPixelBg>) destination,
					(ReadOnlySpan<TPixelFg>) span, this.opacity);*/
			}
		}
	}

	public class InterpolateProcessor : IImageProcessor
	{
		/// <summary>Gets the image to blend.</summary>
		public Image Image { get; }

		/// <summary>Gets the location to draw the blended image.</summary>
		public Point Location { get; }

		/// <summary>Gets the blending mode to use when drawing the image.</summary>
		public PixelColorBlendingMode ColorBlendingMode { get; }

		/// <summary>
		/// Gets the Alpha blending mode to use when drawing the image.
		/// </summary>
		public PixelAlphaCompositionMode AlphaCompositionMode { get; }

		/// <summary>Gets the opacity of the image to blend.</summary>
		public float InterpolationValue { get; }

		public InterpolateProcessor(Image image,
			Point location,
			PixelColorBlendingMode colorBlendingMode,
			PixelAlphaCompositionMode alphaCompositionMode,
			float interpolationValue)
		{
			this.Image = image;
			this.Location = location;
			this.ColorBlendingMode = colorBlendingMode;
			this.AlphaCompositionMode = alphaCompositionMode;
			this.InterpolationValue = interpolationValue;
		}

		/// <inheritdoc />
		public IImageProcessor<TPixelBg> CreatePixelSpecificProcessor<TPixelBg>(Configuration configuration,
			Image<TPixelBg> source,
			SixLabors.ImageSharp.Rectangle sourceRectangle) where TPixelBg : unmanaged, IPixel<TPixelBg>
		{
			InterpolateProcessor.ProcessorFactoryVisitor<TPixelBg> processorFactoryVisitor =
				new InterpolateProcessor.ProcessorFactoryVisitor<TPixelBg>(
					configuration, this, source, sourceRectangle);

			this.Image.AcceptVisitor((IImageVisitor)processorFactoryVisitor);

			return processorFactoryVisitor.Result;
		}

		private class ProcessorFactoryVisitor<TPixelBg> : IImageVisitor where TPixelBg : unmanaged, IPixel<TPixelBg>
		{
			private readonly Configuration configuration;
			private readonly InterpolateProcessor definition;
			private readonly Image<TPixelBg> source;
			private readonly SixLabors.ImageSharp.Rectangle sourceRectangle;

			public ProcessorFactoryVisitor(Configuration configuration,
				InterpolateProcessor definition,
				Image<TPixelBg> source,
				SixLabors.ImageSharp.Rectangle sourceRectangle)
			{
				this.configuration = configuration;
				this.definition = definition;
				this.source = source;
				this.sourceRectangle = sourceRectangle;
			}

			public IImageProcessor<TPixelBg> Result { get; private set; }

			public void Visit<TPixelFg>(Image<TPixelFg> image) where TPixelFg : unmanaged, IPixel<TPixelFg>
			{
				this.Result = (IImageProcessor<TPixelBg>)new InterpolateProcessor<TPixelBg, TPixelFg>(
					this.configuration, image, this.source, this.sourceRectangle, this.definition.Location,
					this.definition.ColorBlendingMode, this.definition.AlphaCompositionMode,
					this.definition.InterpolationValue);
			}
		}
	}
}