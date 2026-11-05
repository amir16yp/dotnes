using System.Text;

namespace dotnes.tests;

public class ChrCodecTests
{
    [Fact]
    public void EncodeDecode_RoundTripsSingleTile()
    {
        var pixels = new byte[8, 8];
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                pixels[y, x] = (byte)((x + y) % 4);

        var encoded = ChrCodec.EncodeTile(pixels);
        Assert.Equal(16, encoded.Length);

        var decoded = ChrCodec.DecodeTile(encoded);
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                Assert.Equal(pixels[y, x], decoded[y, x]);
    }

    [Fact]
    public void FlattenUnflatten_RoundTrips()
    {
        var pixels = new byte[8, 8];
        pixels[0, 0] = 3;
        pixels[7, 7] = 1;
        var flat = ChrCodec.FlattenTile(pixels);
        Assert.Equal(64, flat.Length);
        var restored = ChrCodec.UnflattenTile(flat);
        Assert.Equal(3, restored[0, 0]);
        Assert.Equal(1, restored[7, 7]);
    }

    [Fact]
    public void ChrAssembly_RoundTripsHelloChr()
    {
        string path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "hello", "chr_generic.s"));
        Assert.True(File.Exists(path), $"Missing sample CHR at {path}");

        byte[] original = ChrAssembly.ParseFile(path);
        Assert.True(original.Length > 0);
        Assert.Equal(0, original.Length % ChrCodec.BytesPerTile);

        string formatted = ChrAssembly.Format(original);
        byte[] reparsed = ChrAssembly.Parse(formatted);
        Assert.Equal(original, reparsed);

        var tiles = ChrCodec.DecodeTiles(original);
        byte[] reencoded = ChrCodec.EncodeTiles(tiles);
        Assert.Equal(original, reencoded);
    }

    [Fact]
    public void ChrAssembly_FormatMatchesDecompilerLayout()
    {
        byte[] data = [0x00, 0xFF, 0x81, 0x81, 0x81, 0x81, 0x81, 0xFF, 0x00, 0x00, 0x7E, 0x42, 0x42, 0x42, 0x42, 0x7E];
        string text = ChrAssembly.Format(data);
        Assert.StartsWith(".segment \"CHARS\"", text);
        Assert.Contains(".byte $00,$FF,$81,$81,$81,$81,$81,$FF,$00,$00,$7E,$42,$42,$42,$42,$7E", text);
    }

    [Fact]
    public void IsCharsDocument_DetectsSegment()
    {
        Assert.True(ChrAssembly.IsCharsDocument(".segment \"CHARS\"\n.byte $00"));
        Assert.False(ChrAssembly.IsCharsDocument(".segment \"CODE\"\n.byte $00"));
    }
}
