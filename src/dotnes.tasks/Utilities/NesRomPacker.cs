using System.Text;

namespace dotnes;

/// <summary>
/// Packs an iNES ROM from raw PRG bytes, optional CHR bytes, and header metadata.
/// Used by <c>NESPackMode=SoftCpu</c> to pack lowered PRG + CHR + exact header.
/// </summary>
static class NesRomPacker
{
    /// <summary>
    /// Pack using an exact 16-byte iNES header (and optional trainer) from the original ROM.
    /// Preferred for decompiler rebuilds — preserves NES 2.0 / trailer bytes that MSBuild props omit.
    /// </summary>
    public static void WriteExact(
        Stream stream,
        byte[] header,
        byte[]? trainer,
        byte[] prgRom,
        byte[]? chrRom,
        ILogger? logger = null)
    {
        logger ??= new NullLogger();

        if (header is null || header.Length != 16)
            throw new InvalidOperationException("Exact iNES header must be exactly 16 bytes.");
        if (header[0] != 'N' || header[1] != 'E' || header[2] != 'S' || header[3] != 0x1A)
            throw new InvalidOperationException("Exact iNES header is missing the NES\\x1A magic.");

        int prgBanks = header[4];
        int chrBanks = header[5];
        byte flags6 = header[6];
        bool hasTrainer = (flags6 & 0x04) != 0;

        int expectedPrg = checked(prgBanks * NESWriter.PRG_ROM_BLOCK_SIZE);
        if (prgRom.Length != expectedPrg)
        {
            throw new InvalidOperationException(
                $"Raw PRG size ({prgRom.Length} bytes) does not match header PRG banks={prgBanks} ({expectedPrg} bytes).");
        }

        byte[] trainerImage = Array.Empty<byte>();
        if (hasTrainer)
        {
            trainer ??= Array.Empty<byte>();
            if (trainer.Length != 512)
                throw new InvalidOperationException($"Trainer flag set but trainer is {trainer.Length} bytes (expected 512).");
            trainerImage = trainer;
        }
        else if (trainer is { Length: > 0 })
        {
            throw new InvalidOperationException("Trainer bytes were provided but header Flags6 has no trainer bit.");
        }

        byte[] chrImage = Array.Empty<byte>();
        if (chrBanks > 0)
        {
            int expectedChr = checked(chrBanks * NESWriter.CHR_ROM_BLOCK_SIZE);
            chrRom ??= Array.Empty<byte>();
            if (chrRom.Length > expectedChr)
            {
                throw new InvalidOperationException(
                    $"CHR data ({chrRom.Length} bytes) exceeds header CHR banks={chrBanks} ({expectedChr} bytes).");
            }

            chrImage = new byte[expectedChr];
            Array.Copy(chrRom, chrImage, chrRom.Length);
        }
        else if (chrRom is { Length: > 0 })
        {
            throw new InvalidOperationException("CHR data was provided but header CHR banks=0 (CHR RAM mode).");
        }

        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        int mapper = ((flags6 >> 4) & 0x0F) | (header[7] & 0xF0);
        logger.WriteLine($"Writing exact iNES header (mapper={mapper}, PRG={prgBanks}, CHR={chrBanks})...");
        writer.Write(header);
        if (trainerImage.Length > 0)
        {
            logger.WriteLine($"Writing 512-byte trainer...");
            writer.Write(trainerImage);
        }

        logger.WriteLine($"Writing PRG ROM ({prgRom.Length} bytes)...");
        writer.Write(prgRom);
        if (chrImage.Length > 0)
        {
            logger.WriteLine($"Writing CHR ROM ({chrImage.Length} bytes)...");
            writer.Write(chrImage);
        }
        else
        {
            logger.WriteLine($"CHR RAM mode (no CHR ROM)...");
        }

        writer.Flush();
        logger.WriteLine($"ROM complete. Total size: {stream.Length} bytes");
    }

