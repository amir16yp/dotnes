using Xunit.Abstractions;

namespace dotnes.tests;

public class DecompilerTests
{
    readonly ILogger _logger;

    public DecompilerTests(ITestOutputHelper output) => _logger = new XUnitLogger(output);

    [Fact]
    public void NESRomReader_ParsesHeader()
    {
        var romBytes = GetVerifiedRom("hello");
        var reader = new NESRomReader(romBytes);

        Assert.Equal(2, reader.PrgBanks);
        Assert.Equal(1, reader.ChrBanks);
        Assert.Equal(0, reader.Mapper);
        Assert.False(reader.VerticalMirroring);
        Assert.False(reader.HasBattery);
        Assert.Equal(0x8000, reader.ResetVector);
        Assert.Equal(32768, reader.PrgRom.Length); // 2 * 16KB
        Assert.Equal(8192, reader.ChrRom.Length);  // 1 * 8KB
    }

    [Theory]
    [InlineData("statusbar", true)]   // verticalMirroring=true
    [InlineData("horizscroll", true)] // verticalMirroring=true
    public void NESRomReader_VerticalMirroring(string name, bool expectedMirroring)
    {
        var romBytes = GetVerifiedRom(name);
        var reader = new NESRomReader(romBytes);

        Assert.Equal(expectedMirroring, reader.VerticalMirroring);
    }

    [Fact]
    public void NESRomReader_ChrRamMode()
    {
        var romBytes = GetVerifiedRom("transtable");
        var reader = new NESRomReader(romBytes);

        Assert.Equal(0, reader.ChrBanks);
        Assert.Empty(reader.ChrRom);
    }

    [Fact]
    public void NESRomReader_ChrRomExtraction()
    {
        var romBytes = GetVerifiedRom("hello");
        var reader = new NESRomReader(romBytes);

        Assert.Equal(8192, reader.ChrRom.Length);
        var chrAsm = reader.GenerateChrAssembly();
        Assert.StartsWith(".segment \"CHARS\"", chrAsm);
        Assert.Contains("$", chrAsm); // Should have hex bytes
    }

    [Fact]
    public void NESRomReader_InterruptVectors()
    {
        var romBytes = GetVerifiedRom("hello");
        var reader = new NESRomReader(romBytes);

        // Reset vector should point to $8000 (start of PRG ROM)
        Assert.Equal(0x8000, reader.ResetVector);
        // NMI and IRQ vectors should be in PRG ROM range
        Assert.True(reader.NmiVector >= 0x8000);
        Assert.True(reader.IrqVector >= 0x8000);
    }

