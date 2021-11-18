﻿using Avalonia;
using Avalonia.Media.Imaging;
using Carina.PixelViewer.Runtime.InteropServices;
using CarinaStudio;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media
{
	/// <summary>
	/// Data buffer of <see cref="IBitmap"/>.
	/// </summary>
	unsafe interface IBitmapBuffer : IShareableDisposable<IBitmapBuffer>, IMemoryOwner<byte>
	{
		/// <summary>
		/// Color space of bitmap.
		/// </summary>
		BitmapColorSpace ColorSpace { get; }


		/// <summary>
		/// Format of bitmap.
		/// </summary>
		BitmapFormat Format { get; }


		/// <summary>
		/// Height of bitmap in pixels.
		/// </summary>
		int Height { get; }


		/// <summary>
		/// Bytes per row.
		/// </summary>
		int RowBytes { get; }


		/// <summary>
		/// Width of bitmap in pixels.
		/// </summary>
		int Width { get; }
	}


	/// <summary>
	/// Extensions for <see cref="IBitmapBuffer"/>.
	/// </summary>
	static class BitmapBufferExtensions
	{
		// Fields.
		static readonly ILogger? Logger = App.CurrentOrNull?.LoggerFactory?.CreateLogger(nameof(BitmapBufferExtensions));


		/// <summary>
		/// Copy data as new bitmap buffer.
		/// </summary>
		/// <param name="source">Source <see cref="IBitmapBuffer"/>.</param>
		/// <returns><see cref="IBitmapBuffer"/> with copied data.</returns>
		public static IBitmapBuffer Copy(this IBitmapBuffer source) => new BitmapBuffer(source.Format, source.ColorSpace, source.Width, source.Height).Also(it =>
		{
			source.CopyTo(it);
		});


		/// <summary>
		/// Copy data to given bitmap buffer.
		/// </summary>
		/// <param name="source">Source <see cref="IBitmapBuffer"/>.</param>
		/// <param name="dest">Destination <see cref="IBitmapBuffer"/>.</param>
		public static unsafe void CopyTo(this IBitmapBuffer source, IBitmapBuffer dest)
		{
			if (source == dest)
				return;
			if (source.Format != dest.Format)
				throw new ArgumentException("Cannot copy to bitmap with different formats.");
			if (source.ColorSpace != dest.ColorSpace)
				throw new ArgumentException("Cannot copy to bitmap with different color spaces.");
			if (source.Width != dest.Width || source.Height != dest.Height)
				throw new ArgumentException("Cannot copy to bitmap with different dimensions.");
			source.Memory.Pin(sourceBaseAddr =>
			{
				dest.Memory.Pin(destBaseAddr =>
				{
					var sourceRowStride = source.RowBytes;
					var destRowStride = dest.RowBytes;
					if (sourceRowStride == destRowStride)
						Marshal.Copy((void*)sourceBaseAddr, (void*)destBaseAddr, sourceRowStride * source.Height);
					else
					{
						var sourceRowPtr = (byte*)sourceBaseAddr;
						var destRowPtr = (byte*)destBaseAddr;
						var minRowStride = Math.Min(sourceRowStride, destRowStride);
						for (var y = source.Height; y > 0; --y, sourceRowPtr += sourceRowStride, destRowPtr += destRowStride)
							Marshal.Copy(sourceRowPtr, destRowPtr, minRowStride);
					}
				});
			});
		}


		/// <summary>
		/// Create <see cref="IBitmap"/> which copied data from this <see cref="IBitmapBuffer"/> asynchronously.
		/// </summary>
		/// <param name="buffer"><see cref="IBitmapBuffer"/>.</param>
		/// <returns>Task of creating <see cref="IBitmap"/>.</returns>
		public static async Task<IBitmap> CreateAvaloniaBitmapAsync(this IBitmapBuffer buffer)
        {
			using var sharedBuffer = buffer.Share();
			return await Task.Run(() =>
			{
				return sharedBuffer.Memory.Pin((address) =>
				{
					var colorSpace = buffer.ColorSpace;
					if (colorSpace == BitmapColorSpace.Srgb)
					{
						return new Bitmap(Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Unpremul, address, new PixelSize(sharedBuffer.Width, sharedBuffer.Height), new Vector(96, 96), sharedBuffer.RowBytes);
					}
					else
					{
						var avaloniaBitmap = new WriteableBitmap(new PixelSize(sharedBuffer.Width, sharedBuffer.Height), new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Unpremul);
						using var avaloniaBitmapBuffer = avaloniaBitmap.Lock();
						var width = buffer.Width;
						var srcRowStride = buffer.RowBytes;
						var destRowStride = avaloniaBitmapBuffer.RowBytes;
						var stopWatch = App.CurrentOrNull?.IsDebugMode == true
							? new Stopwatch().Also(it => it.Start())
							: null;
						buffer.Memory.Pin(srcBaseAddr =>
						{
							unsafe
							{
								switch (buffer.Format)
								{
									case BitmapFormat.Bgra32:
                                        {
											var unpackFunc = ImageProcessing.SelectBgra32UnpackingAndNormalizing();
											var packFunc = ImageProcessing.SelectBgra32DenormalizingAndPacking();
											Parallel.For(0, buffer.Height, new ParallelOptions() { MaxDegreeOfParallelism = ImageProcessing.SelectMaxDegreeOfParallelism() }, (y) =>
											{
												var b = 0.0;
												var g = 0.0;
												var r = 0.0;
												var a = 0.0;
												var srcPixelPtr = (uint*)((byte*)srcBaseAddr + (y * srcRowStride));
												var destPixelPtr = (uint*)((byte*)avaloniaBitmapBuffer.Address + (y * destRowStride));
												for (var x = width; x > 0; --x, ++srcPixelPtr, ++destPixelPtr)
												{
													unpackFunc(*srcPixelPtr, &b, &g, &r, &a);
													colorSpace.ConvertToSrgbColorSpace(&r, &g, &b);
													*destPixelPtr = packFunc(b, g, r, a);
												}
											});
                                        }
										break;
									case BitmapFormat.Bgra64:
                                        {
											var unpackFunc = ImageProcessing.SelectBgra64UnpackingAndNormalizing();
											var packFunc = ImageProcessing.SelectBgra32DenormalizingAndPacking();
											Parallel.For(0, buffer.Height, new ParallelOptions() { MaxDegreeOfParallelism = ImageProcessing.SelectMaxDegreeOfParallelism() }, (y) =>
											{
												var b = 0.0;
												var g = 0.0;
												var r = 0.0;
												var a = 0.0;
												var srcPixelPtr = (ulong*)((byte*)srcBaseAddr + (y * srcRowStride));
												var destPixelPtr = (uint*)((byte*)avaloniaBitmapBuffer.Address + (y * destRowStride));
												for (var x = width; x > 0; --x, ++srcPixelPtr, ++destPixelPtr)
												{
													unpackFunc(*srcPixelPtr, &b, &g, &r, &a);
													colorSpace.ConvertToSrgbColorSpace(&r, &g, &b);
													*destPixelPtr = packFunc(b, g, r, a);
												}
											});
										}
										break;
								}
							}
						});
						if (stopWatch != null)
						{
							stopWatch.Stop();
							Logger?.LogTrace($"Take {stopWatch.ElapsedMilliseconds} ms to convert from {width}x{buffer.Height} {colorSpace} bitmap buffer to Avalonia bitmap");
						}
						return avaloniaBitmap;
					}
				});
			});
        }


#if WINDOWS10_0_17763_0_OR_GREATER
		/// <summary>
		/// Create <see cref="System.Drawing.Bitmap"/> which copied data from this <see cref="IBitmapBuffer"/>.
		/// </summary>
		/// <param name="buffer"><see cref="IBitmapBuffer"/>.</param>
		/// <returns><see cref="System.Drawing.Bitmap"/>.</returns>
		public static System.Drawing.Bitmap CreateSystemDrawingBitmap(this IBitmapBuffer buffer)
		{
			return buffer.Memory.Pin((address) =>
			{
				return new System.Drawing.Bitmap(buffer.Width, buffer.Height, buffer.RowBytes, buffer.Format.ToSystemDrawingPixelFormat(), address);
			});
		}
#endif


		/// <summary>
		/// Get byte offset to pixel on given position.
		/// </summary>
		/// <param name="buffer"><see cref="IBitmapBuffer"/>.</param>
		/// <param name="x">Horizontal position of pixel.</param>
		/// <param name="y">Vertical position of pixel.</param>
		/// <returns>Byte offset to pixel.</returns>
		public static int GetPixelOffset(this IBitmapBuffer buffer, int x, int y) => (y * buffer.RowBytes) + (x * buffer.Format.GetByteSize());
	}
}