    public static void Write(
        Stream stream,
        byte[] prgRom,
        byte[]? chrRom,
        int prgBanks,
        int chrBanks,
        int mapper,
        string mirroring,
        bool battery,
        ILogger? logger = null,
        byte[]? exactHeader = null,
        byte[]? trainer = null)
    {
        if (exactHeader is { Length: 16 })
        {
            WriteExact(stream, exactHeader, trainer, prgRom, chrRom, logger);
            return;
        }

        logger ??= new NullLogger();

        if (prgBanks < 1 || prgBanks > byte.MaxValue)
            throw new InvalidOperationException($"NESPrgBanks must be between 1 and {byte.MaxValue}.");
        if (chrBanks < 0 || chrBanks > byte.MaxValue)
            throw new InvalidOperationException($"NESChrBanks must be between 0 and {byte.MaxValue}.");
        if (mapper < 0 || mapper > byte.MaxValue)
            throw new InvalidOperationException($"NESMapper must be between 0 and {byte.MaxValue}.");

        int expectedPrg = checked(prgBanks * NESWriter.PRG_ROM_BLOCK_SIZE);
        if (prgRom.Length != expectedPrg)
        {
            throw new InvalidOperationException(
                $"Raw PRG size ({prgRom.Length} bytes) does not match NESPrgBanks={prgBanks} ({expectedPrg} bytes).");
        }

        byte[] chrImage = Array.Empty<byte>();
        if (chrBanks > 0)
        {
            int expectedChr = checked(chrBanks * NESWriter.CHR_ROM_BLOCK_SIZE);
            chrRom ??= Array.Empty<byte>();
            if (chrRom.Length > expectedChr)
            {
                throw new InvalidOperationException(
                    $"CHR data ({chrRom.Length} bytes) exceeds NESChrBanks={chrBanks} ({expectedChr} bytes).");
            }

            chrImage = new byte[expectedChr];
            Array.Copy(chrRom, chrImage, chrRom.Length);
        }
        else if (chrRom is { Length: > 0 })
        {
            throw new InvalidOperationException("CHR data was provided but NESChrBanks=0 (CHR RAM mode).");
        }

        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        logger.WriteLine($"Writing iNES header (mapper={mapper}, PRG={prgBanks}, CHR={chrBanks})...");
        writer.Write('N');
        writer.Write('E');
        writer.Write('S');
        writer.Write((byte)0x1A);
        writer.Write((byte)prgBanks);
        writer.Write((byte)chrBanks);
        byte flags6 = (byte)((string.Equals(mirroring, "Vertical", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            | (battery ? 0x02 : 0)
            | ((mapper & 0x0F) << 4));
        writer.Write(flags6);
        writer.Write((byte)(mapper & 0xF0));
        for (int i = 0; i < 8; i++)
            writer.Write((byte)0);

        logger.WriteLine($"Writing PRG ROM ({prgRom.Length} bytes)...");
        writer.Write(prgRom);
        if (chrImage.Length > 0)
        {
            logger.WriteLine($"Writing CHR ROM ({chrImage.Length} bytes)...");
            writer.Write(chrImage);
        }
        else
        {
            logger.WriteLine($"CHR RAM mode (no CHR ROM)...");
        }

        writer.Flush();
        logger.WriteLine($"ROM complete. Total size: {stream.Length} bytes");
    }

    /// <summary>
    /// Collects CHARS segments from assembly readers, padding each to an 8 KiB boundary.
    /// </summary>
    public static byte[] GatherChrData(IEnumerable<AssemblyReader> assemblyFiles)
    {
        var chrBytes = new List<byte>();
        foreach (var assemblyFile in assemblyFiles)
        {
            foreach (var segment in assemblyFile.GetSegments())
            {
                if (segment.Name != "CHARS")
                    continue;

                chrBytes.AddRange(segment.Bytes);
                int remainder = segment.Bytes.Length % NESWriter.CHR_ROM_BLOCK_SIZE;
                if (remainder > 0)
                    chrBytes.AddRange(new byte[NESWriter.CHR_ROM_BLOCK_SIZE - remainder]);
            }
        }

        return chrBytes.ToArray();
    }
}