    [Fact]
    public void NESRomReader_InvalidRom_Throws()
    {
        var badData = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05 };
        Assert.Throws<InvalidOperationException>(() => new NESRomReader(badData));
    }

    [Fact]
    public void Decompiler_Shoot2_RecognizesPoke()
    {
        var romBytes = GetVerifiedRom("shoot2");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // shoot2 uses poke() for APU initialization (silence channels)
        Assert.Contains("poke(APU_PULSE1_CTRL, 0x30);", code);
        Assert.Contains("poke(APU_PULSE2_CTRL, 0x30);", code);
        Assert.Contains("poke(APU_TRIANGLE_CTRL, 0x80);", code);
        Assert.Contains("poke(APU_NOISE_CTRL, 0x30);", code);
        Assert.Contains("poke(APU_STATUS, 0x0F);", code);
    }

    [Fact]
    public void Decompiler_Shoot2_RecognizesPeek()
    {
        var romBytes = GetVerifiedRom("shoot2");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // shoot2 uses peek() for reading random seed
        Assert.Contains("peek(", code);
    }

    [Fact]
    public void Decompiler_Hello_ProducesCorrectOutput()
    {
        var romBytes = GetVerifiedRom("hello");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // Verify key NESLib calls are present (colors recovered as NESColor names)
        Assert.Contains("pal_col(0, NESColor.DarkBlue);", code);
        Assert.Contains("pal_col(1, NESColor.Magenta);", code);
        Assert.Contains("pal_col(2, NESColor.LightGray);", code);
        Assert.Contains("pal_col(3, NESColor.White);", code);
        Assert.Contains("vram_adr(NTADR_A(2, 2));", code);
        Assert.Contains("vram_write(\"HELLO, .NET!\");", code);
        Assert.Contains("ppu_on_all();", code);
        Assert.Contains("while (true) ;", code);
    }

    [Fact]
    public void Decompiler_Hello_GeneratesCsproj()
    {
        var romBytes = GetVerifiedRom("hello");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);
        decompiler.Decompile(); // Must decompile first to populate state

        var csproj = decompiler.GenerateCsproj("hello");

        Assert.Contains("<OutputType>Exe</OutputType>", csproj);
        Assert.Contains("<TargetFramework>net10.0</TargetFramework>", csproj);
        Assert.Contains("<Using Include=\"NES\" />", csproj);
        Assert.Contains("<Using Include=\"NES.NESLib\" Static=\"true\" />", csproj);
        Assert.Contains("<Using Remove=\"NES.NESColor\" />", csproj);
        Assert.DoesNotContain("NES.NESColor\" Static=\"true\"", csproj);
        Assert.Contains("dotnes", csproj);
        // hello uses default settings so no mapper/prg/chr overrides
        Assert.DoesNotContain("NESMapper", csproj);
    }

    [Fact]
    public void Decompiler_Music_FindsMainAfterMusicSubroutines()
    {
        var romBytes = GetVerifiedRom("music");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // Music sample has music_tick/start_music subroutines between built-ins and main.
        // The decompiler must find main at its actual address (not builtInsEnd).
        // Verify key NESLib calls that appear at the start of music's main():
        Assert.Contains("pal_col(0, NESColor.DarkAzure);", code);
        Assert.Contains("vram_write(\"NOW PLAYING\");", code);
        Assert.Contains("ppu_on_all();", code);
    }

    [Fact]
    public void Decompiler_Shoot2_UxROM_FindsMain()
    {
        var romBytes = GetVerifiedRom("shoot2");
        var rom = new NESRomReader(romBytes);

        Assert.Equal(2, rom.Mapper); // UxROM

        var decompiler = new Decompiler(rom, _logger);
        var code = decompiler.Decompile();

        // shoot2 uses UxROM mapper - decompiler must correctly identify main
        Assert.Contains("ppu_off();", code);
        Assert.Contains("oam_clear();", code);
    }

    [Fact]
    public void Decompiler_Shoot2_RecognizesSetRand()
    {
        var romBytes = GetVerifiedRom("shoot2");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // shoot2 calls set_rand(42) near the start of main
        Assert.Contains("set_rand(", code);
    }

    [Fact]
    public void Decompiler_Attributetable_RecoversPalBgData()
    {
        var romBytes = GetVerifiedRom("attributetable");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // The attributetable sample uses pal_bg with a 16-byte palette
        Assert.Contains("pal_bg(palette", code);
        Assert.Contains("new byte[]", code);
        // Verify palette values recovered as NESColor names
        Assert.Contains("NESColor.DarkViolet", code); // 0x03
        Assert.Contains("NESColor.Azure", code);      // 0x11
        Assert.Contains("NESColor.White", code);      // 0x30
        Assert.Contains("NESColor.LightOrange", code); // 0x27
    }

    [Fact]
    public void Decompiler_Shoot2_RecoversPalBgAndPalSprData()
    {
        var romBytes = GetVerifiedRom("shoot2");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // shoot2 uses both pal_bg and pal_spr with palette byte arrays
        Assert.Contains("pal_bg(palette", code);
        Assert.Contains("pal_spr(palette", code);
        // Verify pal_bg / pal_spr palette data recovered as NESColor names
        Assert.Contains("NESColor.Black", code);
        Assert.Contains("NESColor.White", code);
        Assert.Contains("NESColor.Gray", code);
    }

    [Fact]
    public void Decompiler_Statusbar_CsprojHasVerticalMirroring()
    {
        var romBytes = GetVerifiedRom("statusbar");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);
        decompiler.Decompile();

        var csproj = decompiler.GenerateCsproj("statusbar");

        Assert.Contains("<NESMirroring>Vertical</NESMirroring>", csproj);
    }

    [Fact]
    public void Decompiler_Shoot2_DetectsArrayDeclarations()
    {
        var romBytes = GetVerifiedRom("shoot2");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // shoot2 has 13 byte[] arrays for game state (bullets, enemies, stars, explosions).
        // Sizes prefer for-loop bounds when available (smarter than gap-only).
        Assert.Contains("byte[] array_0335 = new byte[4];", code);
        Assert.Contains("byte[] array_0339 = new byte[4];", code);
        Assert.Contains("byte[] array_033D = new byte[4];", code);
        Assert.Contains("byte[] array_0341 = new byte[6];", code);
        Assert.Contains("byte[] array_0347 = new byte[6];", code);
        Assert.Contains("byte[] array_034D = new byte[6];", code);
        Assert.Contains("byte[] array_0353 = new byte[6];", code);
        Assert.Contains("byte[] array_0359 = new byte[8];", code);
        Assert.Contains("byte[] array_0361 = new byte[8];", code);
        Assert.Contains("byte[] array_0369 = new byte[8];", code);
        Assert.Contains("byte[] array_0371 = new byte[3];", code);
        Assert.Contains("byte[] array_0374 = new byte[3];", code);
        Assert.Contains("byte[] array_0377 = new byte[3];", code);
    }

    [Fact]
    public void ArrayRecovery_PrefersForLoopBoundOverGap()
    {
        var instructions = new List<(ushort, byte, byte?, byte?)>
        {
            (0x8000, 0xA9, 0x00, null),
            (0x8002, 0x8D, 0x25, 0x03),
            (0x8005, 0xAE, 0x25, 0x03),
            (0x8008, 0xBD, 0x35, 0x03),
            (0x800B, 0x9D, 0x35, 0x03),
            (0x800E, 0xEE, 0x25, 0x03),
            (0x8011, 0xAD, 0x25, 0x03),
            (0x8014, 0xC9, 0x04, null),
            (0x8016, 0x90, 0xED, null),
            (0x8018, 0xBD, 0x3D, 0x03),
        };

        var recovery = new ArrayRecovery();
        var loops = new List<(ushort, byte, byte, int, int, int, int)>
        {
            (0x0325, 0, 4, 0, 2, 5, 9),
        };
        recovery.Analyze(instructions, loops, 0x0325);

        var arr = Assert.Single(recovery.RamArrays, a => a.Base == 0x0335);
        Assert.Equal(4, arr.Size);
        Assert.Equal(4, arr.LoopBound);
    }

    [Fact]
    public void Decompiler_Hello_NoArrayDeclarations()
    {
        var romBytes = GetVerifiedRom("hello");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // hello has no indexed-addressing byte[] arrays
        Assert.DoesNotContain("new byte[", code);
    }

    [Fact]
    public void Decompiler_Attributetable_RecoversPalBgByteArray()
    {
        var romBytes = GetVerifiedRom("attributetable");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // pal_bg should have a byte[] variable declaration with NESColor-named palette data
        Assert.Contains("byte[] palette0 = new byte[] { NESColor.", code);
        Assert.Contains("pal_bg(palette0);", code);
        // Should NOT be commented out
        Assert.DoesNotContain("// pal_bg", code);
    }

    [Fact]
    public void Decompiler_Attributetable_RecoversVramWriteByteArray()
    {
        var romBytes = GetVerifiedRom("attributetable");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // vram_write should have a byte[] variable with the attribute table data
        Assert.Contains("byte[] data1 = new byte[]", code);
        Assert.Contains("vram_write(data1);", code);
        // First bytes of the attribute table
        Assert.Contains("0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00", code);
        // Should NOT have placeholder comments
        Assert.DoesNotContain("vram_write(/*", code);
    }

    [Fact]
    public void Decompiler_Attributetable_RecoversVramFillArgs()
    {
        var romBytes = GetVerifiedRom("attributetable");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // vram_fill should have actual fill value and count
        Assert.Contains("vram_fill(0x16, 960);", code);
        // Should NOT have placeholder
        Assert.DoesNotContain("vram_fill(/*", code);
    }

    [Fact]
    public void Decompiler_Attributetable_HasWhileTrue()
    {
        var romBytes = GetVerifiedRom("attributetable");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        Assert.Contains("while (true) ;", code);
    }

    [Fact]
    public void Decompiler_Attributetable_FullRoundTrip()
    {
        var romBytes = GetVerifiedRom("attributetable");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // Verify the complete sequence of API calls matches the original source
        Assert.Contains("pal_bg(palette0);", code);
        Assert.Contains("vram_adr(NTADR_A(0, 0));", code);
        Assert.Contains("vram_fill(0x16, 960);", code);
        Assert.Contains("vram_write(data1);", code);
        Assert.Contains("ppu_on_all();", code);
        Assert.Contains("while (true) ;", code);
    }

    static byte[] GetVerifiedRom(string name)
    {
        // Verified ROM binaries are stored alongside the test source files
        var path = Path.Combine(FindTestSourceDirectory(), $"TranspilerTests.Write.{name}.verified.bin");
        return File.ReadAllBytes(path);
    }

    [Fact]
    public void Decompiler_Shoot2_FindsGameLoop()
    {
        var romBytes = GetVerifiedRom("shoot2");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // Verify while(true) game loop is recovered (backward JMP pattern)
        Assert.Contains("while (true) ;", code);

        // Verify init code is present
        Assert.Contains("ppu_off();", code);
        Assert.Contains("oam_clear();", code);
        Assert.Contains("ppu_on_all();", code);

        // Verify game loop NESLib calls are present
        Assert.Contains("ppu_wait_nmi();", code);
        Assert.Contains("pal_spr_bright(", code);
    }

    [Fact]
    public void Decompiler_Shoot2_CsprojHasMapper()
    {
        var romBytes = GetVerifiedRom("shoot2");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);
        decompiler.Decompile();

        var csproj = decompiler.GenerateCsproj("shoot2");

        Assert.Contains("<NESMapper>2</NESMapper>", csproj);
        Assert.Contains("<NESChrBanks>0</NESChrBanks>", csproj);
    }

    [Theory]
    [InlineData("animation")]
    [InlineData("pong")]
    [InlineData("snake")]
    public void Decompiler_GameLoop_RecoveredFromBackwardJmp(string name)
    {
        var romBytes = GetVerifiedRom(name);
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // All these samples have while(true){body} game loops
        // that should be recovered via backward JMP detection
        Assert.Contains("while (true) ;", code);
    }

    [Fact]
    public void Decompiler_Scroll_RecoverLocalVariables()
    {
        // scroll has scroll_y local at $0325 — verify it's recovered as a variable, not poke/peek
        var romBytes = GetVerifiedRom("scroll");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // Should declare a local variable for $0325
        Assert.Contains("byte var_0325 = 0;", code);
        // Should use variable assignment, not poke
        Assert.Contains("var_0325 = ", code);
        Assert.DoesNotContain("poke(0x0325", code);
    }

    [Fact]
    public void Decompiler_Shoot2_RecoverLocalVariables()
    {
        // shoot2 has 20+ locals at $0325+ — verify they're recovered as variables
        var romBytes = GetVerifiedRom("shoot2");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // Should have local variable declarations
        Assert.Contains("byte var_0325 = 0;", code);
        // Should NOT use poke for local variable addresses
        Assert.DoesNotContain("poke(0x0325", code);
    }

    [Fact]
    public void Decompiler_Shoot2_RecoverIfBlocks()
    {
        // shoot2 has many conditionals — verify if blocks are recovered
        var romBytes = GetVerifiedRom("shoot2");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // fire_cooldown check: LDA $0327 / BEQ skip → if (var_0327 != 0)
        Assert.Contains("if (var_0327 != 0)", code);
        // Should contain opening and closing braces for if blocks
        Assert.Contains("{", code);
        Assert.Contains("}", code);
    }

    [Fact]
    public void Decompiler_Shoot2_RecoverCmpBranch()
    {
        // shoot2 uses CMP #N / BCC for loop comparisons and boundary checks
        var romBytes = GetVerifiedRom("shoot2");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // Loop-like patterns use LDA $local / CMP #N / BCC
        // Verify at least one comparison-based if block exists
        Assert.Contains("if (var_", code);
    }

    [Fact]
    public void Decompiler_Scroll_RecoverIfBlock()
    {
        // scroll has a conditional check on scroll_y ($0326)
        var romBytes = GetVerifiedRom("scroll");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // scroll uses LDA $0326 / BEQ skip → if (var_0326 != 0)
        Assert.Contains("if (var_0326 != 0)", code);
        Assert.Contains("{", code);
        Assert.Contains("}", code);
    }

    [Fact]
    public void Decompiler_Shoot2_RecoverPadPollResultStorage()
    {
        // shoot2 calls pad_poll(0) and stores result: LDA #$00 / JSR pad_poll / STA $0380
        var romBytes = GetVerifiedRom("shoot2");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // pad_poll result should be stored to a local variable
        Assert.Contains("var_0380 = pad_poll(0);", code);
        // Variable storing pad_poll should be typed as PAD, not byte
        Assert.Contains("PAD var_0380 = 0;", code);
        Assert.DoesNotContain("byte var_0380", code);
    }

    [Fact]
    public void Decompiler_Shoot2_RecoverPadMasking()
    {
        // shoot2 uses LDA $0380 / AND #$01 / BEQ → if ((var_0380 & PAD.A) != 0)
        // and pad_dpad_x emits AND #$80 → if ((var_0380 & PAD.RIGHT) != 0)
        var romBytes = GetVerifiedRom("shoot2");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // PAD.A masking (AND #$01)
        Assert.Contains("if ((var_0380 & PAD.A) != 0)", code);
        // PAD.RIGHT masking (AND #$80)
        Assert.Contains("if ((var_0380 & PAD.RIGHT) != 0)", code);
    }

    [Fact]
    public void Decompiler_Shoot2_RecognizesOamSpr()
    {
        // shoot2 has oam_spr calls with local variables, immediates, and array indexing
        // Pattern: JSR decsp4 / (LDA val / STA ($22),Y) × 4 / LDA val / JSR oam_spr
        var romBytes = GetVerifiedRom("shoot2");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // Verify oam_spr calls are recovered with 5 arguments and assignment
        Assert.Contains("oam_spr(", code);
        // Should NOT have oam_spr as an unknown comment
        Assert.DoesNotContain("// oam_spr(", code);
        // First oam_spr call uses local vars + immediates and assigns result back
        // oam_off = oam_spr(player_x, player_y, SPR_PLAYER, 0, oam_off)
        Assert.Contains("= oam_spr(var_", code);
        // All oam_spr calls should have exactly 5 comma-separated arguments
        foreach (var line in code.Split('\n'))
        {
            if (!line.Contains("oam_spr(")) continue;
            var argsStr = line.Substring(line.IndexOf("oam_spr(") + 8);
            argsStr = argsStr.Substring(0, argsStr.IndexOf(')'));
            var args = argsStr.Split(',');
            Assert.Equal(5, args.Length);

            // For any array-indexed argument, ensure it uses the array_XXXX identifier
            // (matching GenerateCSharp declarations) and has a resolved index variable
            foreach (var rawArg in args)
            {
                var arg = rawArg.Trim();
                var openBracket = arg.IndexOf('[');
                var closeBracket = arg.IndexOf(']');
                if (openBracket < 0 || closeBracket <= openBracket)
                    continue;

                var baseIdentifier = arg.Substring(0, openBracket).Trim();
                var indexExpression = arg.Substring(openBracket + 1, closeBracket - openBracket - 1);

                Assert.StartsWith("array_", baseIdentifier);
                Assert.False(string.IsNullOrWhiteSpace(indexExpression));
            }
        }
    }

    [Fact]
    public void Decompiler_Shoot2_EmitsUnknownAssemblyComments()
    {
        // shoot2 has complex game logic that can't be mapped back to NESLib calls
        var romBytes = GetVerifiedRom("shoot2");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // Should contain unknown assembly comment blocks
        Assert.Contains("// Unknown 6502 assembly at $", code);
        // Should contain disassembled instructions inside comments
        Assert.Contains("//   ", code);
    }

    [Fact]
    public void Decompiler_Shoot2_RecoverForLoops()
    {
        // shoot2 has many for loops for array initialization and game logic
        var romBytes = GetVerifiedRom("shoot2");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // Should contain for loop syntax with proper structure
        Assert.Contains("for (byte", code);

        // Verify specific for-loop limits matching shoot2's constants:
        // MAX_BULLETS=4, MAX_ENEMIES=6, MAX_STARS=8, MAX_EXPLOSIONS=3
        Assert.Matches(@"for \(byte\s+var_\w+\s*=\s*0;\s*var_\w+\s*<\s*4;\s*var_\w+\+\+\)", code);  // MAX_BULLETS
        Assert.Matches(@"for \(byte\s+var_\w+\s*=\s*0;\s*var_\w+\s*<\s*6;\s*var_\w+\+\+\)", code);  // MAX_ENEMIES
        Assert.Matches(@"for \(byte\s+var_\w+\s*=\s*0;\s*var_\w+\s*<\s*8;\s*var_\w+\+\+\)", code);  // MAX_STARS
        Assert.Matches(@"for \(byte\s+var_\w+\s*=\s*0;\s*var_\w+\s*<\s*3;\s*var_\w+\+\+\)", code);  // MAX_EXPLOSIONS

        // Should have indented body inside for loops: for header, then '{', then a line starting with 4 spaces
        Assert.Matches(@"for\s*\([^)]*\)\s*\{\s*\r?\n {4}\S", code);
    }

    [Fact]
    public void Decompiler_Shoot2_ForLoopCounterNotDeclaredAtTop()
    {
        // For-loop counter variables should NOT appear as top-level declarations
        // since they are declared in the for-loop header
        var romBytes = GetVerifiedRom("shoot2");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // The for loops should declare their own counter variables
        // Verify that at least one for loop with proper structure exists
        Assert.Matches(@"for \(byte (var_\w+) = 0; \1 < \d+; \1\+\+\)", code);
    }

    [Fact]
    public void Decompiler_Hello_NoPadVariables()
    {
        // hello has no pad_poll — verify no PAD-typed variables are generated
        var romBytes = GetVerifiedRom("hello");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // hello doesn't use pad_poll, so no PAD variables should appear
        Assert.DoesNotContain("PAD ", code);
        Assert.DoesNotContain("pad_poll", code);
    }

    [Fact]
    public void Decompiler_Hello_NoIfBlocks()
    {
        // hello has no conditionals — verify no if blocks are generated
        var romBytes = GetVerifiedRom("hello");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        // hello is straight-line code with no branches
        Assert.DoesNotContain("if (", code);
    }

    [Fact]
    public void Decompiler_Hello_NoForLoops()
    {
        // hello has no for loops — verify none are detected
        var romBytes = GetVerifiedRom("hello");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        Assert.DoesNotContain("for (byte", code);
    }

    [Fact]
    public void Decompiler_Hello_NoUnknownAssemblyComments()
    {
        // hello is fully mapped to NESLib calls — no unknown instructions
        var romBytes = GetVerifiedRom("hello");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);

        var code = decompiler.Decompile();

        Assert.DoesNotContain("// Unknown 6502 assembly", code);
    }

    static string FindTestSourceDirectory()
    {
        // Navigate from the test output directory to the source directory
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "dotnes.tests");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "TranspilerTests.cs")))
                return candidate;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not find test source directory");
    }

    [Fact]
    public void NESRomReader_MultiBankChr_GeneratesSingleBank()
    {
        var romBytes = GetVerifiedRom("slideshow");
        var reader = new NESRomReader(romBytes);

        Assert.Equal(2, reader.ChrBanks);
        Assert.Equal(16384, reader.ChrRom.Length); // 2 * 8KB

        // Generate single bank
        var bank0 = reader.GenerateChrAssembly(0);
        var bank1 = reader.GenerateChrAssembly(1);

        Assert.StartsWith(".segment \"CHARS\"", bank0);
        Assert.StartsWith(".segment \"CHARS\"", bank1);

        // Each bank should have 8KB = 512 lines of 16 bytes
        int bank0Lines = bank0.Split('\n').Count(l => l.StartsWith(".byte"));
        int bank1Lines = bank1.Split('\n').Count(l => l.StartsWith(".byte"));
        Assert.Equal(512, bank0Lines);
        Assert.Equal(512, bank1Lines);

        // Banks should differ (different tile data)
        Assert.NotEqual(bank0, bank1);
    }

    [Fact]
    public void NESRomReader_MultiBankChr_InvalidBankReturnsEmpty()
    {
        var romBytes = GetVerifiedRom("slideshow");
        var reader = new NESRomReader(romBytes);

        Assert.Empty(reader.GenerateChrAssembly(-1));
        Assert.Empty(reader.GenerateChrAssembly(2));
    }

    [Fact]
    public void NESRomReader_MultiBankChr_AllBanksMatchFull()
    {
        var romBytes = GetVerifiedRom("slideshow");
        var reader = new NESRomReader(romBytes);

        // Full output (no bank parameter) should equal bank0 + bank1 data combined
        var full = reader.GenerateChrAssembly();
        int fullLines = full.Split('\n').Count(l => l.StartsWith(".byte"));
        Assert.Equal(1024, fullLines); // 2 * 512 lines
    }

    [Fact]
    public void Decompiler_Shoot2_ExtractsChrRamTileData()
    {
        var romBytes = GetVerifiedRom("shoot2");
        var rom = new NESRomReader(romBytes);

        Assert.Equal(0, rom.ChrBanks); // CHR RAM

        var decompiler = new Decompiler(rom, _logger);
        decompiler.Decompile();

        var chrRamData = decompiler.GetChrRamTileData();
        Assert.NotEmpty(chrRamData);

        // shoot2 uploads tiles to $1000 (sprites) and $0000 (BG)
        var addresses = chrRamData.Select(d => d.PpuAddress).ToList();
        Assert.Contains((ushort)0x1000, addresses);
        Assert.Contains((ushort)0x0000, addresses);

        // All uploads should have non-zero data
        foreach (var (_, data) in chrRamData)
        {
            Assert.True(data.Length > 0);
            Assert.True(data.Any(b => b != 0), "Tile data should not be all zeros");
        }
    }

    [Fact]
    public void Decompiler_Transtable_ExtractsChrRamTileData()
    {
        var romBytes = GetVerifiedRom("transtable");
        var rom = new NESRomReader(romBytes);

        Assert.Equal(0, rom.ChrBanks); // CHR RAM

        var decompiler = new Decompiler(rom, _logger);
        decompiler.Decompile();

        var chrRamData = decompiler.GetChrRamTileData();
        Assert.NotEmpty(chrRamData);

        // transtable uploads tiles to $0000 (pattern table 0) via vram_adr(0x0)
        var addresses = chrRamData.Select(d => d.PpuAddress).ToList();
        Assert.Contains((ushort)0x0000, addresses);
    }

    [Fact]
    public void Decompiler_Hello_NoChrRamData()
    {
        var romBytes = GetVerifiedRom("hello");
        var rom = new NESRomReader(romBytes);

        Assert.Equal(1, rom.ChrBanks); // CHR ROM, not CHR RAM

        var decompiler = new Decompiler(rom, _logger);
        decompiler.Decompile();

        // CHR ROM samples should have no CHR RAM data extracted
        var chrRamData = decompiler.GetChrRamTileData();
        Assert.Empty(chrRamData);
    }

    [Fact]
    public void Decompiler_Shoot2_DetectsUserFunctions()
    {
        var romBytes = GetVerifiedRom("shoot2");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);
        var code = decompiler.Decompile();

        // shoot2 has user-defined functions after main that should be detected
        // and emitted as static void declarations
        Assert.Contains("static void func_", code);
    }

    [Fact]
    public void Decompiler_Shoot2_UserFunctionCallsNotCommented()
    {
        var romBytes = GetVerifiedRom("shoot2");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);
        var code = decompiler.Decompile();

        // User function calls from main should be actual calls, not comments
        // At least one func_ call should appear without a leading "//"
        var lines = code.Split('\n');
        var funcCalls = lines.Where(l => l.TrimStart().StartsWith("func_")).ToList();
        Assert.NotEmpty(funcCalls);
    }

    [Fact]
    public void Decompiler_Shoot2_UserFunctionHasPokeBody()
    {
        var romBytes = GetVerifiedRom("shoot2");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);
        var code = decompiler.Decompile();

        // The inline set_sounds() code writes APU registers with constant values.
        // The decompiler should recover these as poke() calls.
        // poke(APU_PULSE1_CTRL, 0xB6) = duty 50% + constant vol 6 (bullet fire)
        Assert.Contains("poke(APU_PULSE1_CTRL, 0xB6);", code);
        // poke(APU_NOISE_CTRL, 0x05) = envelope decay rate 5 (player hit)
        Assert.Contains("poke(APU_NOISE_CTRL, 0x05);", code);
        // poke(APU_NOISE_CTRL, 0x02) = envelope decay rate 2 (enemy explosion)
        Assert.Contains("poke(APU_NOISE_CTRL, 0x02);", code);
        // poke(APU_TRIANGLE_CTRL, 0xFF) = sustain counter (enemy dive)
        Assert.Contains("poke(APU_TRIANGLE_CTRL, 0xFF);", code);
    }

    [Fact]
    public void Decompiler_Shoot2_UserFunctionsHaveBraces()
    {
        var romBytes = GetVerifiedRom("shoot2");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);
        var code = decompiler.Decompile();

        // Each user function should have an opening and closing brace
        var lines = code.Split('\n').Select(l => l.Trim()).ToArray();
        var funcDecls = lines.Where(l => l.StartsWith("static void func_")).ToList();
        Assert.True(funcDecls.Count > 0, "Should have at least one user function declaration");

        // Each function declaration should be followed by { ... }
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("static void func_"))
            {
                Assert.True(i + 1 < lines.Length && lines[i + 1] == "{",
                    $"Function declaration at line {i} should be followed by opening brace");
            }
        }
    }

    [Fact]
    public void Decompiler_Hello_NoUserFunctions()
    {
        var romBytes = GetVerifiedRom("hello");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);
        var code = decompiler.Decompile();

        // hello sample has no user-defined functions
        Assert.DoesNotContain("static void func_", code);
    }

    [Fact]
    public void Decompiler_Bigsprites_RecognizesOamSize()
    {
        var romBytes = GetVerifiedRom("bigsprites");
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);
        var code = decompiler.Decompile();

        Assert.Contains("oam_size(SpriteSize.Size8x16);", code);
    }

    [Fact]
    public void Decompiler_Hello_UsesTranspilePackMode()
    {
        var rom = new NESRomReader(GetVerifiedRom("hello"));
        var decompiler = new Decompiler(rom, _logger);
        _ = decompiler.Decompile();
        Assert.Equal(PackMode.Transpile, decompiler.ResolvedPackMode);
        Assert.DoesNotContain("NESPackMode", decompiler.GenerateCsproj("hello"));
    }

    [Fact]
    public void Decompiler_ArbitraryRom_UsesRawPrgPackMode()
    {
        byte[] romBytes = CreateMinimalArbitraryRom();
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);
        string code = decompiler.Decompile();

        Assert.Equal(PackMode.RawPrg, decompiler.ResolvedPackMode);
        Assert.Equal(RecoveryStyle.Optimized, decompiler.RecoveryStyle);
        Assert.Contains("optimized soft-CPU recovery", code);
        Assert.Contains("#pragma warning disable CS8321", code);
        Assert.Contains("void reset()", code);
        Assert.Contains("while (true)", code);
        string csproj = decompiler.GenerateCsproj("arb");
        Assert.Contains("<NESPackMode>RawPrg</NESPackMode>", csproj);
        Assert.Contains("<NESRawPrg>prg.bin</NESRawPrg>", csproj);
        Assert.Contains("<NESRawHeader>header.bin</NESRawHeader>", csproj);
        Assert.Contains("<NESPrgBanks>1</NESPrgBanks>", csproj);
        Assert.Contains("<PackageReference Include=\"dotnes\" Version=\"", csproj);
        Assert.Contains("<PackageReference Include=\"dotnes.mesen\" Version=\"", csproj);
        Assert.DoesNotContain("Version=\"*\"", csproj);
        Assert.Contains("<RunAnalyzersDuringBuild>false</RunAnalyzersDuringBuild>", csproj);
        Assert.Contains("<NoWarn>$(NoWarn);CS8321;CS0162;CS0164</NoWarn>", csproj);
        Assert.DoesNotContain("Recovered.cs", csproj);
    }

    [Fact]
    public void Decompiler_ArbitraryRom_Exact_OptsOutOfStructuring()
    {
        byte[] romBytes = CreateMinimalArbitraryRom();
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger, PackModePreference.Auto, RecoveryStyle.Exact);
        string code = decompiler.Decompile();

        Assert.Equal(RecoveryStyle.Exact, decompiler.RecoveryStyle);
        Assert.Contains("exact soft-CPU recovery", code);
        Assert.DoesNotContain("optimized soft-CPU recovery", code);
        Assert.Contains("#pragma warning disable CS8321", code);
        Assert.Contains("void reset()", code);
    }

    [Fact]
    public void Decompiler_RawPrg_RoundTrip_MatchesOriginalRomBytes()
    {
        byte[] original = CreateMinimalArbitraryRom();
        var rom = new NESRomReader(original);
        string dir = Path.Combine(Path.GetTempPath(), "dotnes-rt-" + Guid.NewGuid().ToString("N"));
        try
        {
            new Decompiler(rom, _logger).WriteProject(dir, "arb");
            byte[] prg = File.ReadAllBytes(Path.Combine(dir, "prg.bin"));
            byte[] header = File.ReadAllBytes(Path.Combine(dir, "header.bin"));
            Assert.Equal(rom.PrgRom, prg);
            Assert.Equal(rom.Header, header);

            // Pack the same way TranspileToNES RawPrg does with exact header
            using var ms = new MemoryStream();
            NesRomPacker.Write(ms, prg, rom.ChrRom, rom.PrgBanks, rom.ChrBanks, rom.Mapper,
                rom.VerticalMirroring ? "Vertical" : "Horizontal", rom.HasBattery, _logger,
                exactHeader: header);
            Assert.Equal(original, ms.ToArray());
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Decompiler_RawPrg_RoundTrip_PreservesNonZeroHeaderTrailer()
    {
        byte[] original = CreateMinimalArbitraryRom();
        // Mega Man–style trailer / NES 2.0-ish bytes that reconstructed headers wipe
        original[7] = 0x08;
        original[8] = 0x20;
        original[9] = 0x00;
        original[10] = 0x00;
        original[11] = 0x07;
        original[12] = 0x01;
        original[13] = 0x00;
        original[14] = 0x00;
        original[15] = 0x01;

        var rom = new NESRomReader(original);
        Assert.Equal(original.AsSpan(0, 16).ToArray(), rom.Header);

        using var ms = new MemoryStream();
        NesRomPacker.WriteExact(ms, rom.Header, rom.Trainer, rom.PrgRom, rom.ChrRom, _logger);
        Assert.Equal(original, ms.ToArray());
    }

    [Fact]
    public void Decompiler_UxRom_FollowsBankSwitchBeforeJmp()
    {
        // 4×16KB UxROM: RESET in fixed bank does LDA #$01 / STA $C000 / JMP $8000
        // into switchable bank 1 (LDX #$AA / infinite JMP).
        var reader = new NESRomReader(CreateUxRomBankSwitchRom());
        Assert.Equal(2, reader.Mapper);
        var code = new Decompiler(reader, _logger).Decompile();
        Assert.Contains("void reset()", code);
        Assert.Contains("func_01_8000()", code);
        Assert.Contains("void func_01_8000()", code);
        Assert.Contains("x = 0xAA", code);
        Assert.Contains("while (true)", code);
    }

    [Fact]
    public void Decompiler_RecoversDoWhileSpinAndWhileTrue()
    {
        // RESET: spin on PPU_STATUS bit7, then infinite JMP-to-self.
        const int prgSize = 16384;
        var rom = new byte[16 + prgSize + 8192];
        rom[0] = (byte)'N'; rom[1] = (byte)'E'; rom[2] = (byte)'S'; rom[3] = 0x1A;
        rom[4] = 1; rom[5] = 1;

        // Code at $8000 (mirrored): LDA $2002 / BPL *-5 / JMP $8005
        rom[16] = 0xAD; rom[17] = 0x02; rom[18] = 0x20; // LDA $2002
        rom[19] = 0x10; rom[20] = 0xFB;                   // BPL $8000
        rom[21] = 0x4C; rom[22] = 0x05; rom[23] = 0x80;   // JMP $8005

        int vec = 16 + prgSize - 6;
        rom[vec] = 0x00; rom[vec + 1] = 0x80;
        rom[vec + 2] = 0x00; rom[vec + 3] = 0x80;
        rom[vec + 4] = 0x00; rom[vec + 5] = 0x80;

        var code = new Decompiler(new NESRomReader(rom), _logger).Decompile();
        _logger.WriteLine($"{code}");
        // Optimized recovery lifts PPU_STATUS bit7 spin → waitvsync (same semantics).
        Assert.Contains("waitvsync();", code);
        Assert.DoesNotContain("do { a = peek(PPU_STATUS); } while (!negative);", code);
        Assert.Contains("while (true) ;", code);
        Assert.DoesNotContain("goto L_8000", code);
        Assert.Contains("Memory accesses (peek / poke counts", code);
        Assert.Contains("PPU_STATUS", code);
        Assert.Matches(@"PPU_STATUS\s+peek×1", code);

        string exact = new Decompiler(new NESRomReader(rom), _logger, PackModePreference.Auto, RecoveryStyle.Exact)
            .Decompile();
        Assert.DoesNotContain("waitvsync();", exact);
        Assert.Contains("peek(PPU_STATUS)", exact);
    }

    [Fact]
    public void Decompiler_LiftsScrollAndOamDmaToNesLib()
    {
        // RESET: STA $2005 ×2; LDA #0 / STA $2003; LDA #2 / STA $4014; JMP self
        const int prgSize = 16384;
        var rom = new byte[16 + prgSize + 8192];
        rom[0] = (byte)'N'; rom[1] = (byte)'E'; rom[2] = (byte)'S'; rom[3] = 0x1A;
        rom[4] = 1; rom[5] = 1;

        byte[] codeBytes =
        [
            0x8D, 0x05, 0x20, // STA $2005
            0x8D, 0x05, 0x20, // STA $2005
            0xA9, 0x00,       // LDA #0
            0x8D, 0x03, 0x20, // STA $2003
            0xA9, 0x02,       // LDA #2
            0x8D, 0x14, 0x40, // STA $4014
            0x4C, 0x12, 0x80, // JMP $8012 (self)
        ];
        Array.Copy(codeBytes, 0, rom, 16, codeBytes.Length);

        int vec = 16 + prgSize - 6;
        rom[vec] = 0x00; rom[vec + 1] = 0x80;
        rom[vec + 2] = 0x00; rom[vec + 3] = 0x80;
        rom[vec + 4] = 0x00; rom[vec + 5] = 0x80;

        var code = new Decompiler(new NESRomReader(rom), _logger).Decompile();
        Assert.Contains("ppu_scroll(", code);
        Assert.Contains("oam_dma(2);", code);
        Assert.DoesNotContain("poke(PPU_OAM_DMA", code);
    }

    [Fact]
    public void Decompiler_LiftsVramAdrPutAndPpuStatus()
    {
        // RESET: LDA $2002; LDA #$20; STA $2006; LDA #0; STA $2006; LDA #$41; STA $2007; JMP self
        const int prgSize = 16384;
        var rom = new byte[16 + prgSize + 8192];
        rom[0] = (byte)'N'; rom[1] = (byte)'E'; rom[2] = (byte)'S'; rom[3] = 0x1A;
        rom[4] = 1; rom[5] = 1;

        byte[] codeBytes =
        [
            0xAD, 0x02, 0x20, // LDA $2002
            0xA9, 0x20,       // LDA #$20
            0x8D, 0x06, 0x20, // STA $2006
            0xA9, 0x00,       // LDA #0
            0x8D, 0x06, 0x20, // STA $2006
            0xA9, 0x41,       // LDA #'A'
            0x8D, 0x07, 0x20, // STA $2007
            0x4C, 0x13, 0x80, // JMP $8013
        ];
        Array.Copy(codeBytes, 0, rom, 16, codeBytes.Length);

        int vec = 16 + prgSize - 6;
        rom[vec] = 0x00; rom[vec + 1] = 0x80;
        rom[vec + 2] = 0x00; rom[vec + 3] = 0x80;
        rom[vec + 4] = 0x00; rom[vec + 5] = 0x80;

        var code = new Decompiler(new NESRomReader(rom), _logger).Decompile();
        Assert.Contains("a = ppu_status();", code);
        Assert.Contains("vram_adr(", code);
        Assert.Contains("vram_put(", code);
        Assert.DoesNotContain("poke(PPU_ADDR", code);
        Assert.DoesNotContain("poke(PPU_DATA", code);
        Assert.DoesNotContain("peek(PPU_STATUS)", code);
    }

    [Fact]
    public void Decompiler_LiftsPpuCtrlAndMaskWrite()
    {
        // RESET: LDA #0 / STA $2000 / LDA #6 / STA $2001 / JMP self
        const int prgSize = 16384;
        var rom = new byte[16 + prgSize + 8192];
        rom[0] = (byte)'N'; rom[1] = (byte)'E'; rom[2] = (byte)'S'; rom[3] = 0x1A;
        rom[4] = 1; rom[5] = 1;

        byte[] codeBytes =
        [
            0xA9, 0x00,       // LDA #0
            0x8D, 0x00, 0x20, // STA $2000
            0xA9, 0x06,       // LDA #6
            0x8D, 0x01, 0x20, // STA $2001
            0x4C, 0x0A, 0x80, // JMP $800A
        ];
        Array.Copy(codeBytes, 0, rom, 16, codeBytes.Length);

        int vec = 16 + prgSize - 6;
        rom[vec] = 0x00; rom[vec + 1] = 0x80;
        rom[vec + 2] = 0x00; rom[vec + 3] = 0x80;
        rom[vec + 4] = 0x00; rom[vec + 5] = 0x80;

        var code = new Decompiler(new NESRomReader(rom), _logger).Decompile();
        Assert.Contains("ppu_ctrl(0);", code);
        Assert.Contains("ppu_mask_write(MASK.EDGE_BG | MASK.EDGE_SPR)", code);
        Assert.DoesNotContain("poke(PPU_CTRL", code);
        Assert.DoesNotContain("poke(PPU_MASK", code);
        Assert.DoesNotContain("ppu_mask_write((byte)", code);
    }

    [Fact]
    public void Decompiler_LiftsPpuCtrlFlagsAsEnum()
    {
        // RESET: LDA #$90 / STA $2000 / JMP self  (NMI | BG_TABLE_1000)
        const int prgSize = 16384;
        var rom = new byte[16 + prgSize + 8192];
        rom[0] = (byte)'N'; rom[1] = (byte)'E'; rom[2] = (byte)'S'; rom[3] = 0x1A;
        rom[4] = 1; rom[5] = 1;

        byte[] codeBytes =
        [
            0xA9, 0x90,       // LDA #$90
            0x8D, 0x00, 0x20, // STA $2000
            0x4C, 0x05, 0x80, // JMP $8005
        ];
        Array.Copy(codeBytes, 0, rom, 16, codeBytes.Length);

        int vec = 16 + prgSize - 6;
        rom[vec] = 0x00; rom[vec + 1] = 0x80;
        rom[vec + 2] = 0x00; rom[vec + 3] = 0x80;
        rom[vec + 4] = 0x00; rom[vec + 5] = 0x80;

        var code = new Decompiler(new NESRomReader(rom), _logger).Decompile();
        Assert.Contains("ppu_ctrl(PPUCTRL.BG_TABLE_1000 | PPUCTRL.NMI)", code);
        Assert.DoesNotContain("ppu_ctrl(0x90)", code);
    }

    [Fact]
    public void Decompiler_LiftsJoypadReadsAndStrobe()
    {
        // RESET: LDA #1 / STA $4016 / LDA #0 / STA $4016 / LDA $4016 / LDA $4017 / JMP self
        const int prgSize = 16384;
        var rom = new byte[16 + prgSize + 8192];
        rom[0] = (byte)'N'; rom[1] = (byte)'E'; rom[2] = (byte)'S'; rom[3] = 0x1A;
        rom[4] = 1; rom[5] = 1;

        byte[] codeBytes =
        [
            0xA9, 0x01,       // LDA #1
            0x8D, 0x16, 0x40, // STA $4016
            0xA9, 0x00,       // LDA #0
            0x8D, 0x16, 0x40, // STA $4016
            0xAD, 0x16, 0x40, // LDA $4016
            0xAD, 0x17, 0x40, // LDA $4017
            0x4C, 0x10, 0x80, // JMP $8010
        ];
        Array.Copy(codeBytes, 0, rom, 16, codeBytes.Length);

        int vec = 16 + prgSize - 6;
        rom[vec] = 0x00; rom[vec + 1] = 0x80;
        rom[vec + 2] = 0x00; rom[vec + 3] = 0x80;
        rom[vec + 4] = 0x00; rom[vec + 5] = 0x80;

        var code = new Decompiler(new NESRomReader(rom), _logger).Decompile();
        Assert.Contains("joypad1_write(JOYSTROBE.Latch)", code);
        Assert.Contains("joypad1_write(JOYSTROBE.Shift)", code);
        Assert.Contains("a = joypad1_read();", code);
        Assert.Contains("a = joypad2_read();", code);
        Assert.DoesNotContain("peek(JOYPAD", code);
        Assert.DoesNotContain("poke(JOYPAD1", code);
    }

    /// <summary>4×16KB UxROM: reset in fixed bank 3 switches to bank 1 then JMP $8000.</summary>
    static byte[] CreateUxRomBankSwitchRom()
    {
        const int prgSize = 4 * 16384;
        var rom = new byte[16 + prgSize];
        rom[0] = (byte)'N'; rom[1] = (byte)'E'; rom[2] = (byte)'S'; rom[3] = 0x1A;
        rom[4] = 4;
        rom[5] = 0;
        rom[6] = 0x20; // mapper 2
        rom[7] = 0;

        // Bank 1 @ file 0x4000: entry at $8000
        int b1 = 16 + 0x4000;
        rom[b1] = 0xA2; rom[b1 + 1] = 0xAA; // LDX #$AA
        rom[b1 + 2] = 0x4C; rom[b1 + 3] = 0x02; rom[b1 + 4] = 0x80; // JMP $8002
        rom[b1 + 5] = 0x4C; rom[b1 + 6] = 0x02; rom[b1 + 7] = 0x80; // $8002: JMP $8002

        // Fixed bank 3 @ file 0xC000 → CPU $C000
        int fix = 16 + 0xC000;
        rom[fix] = 0x78; // SEI
        rom[fix + 1] = 0xA9; rom[fix + 2] = 0x01; // LDA #$01
        rom[fix + 3] = 0x8D; rom[fix + 4] = 0x00; rom[fix + 5] = 0xC0; // STA $C000
        rom[fix + 6] = 0x4C; rom[fix + 7] = 0x00; rom[fix + 8] = 0x80; // JMP $8000

        // Vectors at end of PRG
        int vec = 16 + prgSize - 6;
        rom[vec] = 0x00; rom[vec + 1] = 0xC0;     // NMI
        rom[vec + 2] = 0x00; rom[vec + 3] = 0xC0; // RESET
        rom[vec + 4] = 0x00; rom[vec + 5] = 0xC0; // IRQ
        return rom;
    }

    [Fact]
    public void NesRomPacker_RoundTripsArbitraryRom()
    {
        byte[] original = CreateMinimalArbitraryRom();
        var rom = new NESRomReader(original);

        using var ms = new MemoryStream();
        NesRomPacker.Write(
            ms,
            rom.PrgRom,
            rom.ChrRom,
            rom.PrgBanks,
            rom.ChrBanks,
            rom.Mapper,
            rom.VerticalMirroring ? "Vertical" : "Horizontal",
            rom.HasBattery,
            _logger,
            exactHeader: rom.Header,
            trainer: rom.Trainer);

        Assert.Equal(original, ms.ToArray());
    }

    [Fact]
    public void Decompiler_WriteProject_RawPrg_EmitsPrgBin()
    {
        byte[] romBytes = CreateMinimalArbitraryRom();
        var rom = new NESRomReader(romBytes);
        var decompiler = new Decompiler(rom, _logger);
        string dir = Path.Combine(Path.GetTempPath(), "dotnes-decompile-" + Guid.NewGuid().ToString("N"));
        try
        {
            decompiler.WriteProject(dir, "arb");
            Assert.True(File.Exists(Path.Combine(dir, "prg.bin")));
            Assert.Equal(rom.PrgRom, File.ReadAllBytes(Path.Combine(dir, "prg.bin")));
            Assert.True(File.Exists(Path.Combine(dir, "header.bin")));
            Assert.Equal(rom.Header, File.ReadAllBytes(Path.Combine(dir, "header.bin")));
            Assert.True(File.Exists(Path.Combine(dir, "RomImage.cs")));
            Assert.Contains("readonly byte[] Header", File.ReadAllText(Path.Combine(dir, "RomImage.cs")));
            Assert.True(File.Exists(Path.Combine(dir, "chr_generic.s")));
            Assert.True(File.Exists(Path.Combine(dir, "arb.csproj")));
            Assert.True(File.Exists(Path.Combine(dir, "Program.cs")));
            Assert.False(File.Exists(Path.Combine(dir, "Recovered.cs")));
            Assert.Contains("void reset()", File.ReadAllText(Path.Combine(dir, "Program.cs")));
            Assert.Contains("prg.bin", File.ReadAllText(Path.Combine(dir, "Program.cs")));
            Assert.True(File.Exists(Path.Combine(dir, "disasm.s")));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SignatureDatabase_IncludesCommonNesLibRoutines()
    {
        var names = Analysis.SignatureDatabase.FinalBuiltIns.Select(s => s.Name).ToHashSet();
        Assert.Contains("pad_poll", names);
        Assert.Contains("oam_spr", names);
        Assert.Contains("fade_in", names);
        Assert.Contains("fade_out", names);
        Assert.Contains("apu_init", names);
        Assert.Contains("waitvsync", names);
        Assert.Contains("vram_unrle", names);
        Assert.Contains("music_tick", names);
    }

    [Theory]
    [InlineData(0x00, "SpriteSize.Size8x8")]
    [InlineData(0x01, "SpriteSize.Size8x16")]
    public void NesLibEnumFormatter_SpriteSize(byte value, string expected)
        => Assert.Equal(expected, NesLibEnumFormatter.FormatSpriteSize(value));

    [Theory]
    [InlineData(0x00, "VramIncrement.By1")]
    [InlineData(0x01, "VramIncrement.By32")]
    public void NesLibEnumFormatter_VramIncrement(byte value, string expected)
        => Assert.Equal(expected, NesLibEnumFormatter.FormatVramIncrement(value));

    [Fact]
    public void NesLibEnumFormatter_MaskFlags()
    {
        Assert.Equal("MASK.EDGE_BG | MASK.EDGE_SPR | MASK.BG | MASK.SPR",
            NesLibEnumFormatter.FormatMask((byte)(MASK.BG | MASK.SPR | MASK.EDGE_BG | MASK.EDGE_SPR)));
        Assert.Equal("MASK.MONO", NesLibEnumFormatter.FormatMask((byte)MASK.MONO));
    }

    [Fact]
    public void NesLibEnumFormatter_PpuCtrlFlags()
    {
        Assert.Equal("0", NesLibEnumFormatter.FormatPpuCtrl(0));
        Assert.Equal("PPUCTRL.BG_TABLE_1000 | PPUCTRL.NMI",
            NesLibEnumFormatter.FormatPpuCtrl(0x90));
        Assert.Equal("PPUCTRL.NMI", NesLibEnumFormatter.FormatPpuCtrl((byte)PPUCTRL.NMI));
    }

    [Fact]
    public void NesLibEnumFormatter_ApuStatusAndFrameFlags()
    {
        Assert.Equal("APUSTATUS.PULSE1 | APUSTATUS.PULSE2 | APUSTATUS.TRIANGLE | APUSTATUS.NOISE",
            NesLibEnumFormatter.FormatApuStatus(0x0F));
        Assert.Equal("APUSTATUS.PULSE1 | APUSTATUS.PULSE2 | APUSTATUS.TRIANGLE | APUSTATUS.NOISE | APUSTATUS.DMC",
            NesLibEnumFormatter.FormatApuStatus(0x1F));
        Assert.Equal("APUFRAME.IRQ_INHIBIT | APUFRAME.MODE_5STEP",
            NesLibEnumFormatter.FormatApuFrame(0xC0));
        Assert.Equal("APUFRAME.IRQ_INHIBIT | APUFRAME.MODE_5STEP | (APUFRAME)0x3F",
            NesLibEnumFormatter.FormatApuFrame(0xFF));
    }

    [Fact]
    public void Decompiler_LiftsApuStatusAndFrameAsEnums()
    {
        // RESET: LDA #$0F / STA $4015 / LDA #$C0 / STA $4017 / JMP self
        const int prgSize = 16384;
        var rom = new byte[16 + prgSize + 8192];
        rom[0] = (byte)'N'; rom[1] = (byte)'E'; rom[2] = (byte)'S'; rom[3] = 0x1A;
        rom[4] = 1; rom[5] = 1;

        byte[] codeBytes =
        [
            0xA9, 0x0F,       // LDA #$0F
            0x8D, 0x15, 0x40, // STA $4015
            0xA9, 0xC0,       // LDA #$C0
            0x8D, 0x17, 0x40, // STA $4017
            0x4C, 0x0A, 0x80, // JMP $800A
        ];
        Array.Copy(codeBytes, 0, rom, 16, codeBytes.Length);

        int vec = 16 + prgSize - 6;
        rom[vec] = 0x00; rom[vec + 1] = 0x80;
        rom[vec + 2] = 0x00; rom[vec + 3] = 0x80;
        rom[vec + 4] = 0x00; rom[vec + 5] = 0x80;

        var code = new Decompiler(new NESRomReader(rom), _logger).Decompile();
        Assert.Contains("apu_status_write(APUSTATUS.PULSE1 | APUSTATUS.PULSE2 | APUSTATUS.TRIANGLE | APUSTATUS.NOISE)", code);
        Assert.Contains("apu_frame(APUFRAME.IRQ_INHIBIT | APUFRAME.MODE_5STEP)", code);
        Assert.DoesNotContain("poke(APU_STATUS", code);
        Assert.DoesNotContain("apu_frame(0xC0)", code);
    }

    [Fact]
    public void NesLibEnumFormatter_PadAndApu()
    {
        Assert.Equal("PAD.A", NesLibEnumFormatter.FormatPad((byte)PAD.A));
        Assert.Equal("PAD.LEFT | PAD.RIGHT", NesLibEnumFormatter.FormatPad((byte)(PAD.LEFT | PAD.RIGHT)));
        Assert.True(NesLibEnumFormatter.TryFormatPadByteOperand(0x10, out string up));
        Assert.Equal("(byte)PAD.UP", up);
        Assert.True(NesLibEnumFormatter.TryFormatPadByteOperand(0xDF, out string clearDown));
        Assert.Equal("(byte)~PAD.DOWN", clearDown);
        Assert.Equal("JOYSTROBE.Latch", NesLibEnumFormatter.FormatJoyStrobe(1));
        Assert.Equal("JOYSTROBE.Shift", NesLibEnumFormatter.FormatJoyStrobe(0));
        Assert.Equal("JOYPADBIT.DATA", NesLibEnumFormatter.FormatJoypadBit(1));
        Assert.Equal("PulseChannel.Pulse1", NesLibEnumFormatter.FormatPulseChannel(0));
        Assert.Equal("APUDuty.Duty50", NesLibEnumFormatter.FormatApuDuty(2));
        Assert.Equal("NESColor.Red", NesLibEnumFormatter.FormatNesColor(0x16));
        Assert.Equal("OAM.FLIP_H | OAM.BEHIND", NesLibEnumFormatter.FormatOamAttr((byte)(OAM.FLIP_H | OAM.BEHIND)));
    }

    [Fact]
    public void NesLibCallFormatter_PalBgColAndPalSprCol_UseNesColor()
    {
        Assert.Equal(
            "pal_bg_col(1, NESColor.Red);",
            NesLibCallFormatter.FormatCallN("pal_bg_col", "0x16", ["1"]));
        Assert.Equal(
            "pal_spr_col(0, NESColor.White);",
            NesLibCallFormatter.FormatCallN("pal_spr_col", "0x30", ["0"]));
        Assert.Equal(
            "pal_col(2, NESColor.LightGray);",
            NesLibCallFormatter.FormatCallN("pal_col", "0x20", ["2"]));
    }

    [Fact]
    public void Decompiler_LiftsJoypadStrobeAndSerialBitAsEnums()
    {
        // RESET: LDA #1 / STA $4016 / LDA #0 / STA $4016 / LDA $4016 / AND #1 / JMP self
        const int prgSize = 16384;
        var rom = new byte[16 + prgSize + 8192];
        rom[0] = (byte)'N'; rom[1] = (byte)'E'; rom[2] = (byte)'S'; rom[3] = 0x1A;
        rom[4] = 1; rom[5] = 1;

        byte[] codeBytes =
        [
            0xA9, 0x01,       // LDA #$01
            0x8D, 0x16, 0x40, // STA $4016
            0xA9, 0x00,       // LDA #$00
            0x8D, 0x16, 0x40, // STA $4016
            0xAD, 0x16, 0x40, // LDA $4016
            0x29, 0x01,       // AND #$01
            0x4C, 0x0C, 0x80, // JMP $800C
        ];
        Array.Copy(codeBytes, 0, rom, 16, codeBytes.Length);

        int vec = 16 + prgSize - 6;
        rom[vec] = 0x00; rom[vec + 1] = 0x80;
        rom[vec + 2] = 0x00; rom[vec + 3] = 0x80;
        rom[vec + 4] = 0x00; rom[vec + 5] = 0x80;

        var code = new Decompiler(new NESRomReader(rom), _logger).Decompile();
        Assert.Contains("joypad1_write(JOYSTROBE.Latch)", code);
        Assert.Contains("joypad1_write(JOYSTROBE.Shift)", code);
        Assert.Contains("joypad1_read()", code);
        Assert.Contains("JOYPADBIT.DATA", code);
    }

    [Fact]
    public void Decompiler_PromotesHotRamAddressesToConsts()
    {
        // RESET: loop that peeks/pokes $00FE three times, then JMP self
        const int prgSize = 16384;
        var rom = new byte[16 + prgSize + 8192];
        rom[0] = (byte)'N'; rom[1] = (byte)'E'; rom[2] = (byte)'S'; rom[3] = 0x1A;
        rom[4] = 1; rom[5] = 1;

        byte[] codeBytes =
        [
            0xAD, 0xFE, 0x00, // LDA $00FE
            0x8D, 0xFE, 0x00, // STA $00FE
            0xAD, 0xFE, 0x00, // LDA $00FE
            0x8D, 0xFE, 0x00, // STA $00FE
            0xAD, 0xFE, 0x00, // LDA $00FE
            0x4C, 0x00, 0x80, // JMP $8000
        ];
        Array.Copy(codeBytes, 0, rom, 16, codeBytes.Length);

        int vec = 16 + prgSize - 6;
        rom[vec] = 0x00; rom[vec + 1] = 0x80;
        rom[vec + 2] = 0x00; rom[vec + 3] = 0x80;
        rom[vec + 4] = 0x00; rom[vec + 5] = 0x80;

        var code = new Decompiler(new NESRomReader(rom), _logger).Decompile();
        Assert.Contains("const ushort zp_FE = 0x00FE;", code);
        Assert.Contains("peek(zp_FE)", code);
        Assert.DoesNotContain("peek((ushort)0xFE)", code);
        Assert.DoesNotContain("peek((ushort)0x00FE)", code);
    }

    [Fact]
    public void Decompiler_LiftsPadCompareAndPaletteColorsAsEnums()
    {
        // RESET:
        //   LDA #$01 / STA $4016 / LDA #$00 / STA $4016
        //   LDA $4016 / LSR / ROL $F5
        //   LDA $F5 / CMP #$10
        //   LDA #$3F / STA $2006 / LDA #$00 / STA $2006
        //   LDA #$0F / STA $2007 / LDA #$16 / STA $2007
        //   JMP self
        const int prgSize = 16384;
        var rom = new byte[16 + prgSize + 8192];
        rom[0] = (byte)'N'; rom[1] = (byte)'E'; rom[2] = (byte)'S'; rom[3] = 0x1A;
        rom[4] = 1; rom[5] = 1;

        byte[] codeBytes =
        [
            0xA9, 0x01,       // LDA #$01
            0x8D, 0x16, 0x40, // STA $4016
            0xA9, 0x00,       // LDA #$00
            0x8D, 0x16, 0x40, // STA $4016
            0xAD, 0x16, 0x40, // LDA $4016
            0x4A,             // LSR
            0x26, 0xF5,       // ROL $F5
            0xA5, 0xF5,       // LDA $F5
            0xC9, 0x10,       // CMP #$10  (PAD.UP)
            0xA5, 0xF5,       // LDA $F5
            0x29, 0xDF,       // AND #$DF  (~PAD.DOWN)
            0xA9, 0x3F,       // LDA #$3F
            0x8D, 0x06, 0x20, // STA $2006
            0xA9, 0x00,       // LDA #$00
            0x8D, 0x06, 0x20, // STA $2006
            0xA9, 0x0F,       // LDA #$0F
            0x8D, 0x07, 0x20, // STA $2007
            0xA9, 0x16,       // LDA #$16
            0x8D, 0x07, 0x20, // STA $2007
            0x4C, 0x00, 0x80, // JMP $8000
        ];
        Array.Copy(codeBytes, 0, rom, 16, codeBytes.Length);

        int vec = 16 + prgSize - 6;
        rom[vec] = 0x00; rom[vec + 1] = 0x80;
        rom[vec + 2] = 0x00; rom[vec + 3] = 0x80;
        rom[vec + 4] = 0x00; rom[vec + 5] = 0x80;

        var code = new Decompiler(new NESRomReader(rom), _logger).Decompile();
        Assert.Contains("joypad1_read()", code);
        Assert.Contains("a == (byte)PAD.UP", code);
        Assert.Contains("a & (byte)~PAD.DOWN", code);
        Assert.Contains("vram_adr(0x3F00)", code);
        Assert.Contains("vram_put(NESColor.Black)", code);
        Assert.Contains("vram_put(NESColor.Red)", code);
    }

    [Fact]
    public void Decompiler_LiftsPaletteColorsAfterShiftExprVramAdr()
    {
        // RESET: LDA #$3F / STA $2006 / LDA #$00 / STA $2006  (hi/lo interrupted — not folded)
        //        then after other work: expression-style palette adr + puts
        // Simpler: LDA #$0F / STA $2007 after vram_adr((0x3F<<8)|0) via:
        //   LDA #$3F / STA $2006 / TXA / STA $2006 / LDA #$0F / STA $2007 / LDA #$16 / STA $2007
        // with X=0 so address is $3F00 (pokes not consecutive → not folded to constant vram_adr).
        const int prgSize = 16384;
        var rom = new byte[16 + prgSize + 8192];
        rom[0] = (byte)'N'; rom[1] = (byte)'E'; rom[2] = (byte)'S'; rom[3] = 0x1A;
        rom[4] = 1; rom[5] = 1;

        byte[] codeBytes =
        [
            0xA2, 0x00,       // LDX #$00
            0xA9, 0x3F,       // LDA #$3F
            0x8D, 0x06, 0x20, // STA $2006
            0x8A,             // TXA
            0x8D, 0x06, 0x20, // STA $2006
            0xA9, 0x0F,       // LDA #$0F
            0x8D, 0x07, 0x20, // STA $2007
            0xA9, 0x16,       // LDA #$16
            0x8D, 0x07, 0x20, // STA $2007
            0x4C, 0x00, 0x80, // JMP $8000
        ];
        Array.Copy(codeBytes, 0, rom, 16, codeBytes.Length);

        int vec = 16 + prgSize - 6;
        rom[vec] = 0x00; rom[vec + 1] = 0x80;
        rom[vec + 2] = 0x00; rom[vec + 3] = 0x80;
        rom[vec + 4] = 0x00; rom[vec + 5] = 0x80;

        var code = new Decompiler(new NESRomReader(rom), _logger).Decompile();
        Assert.Contains("vram_put(NESColor.Black)", code);
        Assert.Contains("vram_put(NESColor.Red)", code);
    }

    [Fact]
    public void Decompiler_Bigsprites_UsesSpriteSizeEnum()
    {
        var rom = new NESRomReader(GetVerifiedRom("bigsprites"));
        var code = new Decompiler(rom, _logger).Decompile();
        Assert.Contains("oam_size(SpriteSize.Size8x16)", code);
        Assert.DoesNotContain("oam_size(0x01)", code);
        Assert.DoesNotContain("oam_size(1)", code);
    }

    /// <summary>
    /// Minimal non-dotnes iNES ROM: 16KB PRG + 8KB CHR, RESET→infinite JMP.
    /// </summary>
    static byte[] CreateMinimalArbitraryRom()
    {
        const int prgSize = 16384;
        const int chrSize = 8192;
        var rom = new byte[16 + prgSize + chrSize];
        rom[0] = (byte)'N';
        rom[1] = (byte)'E';
        rom[2] = (byte)'S';
        rom[3] = 0x1A;
        rom[4] = 1; // PRG banks
        rom[5] = 1; // CHR banks
        rom[6] = 0;
        rom[7] = 0;

        // Put a distinctive non-CRT byte pattern at start of PRG
        rom[16] = 0xEA; // NOP
        rom[17] = 0xEA;
        // Infinite loop at $8002: JMP $8002
        rom[18] = 0x4C;
        rom[19] = 0x02;
        rom[20] = 0x80;

        // Vectors at end of PRG
        int vec = 16 + prgSize - 6;
        rom[vec] = 0x02; rom[vec + 1] = 0x80;     // NMI
        rom[vec + 2] = 0x02; rom[vec + 3] = 0x80; // RESET
        rom[vec + 4] = 0x02; rom[vec + 5] = 0x80; // IRQ

        // Distinctive CHR byte
        rom[16 + prgSize] = 0xA5;
        return rom;
    }
}
