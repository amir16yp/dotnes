namespace dotnes;

/// <summary>
/// NES CHR tile codec: 8×8 pixels, 2bpp planar (16 bytes per tile).
/// Pixel values are palette indices 0–3.
/// </summary>
public static class ChrCodec
{
    public const int TileSize = 8;
    public const int BytesPerTile = 16;

    /// <summary>
    /// Decode one tile from <paramref name="data"/> at <paramref name="offset"/> into an 8×8 grid
    /// (row-major, values 0–3).
    /// </summary>
    public static byte[,] DecodeTile(ReadOnlySpan<byte> data, int offset = 0)
    {
        if (offset < 0 || offset + BytesPerTile > data.Length)
            throw new ArgumentOutOfRangeException(nameof(offset));

        var pixels = new byte[TileSize, TileSize];
        for (int y = 0; y < TileSize; y++)
        {
            byte plane0 = data[offset + y];
            byte plane1 = data[offset + 8 + y];
            for (int x = 0; x < TileSize; x++)
            {
                int bit = 7 - x;
                byte lo = (byte)((plane0 >> bit) & 1);
                byte hi = (byte)((plane1 >> bit) & 1);
                pixels[y, x] = (byte)((hi << 1) | lo);
            }
        }
        return pixels;
    }

    /// <summary>
    /// Encode an 8×8 pixel grid (values 0–3) into 16 NES CHR bytes.
    /// </summary>
    public static byte[] EncodeTile(byte[,] pixels)
    {
        if (pixels.GetLength(0) != TileSize || pixels.GetLength(1) != TileSize)
            throw new ArgumentException("Tile must be 8×8.", nameof(pixels));

        var data = new byte[BytesPerTile];
        for (int y = 0; y < TileSize; y++)
        {
            byte plane0 = 0;
            byte plane1 = 0;
            for (int x = 0; x < TileSize; x++)
            {
                byte v = pixels[y, x];
                if (v > 3)
                    throw new ArgumentOutOfRangeException(nameof(pixels), $"Pixel ({x},{y}) value {v} exceeds 0–3.");
                int bit = 7 - x;
                if ((v & 1) != 0)
                    plane0 |= (byte)(1 << bit);
                if ((v & 2) != 0)
                    plane1 |= (byte)(1 << bit);
            }
            data[y] = plane0;
            data[8 + y] = plane1;
        }
        return data;
    }

    /// <summary>
    /// Decode all complete tiles from CHR ROM bytes.
    /// </summary>
    public static IReadOnlyList<byte[,]> DecodeTiles(ReadOnlySpan<byte> data)
    {
        int count = data.Length / BytesPerTile;
        var tiles = new byte[count][,];
        for (int i = 0; i < count; i++)
            tiles[i] = DecodeTile(data, i * BytesPerTile);
        return tiles;
    }

    /// <summary>
    /// Encode a list of 8×8 tiles into a contiguous CHR byte buffer.
    /// </summary>
    public static byte[] EncodeTiles(IReadOnlyList<byte[,]> tiles)
    {
        var data = new byte[tiles.Count * BytesPerTile];
        for (int i = 0; i < tiles.Count; i++)
        {
            var encoded = EncodeTile(tiles[i]);
            encoded.CopyTo(data, i * BytesPerTile);
        }
        return data;
    }

    /// <summary>
    /// Flatten an 8×8 tile to a length-64 row-major array (for JSON / LSP).
    /// </summary>
    public static byte[] FlattenTile(byte[,] pixels)
    {
        var flat = new byte[TileSize * TileSize];
        for (int y = 0; y < TileSize; y++)
            for (int x = 0; x < TileSize; x++)
                flat[y * TileSize + x] = pixels[y, x];
        return flat;
    }

    /// <summary>
    /// Rebuild an 8×8 tile from a length-64 row-major array.
    /// </summary>
    public static byte[,] UnflattenTile(IReadOnlyList<byte> flat)
    {
        if (flat.Count != TileSize * TileSize)
            throw new ArgumentException("Flat tile must have 64 values.", nameof(flat));

        var pixels = new byte[TileSize, TileSize];
        for (int y = 0; y < TileSize; y++)
            for (int x = 0; x < TileSize; x++)
                pixels[y, x] = flat[y * TileSize + x];
        return pixels;
    }
}
