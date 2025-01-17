using Carina.PixelViewer.Media.ImageRenderers;
using SkiaSharp;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;

namespace Carina.PixelViewer.Media.FileFormatParsers;

/// <summary>
/// <see cref="IFileFormatParser"/> to parse JPEG file.
/// </summary>
class JpegFileFormatParser : SkiaFileFormatParser
{
    /// <summary>
    /// Initialize new <see cref="JpegFileFormatParser"/> instance.
    /// </summary>
    public JpegFileFormatParser() : base(FileFormats.Jpeg, SKEncodedImageFormat.Jpeg, ImageRenderers.ImageRenderers.All.First(it => it is JpegImageRenderer))
    { }


    /// <summary>
    /// Check whether header of file represents JPEG/JFIF or not.
    /// </summary>
    /// <param name="stream">Stream to read image data.</param>
    /// <returns>True if header represents JPEG/JFIF.</returns>
    public static bool CheckFileHeader(Stream stream)
    {
        var buffer = new byte[3];
        return stream.Read(buffer, 0, 3) == 3
            && buffer[0] == 0xff
            && buffer[1] == 0xd8
            && buffer[2] == 0xff;
    }


    /// <inheritdoc/>
    protected override bool OnCheckFileHeader(Stream stream) =>
        CheckFileHeader(stream);


    /// <inheritdoc/>
    protected override bool OnSeekToIccProfile(Stream stream) =>
        SeekToIccProfile(stream);


    /// <summary>
    /// Seek to embedded ICC profile.
    /// </summary>
    /// <param name="stream">Stream to read JPEG image.</param>
    /// <returns>True if seeking successfully.</returns>
    public static bool SeekToIccProfile(Stream stream)
    {
        // skip file header
        var segmentHeaderBuffer = new byte[4];
        stream.Seek(2, SeekOrigin.Current);

        // find ICC profile in APP segment
        while (true)
        {
            if (stream.Read(segmentHeaderBuffer, 0, 4) < 4)
                return false;
            if (segmentHeaderBuffer[0] != 0xff)
                return false;
            if (segmentHeaderBuffer[1] == 0xda) // SOS
                return false;
            var segmentSize = BinaryPrimitives.ReadUInt16BigEndian(segmentHeaderBuffer.AsSpan(2));
            if (segmentSize < 2)
                return false;
            if ((segmentHeaderBuffer[1] == 0xe1 || segmentHeaderBuffer[1] == 0xe2) // APP1 or APP2
                && segmentSize > 16)
            {
                var segmentDataBuffer = new byte[segmentSize - 2];
                if (stream.Read(segmentDataBuffer, 0, segmentDataBuffer.Length) < segmentDataBuffer.Length)
                    return false;
                if (Encoding.ASCII.GetString(segmentDataBuffer, 0, 11) == "ICC_PROFILE" 
                    && segmentDataBuffer[11] == 0x0
                    && segmentDataBuffer[12] == 0x1
                    && segmentDataBuffer[13] == 0x1)
                {
                    stream.Seek(-segmentSize + 16, SeekOrigin.Current);
                    return true;
                }
            }
            else
                stream.Seek(segmentSize - 2, SeekOrigin.Current);
        }
    }
}