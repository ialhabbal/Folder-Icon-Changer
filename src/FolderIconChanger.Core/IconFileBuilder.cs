using System.Buffers.Binary;
using System.Text;
using ImageMagick;

namespace FolderIconChanger.Core;

public sealed class IconFileBuilder
{
    public static IReadOnlyList<int> Resolutions { get; } = [16, 24, 32, 48, 64, 128, 256];

    public byte[] Build(string imagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);

        try
        {
            using var source = new MagickImage(imagePath);
            source.AutoOrient();

            var pngFrames = new List<byte[]>(Resolutions.Count);
            foreach (var resolution in Resolutions)
            {
                using var frame = source.Clone();
                frame.FilterType = FilterType.Lanczos;
                frame.Resize(new MagickGeometry((uint)resolution, (uint)resolution)
                {
                    FillArea = true
                });
                frame.BackgroundColor = MagickColors.Transparent;
                frame.Extent((uint)resolution, (uint)resolution, Gravity.Center);
                frame.Format = MagickFormat.Png;

                using var png = new MemoryStream();
                frame.Write(png);
                pngFrames.Add(png.ToArray());
            }

            var icon = CreateIconFile(pngFrames);
            Validate(icon);
            return icon;
        }
        catch (MagickException exception)
        {
            throw new ImageProcessingException(
                "The selected image could not be opened or converted.",
                exception);
        }
        catch (IOException exception)
        {
            throw new ImageProcessingException(
                "The selected image could not be opened or converted.",
                exception);
        }
    }

    public void Validate(string iconPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(iconPath);
        Validate(File.ReadAllBytes(iconPath));
    }

    public void Validate(byte[] icon)
    {
        ArgumentNullException.ThrowIfNull(icon);

        var directoryLength = checked(6 + (16 * Resolutions.Count));
        if (icon.Length < directoryLength
            || BinaryPrimitives.ReadUInt16LittleEndian(icon.AsSpan(0, 2)) != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(icon.AsSpan(2, 2)) != 1
            || BinaryPrimitives.ReadUInt16LittleEndian(icon.AsSpan(4, 2)) != Resolutions.Count)
        {
            throw new InvalidDataException("The generated icon has an invalid ICO header.");
        }

        var expectedOffset = directoryLength;
        var pngSignature = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        for (var index = 0; index < Resolutions.Count; index++)
        {
            var resolution = Resolutions[index];
            var entryOffset = 6 + (16 * index);
            var expectedDimension = resolution == 256 ? 0 : resolution;
            var imageLengthValue = BinaryPrimitives.ReadUInt32LittleEndian(
                icon.AsSpan(entryOffset + 8, 4));
            var imageOffsetValue = BinaryPrimitives.ReadUInt32LittleEndian(
                icon.AsSpan(entryOffset + 12, 4));
            if (imageLengthValue > int.MaxValue || imageOffsetValue > int.MaxValue)
            {
                throw new InvalidDataException("The generated icon contains an invalid image range.");
            }

            var imageLength = (int)imageLengthValue;
            var imageOffset = (int)imageOffsetValue;
            if (icon[entryOffset] != expectedDimension
                || icon[entryOffset + 1] != expectedDimension
                || icon[entryOffset + 2] != 0
                || icon[entryOffset + 3] != 0
                || BinaryPrimitives.ReadUInt16LittleEndian(icon.AsSpan(entryOffset + 4, 2)) != 1
                || BinaryPrimitives.ReadUInt16LittleEndian(icon.AsSpan(entryOffset + 6, 2)) != 32
                || imageOffset != expectedOffset
                || imageLength < pngSignature.Length
                || imageOffset > icon.Length - imageLength
                || !icon.AsSpan(imageOffset, pngSignature.Length).SequenceEqual(pngSignature))
            {
                throw new InvalidDataException("The generated icon contains an invalid image entry.");
            }

            try
            {
                using var png = new MemoryStream(icon, imageOffset, imageLength, writable: false);
                using var frame = new MagickImage(png);
                if (frame.Width != resolution || frame.Height != resolution)
                {
                    throw new InvalidDataException("The generated icon contains an image at the wrong size.");
                }
            }
            catch (MagickException exception)
            {
                throw new InvalidDataException("The generated icon contains an unreadable image.", exception);
            }

            expectedOffset = checked(imageOffset + imageLength);
        }

        if (expectedOffset != icon.Length)
        {
            throw new InvalidDataException("The generated icon contains unexpected trailing data.");
        }
    }

    private static byte[] CreateIconFile(IReadOnlyList<byte[]> pngFrames)
    {
        var imageDataOffset = checked(6 + (16 * pngFrames.Count));
        var totalLength = checked(imageDataOffset + pngFrames.Sum(frame => frame.Length));
        var icon = new byte[totalLength];

        BinaryPrimitives.WriteUInt16LittleEndian(icon.AsSpan(0, 2), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(icon.AsSpan(2, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(icon.AsSpan(4, 2), (ushort)pngFrames.Count);

        var dataOffset = imageDataOffset;
        for (var index = 0; index < pngFrames.Count; index++)
        {
            var resolution = Resolutions[index];
            var entryOffset = 6 + (16 * index);
            icon[entryOffset] = resolution == 256 ? (byte)0 : (byte)resolution;
            icon[entryOffset + 1] = icon[entryOffset];
            icon[entryOffset + 2] = 0;
            icon[entryOffset + 3] = 0;
            BinaryPrimitives.WriteUInt16LittleEndian(icon.AsSpan(entryOffset + 4, 2), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(icon.AsSpan(entryOffset + 6, 2), 32);
            BinaryPrimitives.WriteUInt32LittleEndian(
                icon.AsSpan(entryOffset + 8, 4),
                checked((uint)pngFrames[index].Length));
            BinaryPrimitives.WriteUInt32LittleEndian(
                icon.AsSpan(entryOffset + 12, 4),
                checked((uint)dataOffset));

            pngFrames[index].CopyTo(icon, dataOffset);
            dataOffset += pngFrames[index].Length;
        }

        return icon;
    }
}
