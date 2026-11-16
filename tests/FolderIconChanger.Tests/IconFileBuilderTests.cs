using System.Buffers.Binary;
using ImageMagick;
using Xunit;

namespace FolderIconChanger.Tests;

public sealed class IconFileBuilderTests
{
    [Fact]
    public void Build_CreatesSevenTransparentPngIconImages()
    {
        using var directory = new TemporaryDirectory();
        var imagePath = Path.Combine(directory.Path, "صور with spaces.png");
        using (var source = new MagickImage(MagickColors.Transparent, 180, 90))
        {
            source.Write(imagePath, MagickFormat.Png);
        }

        var icon = new FolderIconChanger.Core.IconFileBuilder().Build(imagePath);

        Assert.Equal((ushort)1, BinaryPrimitives.ReadUInt16LittleEndian(icon.AsSpan(2, 2)));
        Assert.Equal((ushort)7, BinaryPrimitives.ReadUInt16LittleEndian(icon.AsSpan(4, 2)));

        var expectedSizes = new byte[] { 16, 24, 32, 48, 64, 128, 0 };
        for (var index = 0; index < expectedSizes.Length; index++)
        {
            var entryOffset = 6 + (16 * index);
            Assert.Equal(expectedSizes[index], icon[entryOffset]);
            Assert.Equal(expectedSizes[index], icon[entryOffset + 1]);

            var imageLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                icon.AsSpan(entryOffset + 8, 4)));
            var imageOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                icon.AsSpan(entryOffset + 12, 4)));
            Assert.True(imageLength > 32);
            Assert.True(imageOffset + imageLength <= icon.Length);
            Assert.Equal(
                new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 },
                icon.AsSpan(imageOffset, 8).ToArray());

            var width = BinaryPrimitives.ReadUInt32BigEndian(icon.AsSpan(imageOffset + 16, 4));
            var height = BinaryPrimitives.ReadUInt32BigEndian(icon.AsSpan(imageOffset + 20, 4));
            Assert.Equal(index == 6 ? 256u : expectedSizes[index], width);
            Assert.Equal(index == 6 ? 256u : expectedSizes[index], height);

            using var png = new MemoryStream(icon, imageOffset, imageLength, writable: false);
            using var decoded = new MagickImage(png);
            var transparentPixel = decoded.GetPixels().GetPixel(0, 0)
                ?? throw new InvalidOperationException("The generated icon image has no pixels.");
            var transparentColor = transparentPixel.ToColor()
                ?? throw new InvalidOperationException("The generated icon pixel has no color.");
            Assert.Equal((byte)0, transparentColor.A);
        }
    }

    [Theory]
    [InlineData(MagickFormat.Jpeg, ".jpg")]
    [InlineData(MagickFormat.Bmp, ".bmp")]
    [InlineData(MagickFormat.Gif, ".gif")]
    [InlineData(MagickFormat.WebP, ".webp")]
    [InlineData(MagickFormat.Tiff, ".tiff")]
    public void Build_ConvertsCommonFormatsAndNonSquareImages(MagickFormat format, string extension)
    {
        using var directory = new TemporaryDirectory();
        var imagePath = Path.Combine(directory.Path, "wide image" + extension);
        using (var source = new MagickImage(MagickColors.DarkSlateBlue, 210, 70))
        {
            source.Write(imagePath, format);
        }

        var icon = new FolderIconChanger.Core.IconFileBuilder().Build(imagePath);

        Assert.Equal((ushort)7, BinaryPrimitives.ReadUInt16LittleEndian(icon.AsSpan(4, 2)));
        Assert.True(icon.Length > 7 * 32);
    }

    [Fact]
    public void Build_UsesTheFirstFrameOfAnAnimatedGif()
    {
        using var directory = new TemporaryDirectory();
        var imagePath = Path.Combine(directory.Path, "two frames.gif");
        using (var frames = new MagickImageCollection())
        {
            frames.Add(new MagickImage(MagickColors.Red, 100, 60));
            frames.Add(new MagickImage(MagickColors.Blue, 100, 60));
            frames.Write(imagePath, MagickFormat.Gif);
        }

        var icon = new FolderIconChanger.Core.IconFileBuilder().Build(imagePath);
        var entry = 6 + (16 * 6);
        var imageLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
            icon.AsSpan(entry + 8, 4)));
        var imageOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
            icon.AsSpan(entry + 12, 4)));
        using var png = new MemoryStream(icon, imageOffset, imageLength, writable: false);
        using var decoded = new MagickImage(png);
        var centerPixel = decoded.GetPixels().GetPixel(128, 128)
            ?? throw new InvalidOperationException("The generated icon image has no pixels.");
        var centerColor = centerPixel.ToColor()
            ?? throw new InvalidOperationException("The generated icon pixel has no color.");

        Assert.True(centerColor.R > 200);
        Assert.True(centerColor.B < 50);
    }

    [Fact]
    public void Validate_RejectsAnIconWithAnInvalidImageOffset()
    {
        using var directory = new TemporaryDirectory();
        var imagePath = Path.Combine(directory.Path, "picture.png");
        using (var source = new MagickImage(MagickColors.Orange, 80, 80))
        {
            source.Write(imagePath, MagickFormat.Png);
        }

        var builder = new FolderIconChanger.Core.IconFileBuilder();
        var icon = builder.Build(imagePath);
        BinaryPrimitives.WriteUInt32LittleEndian(icon.AsSpan(18, 4), uint.MaxValue);

        Assert.Throws<InvalidDataException>(() => builder.Validate(icon));
    }
}
