﻿using CarinaStudio;
using CarinaStudio.IO;
using CarinaStudio.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media.ImageRenderers
{
	/// <summary>
	/// Base implementation of <see cref="IImageRenderer"/>.
	/// </summary>
	abstract class BaseImageRenderer : IImageRenderer
	{
		// Statis fields.
		static readonly TaskFactory RenderingTaskFactory = new TaskFactory(new FixedThreadsTaskScheduler(Math.Min(2, Environment.ProcessorCount)));


		/// <summary>
		/// Initialize new <see cref="BaseImageRenderer"/> instance.
		/// </summary>
		/// <param name="format">Format supported by this instance.</param>
		protected BaseImageRenderer(ImageFormat format)
		{
			this.Format = format;
			this.Logger = App.Current.LoggerFactory.CreateLogger(this.GetType().Name);
		}


		/// <summary>
		/// Create function to extract from [9, 16]-bit data to 16-bit data.
		/// </summary>
		/// <param name="byteOrdering">Byte ordering.</param>
		/// <param name="effectiveBits">Effective bits, range is [9, 16].</param>
		/// <returns>Extraction function.</returns>
		protected Func<byte, byte, ushort> Create16BitColorExtraction(ByteOrdering byteOrdering, int effectiveBits)
		{
			if (effectiveBits < 9 || effectiveBits > 16)
				throw new ArgumentOutOfRangeException(nameof(effectiveBits));
			if (effectiveBits == 16)
			{
				return byteOrdering == ByteOrdering.LittleEndian
					? (b1, b2) => (ushort)((b2 << 8) | b1)
					: (b1, b2) => (ushort)((b1 << 8) | b2);
			}
			else
			{
				var effectiveBitsShiftCount = (16 - effectiveBits);
				var effectiveBitsMask = (0xffff >> effectiveBitsShiftCount);
				return byteOrdering == ByteOrdering.LittleEndian
					? (b1, b2) => (ushort)((((b2 << 8) | b1) & effectiveBitsMask) << effectiveBitsShiftCount)
					: (b1, b2) => (ushort)((((b1 << 8) | b2) & effectiveBitsMask) << effectiveBitsShiftCount);
			}
		}


		/// <summary>
		/// Create conversion function to convert from [9, 16]-bits data to 8-bits data.
		/// </summary>
		/// <param name="byteOrdering">Byte ordering.</param>
		/// <param name="effectiveBits">Effective bits, range is [9, 16].</param>
		/// <returns>Conversion function.</returns>
		protected Func<byte, byte, byte> Create16BitsTo8BitsConversion(ByteOrdering byteOrdering, int effectiveBits)
		{
			if (effectiveBits < 9 || effectiveBits > 16)
				throw new ArgumentOutOfRangeException(nameof(effectiveBits));
			if (effectiveBits == 16)
			{
				return byteOrdering == ByteOrdering.LittleEndian
					? (b1, b2) => b2
					: (b1, b2) => b1;
			}
			else
            {
				var effectiveBitsShiftCount = (effectiveBits - 8);
				var effectiveBitsMask = (0xff << effectiveBitsShiftCount);
				return byteOrdering == ByteOrdering.LittleEndian
					? (b1, b2) => (byte)((((b2 << 8) | b1) & effectiveBitsMask) >> effectiveBitsShiftCount)
					: (b1, b2) => (byte)((((b1 << 8) | b2) & effectiveBitsMask) >> effectiveBitsShiftCount);
			}
		}


		/// <summary>
		/// Logger.
		/// </summary>
		protected ILogger Logger { get; }


		/// <summary>
		/// Called to perform image rendering. The method will be called in background thread.
		/// </summary>
		/// <param name="source">Source of raw image data.</param>
		/// <param name="imageStream"><see cref="Stream"/> to read raw image data.</param>
		/// <param name="bitmapBuffer"><see cref="IBitmapBuffer"/> to receive rendered image.</param>
		/// <param name="renderingOptions">Rendering options.</param>
		/// <param name="planeOptions">List of <see cref="ImagePlaneOptions"/> for rendering.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>Result of rendering.</returns>
		protected abstract ImageRenderingResult OnRender(IImageDataSource source, Stream imageStream, IBitmapBuffer bitmapBuffer, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken);


		/// <inheritdoc/>
		public async Task<ImageRenderingResult> Render(IImageDataSource source, IBitmapBuffer bitmapBuffer, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions, CancellationToken cancellationToken)
		{
			// check parameter
			if (bitmapBuffer.Format != this.RenderedFormat)
				throw new ArgumentException($"Invalid format of bitmap buffer: {bitmapBuffer.Format}.");

			// share resources
			using var sharedSource = source.Share();
			using var sharedBitmapBuffer = bitmapBuffer.Share();

			// open stream
			var stream = await sharedSource.OpenStreamAsync(StreamAccess.Read);
			if (cancellationToken.IsCancellationRequested)
			{
				Global.RunWithoutErrorAsync(stream.Dispose);
				throw new TaskCanceledException();
			}

			// render
			try
            {
				return await RenderingTaskFactory.StartNew(() =>
				{
					if (renderingOptions.DataOffset > 0)
						stream.Seek(renderingOptions.DataOffset, SeekOrigin.Begin);
					var stopWatch = App.CurrentOrNull?.IsDebugMode == true ? new Stopwatch() : null;
					stopWatch?.Start();
					var result = this.OnRender(sharedSource, stream, sharedBitmapBuffer, renderingOptions, planeOptions, cancellationToken);
					stopWatch?.Let(it => this.Logger.LogTrace($"Rendering time: {it.ElapsedMilliseconds} ms"));
					return result;
				});
			}
			finally
            {
				Global.RunWithoutErrorAsync(stream.Dispose);
			}
		}


		// Implementations.
		public abstract IList<ImagePlaneOptions> CreateDefaultPlaneOptions(int width, int height);
		public abstract int EvaluatePixelCount(IImageDataSource source);
		public abstract long EvaluateSourceDataSize(int width, int height, ImageRenderingOptions renderingOptions, IList<ImagePlaneOptions> planeOptions);
		public ImageFormat Format { get; }
		public virtual BitmapFormat RenderedFormat => BitmapFormat.Bgra32;
	}
}
