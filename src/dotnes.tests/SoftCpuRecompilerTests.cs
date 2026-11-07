namespace dotnes;

public class SoftCpuRecompilerTests
{
    [Fact]
    public void SoftCpu_LowersMinimalReset_OntoPrg()
    {
        // NROM-128 style 16KB PRG; reset at $8000
        var basePrg = new byte[0x4000];
        for (int i = 0; i < basePrg.Length; i++)
            basePrg[i] = 0xEA; // NOP filler

        const string program = """
            // bank=0 $8000
            void reset()
            {
                sei();
                cld();
                a = 0x10;
                poke(PPU_CTRL, a);
                x = 0xFF;
                txs();
                while (true) ;
            }
            """;

        var result = SoftCpuRecompiler.Recompile(program, basePrg);
        Assert.Equal(1, result.RoutinesLowered);
        Assert.Equal(0, result.RoutinesSkipped);

        // SEI CLD LDA #$10 STA $2000 LDX #$FF TXS JMP self
        Assert.Equal(0x78, result.PrgRom[0]); // SEI
        Assert.Equal(0xD8, result.PrgRom[1]); // CLD
        Assert.Equal(0xA9, result.PrgRom[2]); // LDA #
        Assert.Equal(0x10, result.PrgRom[3]);
        Assert.Equal(0x8D, result.PrgRom[4]); // STA abs
        Assert.Equal(0x00, result.PrgRom[5]);
        Assert.Equal(0x20, result.PrgRom[6]);
        Assert.Equal(0xA2, result.PrgRom[7]); // LDX #
        Assert.Equal(0xFF, result.PrgRom[8]);
        Assert.Equal(0x9A, result.PrgRom[9]); // TXS
        Assert.Equal(0x4C, result.PrgRom[10]); // JMP abs
    }

    [Fact]
    public void SoftCpu_KeepsGapBridge_WhenRoutineCannotLower()
    {
        var basePrg = new byte[0x4000];
        basePrg[0] = 0x42;
        const string program = """
            // bank=0 $8000
            void reset()
            {
                totally_unsupported_intrinsic(a, x, y);
            }
            """;

        var result = SoftCpuRecompiler.Recompile(program, basePrg);
        Assert.Equal(0, result.RoutinesLowered);
        Assert.Equal(1, result.RoutinesSkipped);
        Assert.Equal(0x42, result.PrgRom[0]);
    }

    [Fact]
    public void SoftCpu_CmpAndBranch_AndIndexedPeek()
    {
        var basePrg = new byte[0x8000];
        const string program = """
            const ushort ram_07D7 = 0x07D7;
            // bank=0 $8000
            void reset()
            {
                x = 5;
                L_8018: ;
                a = peek((ushort)(ram_07D7 + x));
                zero = a == 0x0A; carry = a >= 0x0A; negative = (byte)(a - 0x0A) >= 0x80;
                if (carry) goto L_802B;
                x--;
                if (!negative) goto L_8018;
                L_802B: ;
                return;
            }
            """;

        var result = SoftCpuRecompiler.Recompile(program, basePrg);
        Assert.Equal(1, result.RoutinesLowered);
        Assert.Equal(0xA2, result.PrgRom[0]); // LDX #5
        Assert.Equal(0x05, result.PrgRom[1]);
    }

    [Fact]
    public void SoftCpu_TryCpuToPrgOffset_Nrom()
    {
        Assert.True(SoftCpuRecompiler.TryCpuToPrgOffset(0x8000, 0, 0x4000, out int o16));
        Assert.Equal(0, o16);
        Assert.True(SoftCpuRecompiler.TryCpuToPrgOffset(0x8000, 0, 0x8000, out int o32));
        Assert.Equal(0, o32);
        Assert.True(SoftCpuRecompiler.TryCpuToPrgOffset(0xC000, 1, 0x8000, out int hi));
        Assert.Equal(0x4000, hi);
    }

    [Fact]
    public void SoftCpu_ApuRegisterWriters_LowerToAbsStores()
    {
        var basePrg = new byte[0x4000];
        Array.Fill(basePrg, (byte)0xEA);
        const string program = """
            // bank=0 $8000
            void reset()
            {
                apu_status_write(APUSTATUS.PULSE1 | APUSTATUS.PULSE2 | APUSTATUS.TRIANGLE | APUSTATUS.NOISE);
                apu_frame(APUFRAME.IRQ_INHIBIT | APUFRAME.MODE_5STEP | (APUFRAME)0x3F);
                apu_pulse1_ctrl(0x90);
                apu_pulse1_sweep(0xBB);
                apu_dmc_raw(0x40);
                apu_noise_ctrl(a);
                apu_noise_period(x);
                return;
            }
            """;
        var result = SoftCpuRecompiler.Recompile(program, basePrg);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(1, result.RoutinesLowered);
        var hex = Convert.ToHexString(result.PrgRom.AsSpan(0, 48));
        Assert.Contains("A90F8D1540", hex); // LDA #$0F / STA $4015
        Assert.Contains("A9FF8D1740", hex); // LDA #$FF / STA $4017 (0xC0|0x3F)
        Assert.Contains("A9908D0040", hex); // pulse1 ctrl
        Assert.Contains("A9BB8D0140", hex); // pulse1 sweep
        Assert.Contains("A9408D1140", hex); // dmc raw
        Assert.Contains("8D0C40", hex); // STA $400C from A
        Assert.Contains("8E0E40", hex); // STX $400E
    }

    [Fact]
    public void SoftCpu_SmbReadJoypad_MatchesOriginalBytes()
    {
        string? smbDir = FindSmbDecompDir();
        if (smbDir is null)
            return;

        byte[] prg = File.ReadAllBytes(Path.Combine(smbDir, "prg.bin"));
        var result = SoftCpuRecompiler.Recompile(File.ReadAllText(Path.Combine(smbDir, "Program.cs")), prg);
        Assert.DoesNotContain(result.Diagnostics, d => d.Contains("truncated", StringComparison.OrdinalIgnoreCase));
        int off = 0x8E6A - 0x8000;
        Assert.Equal(prg.AsSpan(off, 40).ToArray(), result.PrgRom.AsSpan(off, 40).ToArray());
    }

    [Fact]
    public void SoftCpu_SmbVramPath_MatchesOriginalBytes()
    {
        // func_00_8EDD overlays from in-body L_8E92 through next routine ($8EE6).
        string? smbDir = FindSmbDecompDir();
        if (smbDir is null)
            return;

        byte[] prg = File.ReadAllBytes(Path.Combine(smbDir, "prg.bin"));
        var result = SoftCpuRecompiler.Recompile(File.ReadAllText(Path.Combine(smbDir, "Program.cs")), prg);
        Assert.DoesNotContain(result.Diagnostics, d => d.Contains("truncated", StringComparison.OrdinalIgnoreCase));
        int off = 0x8E92 - 0x8000;
        int len = 0x8EE6 - 0x8E92;
        Assert.Equal(prg.AsSpan(off, len).ToArray(), result.PrgRom.AsSpan(off, len).ToArray());
    }

    [Fact]
    public void SoftCpu_SmbProgram_LowersResetBootPath()
    {
        string? smbDir = FindSmbDecompDir();
        if (smbDir is null)
            return; // optional local fixture

        string programCs = Path.Combine(smbDir, "Program.cs");
        string prgPath = Path.Combine(smbDir, "prg.bin");
        Assert.True(File.Exists(programCs));
        Assert.True(File.Exists(prgPath));

        byte[] prg = File.ReadAllBytes(prgPath);
        var result = SoftCpuRecompiler.Recompile(File.ReadAllText(programCs), prg);

        // Reset must stay SEI/CLD; overlays may change body but must not smash vectors.
        Assert.Equal(0x78, result.PrgRom[0]); // SEI
        Assert.Equal(0xD8, result.PrgRom[1]); // CLD
        Assert.Equal(prg[^6..], result.PrgRom[^6..]); // NMI/RESET/IRQ vectors intact
        Assert.True(result.RoutinesLowered > 0 || result.RoutinesSkipped > 0);
        Assert.DoesNotContain(result.Diagnostics, d => d.Contains("truncated", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SoftCpu_FreshSmbDecomp_LowersCountdownAndIndexedForms()
    {
        // Optional: /tmp/smb-read6 from a fresh Optimized decompile of SMB.
        const string dir = "/tmp/smb-read6";
        string programCs = Path.Combine(dir, "Program.cs");
        string prgPath = Path.Combine(dir, "prg.bin");
        if (!File.Exists(programCs) || !File.Exists(prgPath))
            return;

        string src = File.ReadAllText(programCs);
        Assert.True(
            src.Contains("while (--", StringComparison.Ordinal)
            || src.Contains("for (", StringComparison.Ordinal));
        Assert.Contains("peek(", src, StringComparison.Ordinal);
        Assert.Contains("+ x)", src, StringComparison.Ordinal);

        byte[] prg = File.ReadAllBytes(prgPath);
        var result = SoftCpuRecompiler.Recompile(src, prg);
        Assert.True(result.RoutinesLowered > 0);
        Assert.DoesNotContain(result.Diagnostics, d => d.Contains("truncated", StringComparison.OrdinalIgnoreCase));
        // New sugars must not crash the countdown / multi-shift / indexed lowerers.
        Assert.DoesNotContain(result.Diagnostics, d => d.Contains("do/while cond", StringComparison.Ordinal));
        Assert.Equal(0x78, result.PrgRom[0]);
        Assert.Equal(0xD8, result.PrgRom[1]);
    }

    [Fact]
    public void SoftCpu_LowersForCountdownAndCountUp()
    {
        var basePrg = new byte[0x4000];
        for (int i = 0; i < basePrg.Length; i++)
            basePrg[i] = 0xEA;

        const string program = """
            // bank=0 $8000
            void reset()
            {
                for (x = 3; x != 0; x--)
                {
                    y++;
                }
                for (x = 0; x != 6; x++)
                {
                    a = peek((ushort)(0x0300 + x));
                }
                return;
            }
            """;

        var result = SoftCpuRecompiler.Recompile(program, basePrg);
        Assert.Equal(1, result.RoutinesLowered);
        Assert.Empty(result.Diagnostics);
        // LDX #3 / loop: INY / DEX / BNE
        Assert.Equal(0xA2, result.PrgRom[0]);
        Assert.Equal(0x03, result.PrgRom[1]);
        Assert.Equal(0xC8, result.PrgRom[2]); // INY
        Assert.Equal(0xCA, result.PrgRom[3]); // DEX
        Assert.Equal(0xD0, result.PrgRom[4]); // BNE
    }

    [Fact]
    public void SoftCpu_LowersBufferIndexStoreAndLoad()
    {
        var basePrg = new byte[0x4000];
        Array.Fill(basePrg, (byte)0xEA);

        const string program = """
            byte[] oam = new byte[256]; // $0200
            byte[] vram_buf = new byte[256]; // $0300

            // bank=0 $8000
            void reset()
            {
                oam[y] = a;
                a = vram_buf[x];
                vram_buf[x + 1] = 0x00;
                return;
            }
            """;

        var result = SoftCpuRecompiler.Recompile(program, basePrg);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(1, result.RoutinesLowered);
        // STA $0200,Y
        Assert.Equal(0x99, result.PrgRom[0]);
        Assert.Equal(0x00, result.PrgRom[1]);
        Assert.Equal(0x02, result.PrgRom[2]);
        // LDA $0300,X
        Assert.Equal(0xBD, result.PrgRom[3]);
        Assert.Equal(0x00, result.PrgRom[4]);
        Assert.Equal(0x03, result.PrgRom[5]);
        // LDA #$00 / STA $0301,X
        Assert.Equal(0xA9, result.PrgRom[6]);
        Assert.Equal(0x00, result.PrgRom[7]);
        Assert.Equal(0x9D, result.PrgRom[8]);
        Assert.Equal(0x01, result.PrgRom[9]);
        Assert.Equal(0x03, result.PrgRom[10]);
    }

    [Fact]
    public void SoftCpu_LowersOamFieldsCarryAddBufRmwAndSwitch()
    {
        var basePrg = new byte[0x4000];
        Array.Fill(basePrg, (byte)0xEA);

        const string program = """
            byte[] oam_y = new byte[256]; // $0200
            byte[] oam_tile = new byte[256]; // $0201
            byte[] vram_buf = new byte[256]; // $0300

            // bank=0 $8000
            void reset()
            {
                oam_y[y] = a;
                oam_tile[y] = a;
                vram_buf[x]++;
                a += 8;
                switch (a)
                {
                    case 1:
                        return;
                    case 2:
                        y++;
                        return;
                }
                return;
            }
            """;

        var result = SoftCpuRecompiler.Recompile(program, basePrg);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(1, result.RoutinesLowered);
        // STA $0200,Y
        Assert.Equal(0x99, result.PrgRom[0]);
        Assert.Equal(0x00, result.PrgRom[1]);
        Assert.Equal(0x02, result.PrgRom[2]);
        // STA $0201,Y
        Assert.Equal(0x99, result.PrgRom[3]);
        Assert.Equal(0x01, result.PrgRom[4]);
        Assert.Equal(0x02, result.PrgRom[5]);
        // INC $0300,X
        Assert.Equal(0xFE, result.PrgRom[6]);
        Assert.Equal(0x00, result.PrgRom[7]);
        Assert.Equal(0x03, result.PrgRom[8]);
        // CLC / ADC #$08
        Assert.Equal(0x18, result.PrgRom[9]);
        Assert.Equal(0x69, result.PrgRom[10]);
        Assert.Equal(0x08, result.PrgRom[11]);
        // CMP #$01
        Assert.Equal(0xC9, result.PrgRom[12]);
        Assert.Equal(0x01, result.PrgRom[13]);
    }

    [Fact]
    public void SoftCpuReadability_DemotesSize1AndSugarsCarry()
    {
        var bodies = new List<List<string>>
        {
            new()
            {
                "buf_000F[0] = a;",
                "carry = false;",
                "a = (byte)(a + 8 + (carry ? 1 : 0));",
                "vram_buf[x] = (byte)(vram_buf[x] - 1);",
            },
        };
        var buffers = new List<BufferRecovery.RamBuffer>
        {
            new(0x000F, 1, "buf_000F"),
            new(0x0300, 16, "vram_buf"),
        };
        var ram = new Dictionary<ushort, string>();
        buffers = SoftCpuReadabilityPass.DemoteSize1Buffers(bodies, buffers, ram, out _);
        Assert.DoesNotContain(buffers, b => b.Name == "buf_000F");
        Assert.Contains(ram, kv => kv.Key == 0x000F);
        SoftCpuReadabilityPass.ApplyBodyLocalPasses(bodies[0]);
        Assert.Contains(bodies[0], l => l.Contains("a += 8"));
        Assert.Contains(bodies[0], l => l.Contains("vram_buf[x]--"));
    }

    [Fact]
    public void SoftCpuReadability_ExpandsIndexedSize1AndRolesPadBuf()
    {
        var bodies = new List<List<string>>
        {
            new()
            {
                "buf_000F[x] = a;",
                "buf_06FC[0] = a;",
                "a = peek(pad_06FC);",
            },
        };
        var buffers = new List<BufferRecovery.RamBuffer>
        {
            new(0x000F, 1, "buf_000F"),
            new(0x001E, 1, "buf_001E"),
            new(0x06FC, 64, "buf_06FC"),
        };
        var ram = new Dictionary<ushort, string>();
        buffers = SoftCpuReadabilityPass.DemoteSize1Buffers(bodies, buffers, ram, out _);
        var expanded = Assert.Single(buffers, b => b.Base == 0x000F);
        Assert.True(expanded.Size >= 2, $"size {expanded.Size}");
        buffers = SoftCpuReadabilityPass.ApplyBufferRoleNames(bodies, buffers, [0x06FC]);
        Assert.Contains(buffers, b => b.Name == "pad_buf" && b.Base == 0x06FC);
        Assert.Contains(bodies[0], l => l.Contains("pad_buf["));
    }

    [Fact]
    public void SoftCpuReadability_KeepsOffsetZeroHotSlotScalars()
    {
        var bodies = new List<List<string>>
        {
            new()
            {
                "a = buf_0086[0];",
                "a = buf_0086[0];",
                "a = buf_0086[0];",
                "buf_0086[0] = a;",
                "a = buf_0086[x];",
            },
        };
        var buffers = new List<BufferRecovery.RamBuffer> { new(0x0086, 2, "buf_0086") };
        var ram = new Dictionary<ushort, string>();
        var promoted = SoftCpuReadabilityPass.PromoteHotBufferSlots(bodies, buffers, ram);
        Assert.Contains((ushort)0x0086, promoted);
        Assert.Equal("buf_0086_00", ram[0x0086]);
        Assert.Contains(bodies[0], l => l.Contains("buf_0086_00"));
        Assert.Contains(bodies[0], l => l.Contains("buf_0086[x]"));
    }

    [Fact]
    public void SoftCpuReadability_DoesNotDropMultiTargetGotoLabels()
    {
        var body = new List<string>
        {
            "if (a == 0x0D) goto L_EAD7;",
            "if (a == 0x0C) goto L_EAD7;",
            "a = 1;",
            "L_EAD7: ;",
            "a = 2;",
        };
        SoftCpuReadabilityPass.ReduceLocalGotoIslands(body);
        Assert.Contains(body, l => l.Contains("L_EAD7"));
        Assert.Contains(body, l => l.Contains("goto L_EAD7"));
    }

    [Fact]
    public void SoftCpuReadability_SugarsAdc0AndFoldsPassThroughA()
    {
        var body = new List<string>
        {
            "a = buf_0086_00;",
            "a += 8;",
            "a &= 0xF0;",
            "obj_0087[x + 8] = a;",
            "a = buf_006D_00;",
            "a = (byte)(a + 0 + (carry ? 1 : 0));",
            "obj_006E[x + 8] = a;",
            "a = obj_009F_16;",
            "obj_00B6[x + 8] = a;",
        };
        SoftCpuReadabilityPass.CollapseCarryAdc(body);
        SoftCpuReadabilityPass.FoldPassThroughA(body);
        SoftCpuReadabilityPass.FoldAdc0ThroughA(body);
        Assert.Contains(body, l => l.Contains("obj_006E[x + 8] = (byte)(buf_006D_00 + (carry ? 1 : 0))")
            || l.Contains("obj_006E[x + 8] += (byte)(carry ? 1 : 0)"));
        Assert.Contains(body, l => l.Contains("obj_00B6[x + 8] = obj_009F_16"));
        Assert.DoesNotContain(body, l => l.Contains("a + 0 + (carry"));
    }

    [Fact]
    public void SoftCpuReadability_FoldsAdc0IntoDest()
    {
        var body = new List<string>
        {
            "a = game[0x1E];",
            "a += (byte)(carry ? 1 : 0);",
            "game[0x1E] = a;",
            "a = hi;",
            "a += (byte)(carry ? 1 : 0);",
            "dest = a;",
            "alias = a;",
        };
        SoftCpuReadabilityPass.FoldAdc0ThroughA(body);
        Assert.Contains(body, l => l == "game[0x1E] += (byte)(carry ? 1 : 0);");
        Assert.Contains(body, l => l == "dest = (byte)(hi + (carry ? 1 : 0));");
        Assert.Contains(body, l => l == "alias = dest;");
        Assert.DoesNotContain(body, l => l.TrimStart().StartsWith("a +=", StringComparison.Ordinal));
    }

    [Fact]
    public void SoftCpuReadability_RaisesAccAluOffRegisters()
    {
        var body = new List<string>
        {
            "a = 0xF8;",
            "oam_y[y] = a;",
            "a = lo;",
            "a += 8;",
            "a &= 0xF0;",
            "dest_lo = a;",
            "scroll = a;",
        };
        SoftCpuReadabilityPass.RaiseAccAluToDest(body);
        SoftCpuReadabilityPass.FoldPassThroughA(body);
        Assert.Contains(body, l => l == "oam_y[y] = 0xF8;");
        Assert.Contains(body, l => l == "dest_lo = (byte)((byte)(lo + 8) & 0xF0);");
        Assert.Contains(body, l => l == "scroll = dest_lo;");
        Assert.DoesNotContain(body, l => l.TrimStart().StartsWith("a =", StringComparison.Ordinal));
    }

    [Fact]
    public void SoftCpuReadability_RaisesCarryAluAndMulAndSkipsComments()
    {
        var adc = new List<string>
        {
            "a = lo;",
            "// keep carry clear",
            "carry = false;",
            "a = (byte)(a + hi + (carry ? 1 : 0));",
            "sum = a;",
            "a = width;",
            "carry = true;",
            "a = (byte)(a - 1 - (carry ? 0 : 1));",
            "width = a;",
            "a = tile;",
            "// scale",
            "a *= 4;",
            "tile = a;",
        };
        SoftCpuReadabilityPass.RaiseCarryAluToDest(adc);
        SoftCpuReadabilityPass.RaiseAccAluToDest(adc);
        Assert.Contains(adc, l => l == "sum = (byte)(lo + hi);");
        Assert.Contains(adc, l => l == "width = (byte)(width - 1);");
        Assert.Contains(adc, l => l == "tile = (byte)(tile * 4);");
        Assert.DoesNotContain(adc, l => l.TrimStart().StartsWith("a =", StringComparison.Ordinal));
        Assert.DoesNotContain(adc, l => l.Contains("carry ="));

        var liveA = new List<string>
        {
            "carry = false;",
            "a = (byte)(a + game[0x41] + (carry ? 1 : 0));",
            "game[0x41] = a;",
            "carry = true;",
            "a = (byte)(a - 0x20 - (carry ? 0 : 1));",
            "zp_02 = a;",
        };
        SoftCpuReadabilityPass.RaiseCarryAluToDest(liveA);
        Assert.Contains(liveA, l => l == "game[0x41] = (byte)(a + game[0x41]);");
        Assert.Contains(liveA, l => l == "zp_02 = (byte)(a - 0x20);");
        Assert.DoesNotContain(liveA, l => l.Contains("carry ="));

        var fold = new List<string>
        {
            "a = src;",
            "// copy",
            "dest = a;",
        };
        SoftCpuReadabilityPass.FoldPassThroughA(fold);
        Assert.Equal(new List<string> { "dest = src;" }, fold);

        var peekPla = new List<string>
        {
            "a = peek((ushort)(0x84B7 + y));",
            "a &= 0x0F;",
            "buf_0134[x] = a;",
            "a = pla();",
            "a &= (byte)~(PADMSB.START | PADMSB.SELECT);",
            "game[x] = a;",
            "a = buf_0110[x];",
            "a *= 2;",
            "x = a;",
            "a = status;",
            "if ((a & 1) == 0) return;",
        };
        SoftCpuReadabilityPass.RaiseAccAluToDest(peekPla);
        SoftCpuReadabilityPass.RaiseLoadIntoMaskedIf(peekPla);
        Assert.Contains(peekPla, l => l.Contains("buf_0134[x] = (byte)(peek((ushort)(0x84B7 + y)) & 0x0F)"));
        Assert.Contains(peekPla, l => l.Contains("game[x] = (byte)(pla() & (byte)~(PADMSB.START | PADMSB.SELECT))"));
        Assert.Contains(peekPla, l => l == "x = (byte)(buf_0110[x] * 2);");
        Assert.Contains(peekPla, l => l.Contains("if ((status & 1) == 0) return;"));

        var mixed = new List<string>
        {
            "a = ppu_ctrl_shadow;",
            "a &= (byte)~PPUCTRL.NAMETABLE_X;",
            "a = (byte)(a | vram_ptr_lo);",
            "ppu_ctrl_shadow = a;",
            "a = vram_buf[0xC4];",
            "a &= 0xFC;",
            "a = (byte)(a | vram_ptr_lo);",
            "vram_buf[0xC4] = a;",
            "a = x;",
            "a ^= 0xFF;",
            "ram_0490 = a;",
        };
        SoftCpuReadabilityPass.RaiseAccAluToDest(mixed);
        Assert.Contains(mixed, l => l.Contains("ppu_ctrl_shadow = (byte)((byte)(ppu_ctrl_shadow & (byte)~PPUCTRL.NAMETABLE_X) | vram_ptr_lo)"));
        Assert.Contains(mixed, l => l.Contains("vram_buf[0xC4] = (byte)((byte)(vram_buf[0xC4] & 0xFC) | vram_ptr_lo)"));
        Assert.Contains(mixed, l => l == "ram_0490 = (byte)(x ^ 0xFF);");
        Assert.DoesNotContain(mixed, l => l.TrimStart().StartsWith("a =", StringComparison.Ordinal));

        var shifts = new List<string>
        {
            "a = vram_ptr_hi;",
            "a >>= 2;",
            "vram_ptr_hi = a;",
            "a = zp_09;",
            "a >>= 2;",
            "x = a;",
            "a = peek((ushort)(0x84B7 + y));",
            "a >>= 4;",
            "x = a;",
            "a = (byte)(a + 0xC0 + (carry ? 1 : 0));",
            "vram_ptr_hi = a;",
            "a = buf_046B[y + 6];",
            "carry = false;",
            "zp_02 = a;",
        };
        SoftCpuReadabilityPass.RaiseAccAluToDest(shifts);
        SoftCpuReadabilityPass.FoldPassThroughA(shifts);
        Assert.Contains(shifts, l => l == "vram_ptr_hi = (byte)(vram_ptr_hi >> 2);");
        Assert.Contains(shifts, l => l == "x = (byte)(zp_09 >> 2);");
        Assert.Contains(shifts, l => l.Contains("x = (byte)(peek((ushort)(0x84B7 + y)) >> 4)"));
        Assert.Contains(shifts, l => l.Contains("vram_ptr_hi = (byte)(a + 0xC0 + (carry ? 1 : 0))"));
        Assert.Contains(shifts, l => l == "carry = false;");
        Assert.Contains(shifts, l => l == "zp_02 = buf_046B[y + 6];");
        Assert.DoesNotContain(shifts, l => l.TrimStart().StartsWith("a =", StringComparison.Ordinal));

        var lsr = new List<string>
        {
            "a = ram_07BE;",
            "carry = (a & 1) != 0; a >>= 1;",
            "y = a;",
            "a = ram_07BB;",
            "carry = (a & 1) != 0; a >>= 1;",
            "carry = (a & 1) != 0; a >>= 1;",
            "slot = a;",
        };
        SoftCpuReadabilityPass.RaiseLsrToDest(lsr);
        Assert.Contains(lsr, l => l == "y = (byte)(ram_07BE >> 1);");
        Assert.Contains(lsr, l => l == "slot = (byte)(ram_07BB >> 2);");
        Assert.DoesNotContain(lsr, l => l.TrimStart().StartsWith("a =", StringComparison.Ordinal));

        var bit = new List<string>
        {
            "a = game[0x7A];",
            "carry = (a & 1) != 0; a >>= 1;",
            "if (!carry)",
            "{",
            "    ClearOam();",
            "}",
            "a = ram_07BD;",
            "carry = (a & 1) != 0; a >>= 1;",
            "if (carry) { return; }",
            "y = a;",
        };
        SoftCpuReadabilityPass.RaiseLsrBitTest(bit);
        SoftCpuReadabilityPass.RaiseLsrToDest(bit);
        SoftCpuReadabilityPass.FoldPassThroughA(bit);
        Assert.Contains(bit, l => l.Contains("if ((game[0x7A] & 1) == 0)"));
        Assert.Contains(bit, l => l.Contains("if ((ram_07BD & 1) != 0) { return; }"));
        Assert.Contains(bit, l => l == "y = (byte)(ram_07BD >> 1);" || l == "y = a;");

        var call = new List<string>
        {
            "a = scroll;",
            "ppu_scroll(a, a);",
            "a = ppu_mask_shadow;",
            "ppu_mask_write((MASK)a);",
            "a = pla();",
            "a |= 0x80;",
            "ppu_ctrl((PPUCTRL)a);",
        };
        SoftCpuReadabilityPass.RaiseAccIntoCall(call);
        Assert.Contains(call, l => l == "ppu_scroll(scroll, scroll);");
        Assert.Contains(call, l => l == "ppu_mask_write((MASK)ppu_mask_shadow);");
        Assert.Contains(call, l => l.Contains("ppu_ctrl((PPUCTRL)(byte)(pla() | 0x80))"));
        Assert.DoesNotContain(call, l => l.TrimStart().StartsWith("a =", StringComparison.Ordinal));

        var collapse = new List<string>
        {
            "a = ppu_ctrl_shadow;",
            "a |= (byte)PPUCTRL.NMI;",
            "func_00_8EED();",
            "a = game[0x7A];",
            "y = (byte)(a + 1);",
            "a = peek((ushort)(0x042C + x));",
            "carry = true;",
            "poke((ushort)((ushort)0x042C + x), a);",
            "a = ram_07BB;",
            "carry = (a & 1) != 0; a >>= 1;",
            "carry = (a & 1) != 0; a >>= 1;",
            "a &= 3;",
        };
        SoftCpuReadabilityPass.RaiseAccAluToDest(collapse);
        SoftCpuReadabilityPass.RaiseLoadIntoIndexInc(collapse);
        SoftCpuReadabilityPass.FoldPassThroughA(collapse);
        SoftCpuReadabilityPass.RaiseLsrToDest(collapse);
        Assert.Contains(collapse, l => l.Contains("a = (byte)(ppu_ctrl_shadow | (byte)PPUCTRL.NMI)"));
        Assert.Contains(collapse, l => l == "y = (byte)(game[0x7A] + 1);");
        Assert.Contains(collapse, l => l.Contains("poke(") && l.Contains("peek((ushort)(0x042C + x))"));
        Assert.Contains(collapse, l => l.Contains("carry = true;"));
        Assert.Contains(collapse, l => l == "a = (byte)(ram_07BB >> 2);");

        var more = new List<string>
        {
            "a = (byte)(pla() | 0x80);",
            "ppu_ctrl((PPUCTRL)a);",
            "a = (byte)(game[0x7A] >> 1);",
            "if (!carry)",
            "{",
            "    ClearOam();",
            "}",
            "a = buf_0110[x];",
            "carry = true;",
            "a = (byte)(a - 8 - (carry ? 0 : 1));",
            "ClearOam_E5C1();",
            "a = x;",
            "a += 7;",
            "}",
            "a = (byte)(game[0x25] & 0x1F);",
            "carry = true;",
            "vram_ptr_hi = (byte)((byte)(a - 4 - (carry ? 0 : 1)) & 0x1F);",
        };
        SoftCpuReadabilityPass.RaiseAccIntoCall(more);
        SoftCpuReadabilityPass.RaiseShiftedACarryTest(more);
        SoftCpuReadabilityPass.RaiseCarryAluToDest(more);
        SoftCpuReadabilityPass.RaiseAccAluToDest(more);
        SoftCpuReadabilityPass.RaiseAccIntoCall(more); // after carry-ALU collapse into a=
        SoftCpuReadabilityPass.DropDeadAccAtBlockEnd(more);
        Assert.Contains(more, l => l.Contains("ppu_ctrl((PPUCTRL)(byte)(pla() | 0x80))"));
        Assert.Contains(more, l => l.Contains("if ((game[0x7A] & 1) == 0)"));
        Assert.Contains(more, l => l.Contains("ClearOam_E5C1((byte)(buf_0110[x] - 8))"));
        Assert.Contains(more, l => l.Contains("vram_ptr_hi = (byte)((byte)((byte)(game[0x25] & 0x1F) - 4) & 0x1F)"));
        Assert.DoesNotContain(more, l => l.TrimStart() == "a = (byte)(x + 7);");

        var apu = new List<string>
        {
            "a = peek((ushort)(0xF3B0 + y));",
            "apu_pulse1_ctrl(a);",
        };
        SoftCpuReadabilityPass.RaiseAccIntoCall(apu);
        Assert.Contains(apu, l => l.Contains("apu_pulse1_ctrl(peek((ushort)(0xF3B0 + y)))"));
        Assert.DoesNotContain(apu, l => l.TrimStart().StartsWith("a =", StringComparison.Ordinal));

        var abiCall = new List<string>
        {
            "a = 0x24;",
            "func_00_8E2D();",
            "a = game[0x74];",
            "func_00_8E04();",
            "x = 1;",
            "a = obj_00CF[x];",
            "ClearOam_E5C1();",
            "oam_y[y] = (byte)(a + 8);",
        };
        SoftCpuReadabilityPass.RaiseAccIntoCall(abiCall);
        Assert.Contains(abiCall, l => l == "func_00_8E2D(0x24);");
        Assert.Contains(abiCall, l => l == "func_00_8E04(game[0x74]);");
        Assert.Contains(abiCall, l => l == "a = obj_00CF[x];"); // live after call
        Assert.Contains(abiCall, l => l == "ClearOam_E5C1();");

        var abiRet = new List<string>
        {
            "a = 1;",
            "func_00_B0E6(); return;",
            "a = buf_00CE[x];",
            "func_E42B(); return;",
            "a = 0xFF;",
            "L_F86A: ;",
            "apu_triangle_ctrl(a);",
        };
        SoftCpuReadabilityPass.RaiseAccIntoCall(abiRet);
        Assert.Contains(abiRet, l => l == "func_00_B0E6(1); return;");
        Assert.Contains(abiRet, l => l == "func_E42B(buf_00CE[x]); return;");
        Assert.Contains(abiRet, l => l == "L_F86A: ;");
        Assert.Contains(abiRet, l => l.Contains("apu_triangle_ctrl(0xFF)"));
        Assert.DoesNotContain(abiRet, l => l.TrimStart() == "a = 0xFF;");

        var foldBrace = new List<string>
        {
            "a = 0xFF;",
            "}",
            "obj_009F[x + 1] = a;",
            "a = 0x15;",
            "}",
            "if (a == 0x33) return;",
            "a = 0;",
            "L_BED1: ;",
            "obj[x + 8] = a;",
        };
        SoftCpuReadabilityPass.FoldPassThroughA(foldBrace);
        Assert.Contains(foldBrace, l => l == "obj_009F[x + 1] = 0xFF;");
        Assert.DoesNotContain(foldBrace, l => l.TrimStart() == "a = 0xFF;");
        Assert.Contains(foldBrace, l => l == "a = 0x15;"); // still live in if (a == …)
        Assert.Contains(foldBrace, l => l == "L_BED1: ;");
        Assert.Contains(foldBrace, l => l == "obj[x + 8] = 0;");
        Assert.DoesNotContain(foldBrace, l => l.TrimStart() == "a = 0;");

        var deadLitCall = new List<string>
        {
            "a = 0xFF;",
            "L_F86A: ;",
            "apu_triangle_ctrl(0xFF);",
            "ram_07BA--;",
        };
        SoftCpuReadabilityPass.DropDeadAccWhenUnused(deadLitCall);
        Assert.DoesNotContain(deadLitCall, l => l.TrimStart() == "a = 0xFF;");
        Assert.Contains(deadLitCall, l => l == "L_F86A: ;");
        Assert.Contains(deadLitCall, l => l == "apu_triangle_ctrl(0xFF);");

        var cmpTail = new List<string>
        {
            "a = buf_00CE_00;",
            "zero = a == 0xD0; carry = a >= 0xD0; negative = (byte)(a - 0xD0) >= 0x80;",
            "}",
        };
        SoftCpuReadabilityPass.RaiseLoadIntoCmpFlags(cmpTail);
        Assert.DoesNotContain(cmpTail, l => l.TrimStart().StartsWith("a =", StringComparison.Ordinal));
        Assert.Contains(cmpTail, l => l.Contains("zero = buf_00CE_00 == 0xD0"));

        var condDest = new List<string>
        {
            "a = 0x20;",
            "if (y == 0) { a = 0x22; }",
            "vram_buf[x + 1] = a;",
            "a = 2;",
            "if (carry) { a |= 0xC0; }",
            "oam_attr[y] = a;",
            "a = 0x20;",
            "if (y < 3) { a |= 3; }",
            "obj[x] = a;",
        };
        SoftCpuReadabilityPass.RaiseCondAccToDest(condDest);
        Assert.Contains(condDest, l => l == "vram_buf[x + 1] = 0x20;");
        Assert.Contains(condDest, l => l.Contains("if (y == 0) { vram_buf[x + 1] = 0x22; }"));
        Assert.Contains(condDest, l => l == "oam_attr[y] = 2;");
        Assert.Contains(condDest, l => l.Contains("if (carry) { oam_attr[y] = (byte)(2 | 0xC0); }"));
        Assert.Contains(condDest, l => l == "obj[x] = 0x20;");
        Assert.Contains(condDest, l => l.Contains("if (y < 3) { obj[x] = (byte)(0x20 | 3); }"));
        Assert.DoesNotContain(condDest, l => l.TrimStart().StartsWith("a =", StringComparison.Ordinal));

        var cmpAcrossXy = new List<string>
        {
            "a = zp_EF;",
            "x = ptr_04_lo;",
            "if (a == 5) { func_EB64(); return; }",
            "a = peek((ushort)(0xF1E3 + x));",
            "x = ptr_04_lo;",
            "if (a != 0) return;", // must NOT raise — peek uses x before overwrite
        };
        SoftCpuReadabilityPass.RaiseLoadIntoCompare(cmpAcrossXy);
        Assert.Contains(cmpAcrossXy, l => l == "x = ptr_04_lo;");
        Assert.Contains(cmpAcrossXy, l => l.Contains("if (zp_EF == 5)"));
        Assert.Contains(cmpAcrossXy, l => l.TrimStart() == "a = peek((ushort)(0xF1E3 + x));");

        var deadFill = new List<string>
        {
            "a = 0;",
            "x = 6;",
            "do",
            "{",
            "buf_0110[x + 35] = 0;",
            "x--;",
            "}",
            "while (!negative);",
            "return;",
        };
        SoftCpuReadabilityPass.DropDeadAccWhenUnused(deadFill);
        Assert.DoesNotContain(deadFill, l => l.TrimStart() == "a = 0;");
        Assert.Contains(deadFill, l => l == "x = 6;");

        // AccUsedAfter must not look past return into later a uses in the same body list.
        var deadBeforeReturn = new List<string>
        {
            "a = 0;",
            "x = 6;",
            "buf_0110[x] = 0;",
            "return;",
            "oam_y[y] = (byte)(a + 8);",
        };
        SoftCpuReadabilityPass.DropDeadAccWhenUnused(deadBeforeReturn);
        Assert.DoesNotContain(deadBeforeReturn, l => l.TrimStart() == "a = 0;");
        Assert.Contains(deadBeforeReturn, l => l.Contains("oam_y[y]"));

        var abiAcrossInert = new List<string>
        {
            "a = 0xFD;",
            "y = game[0x52];",
            "if (y == 0) { }",
            "obj_009F[x + 1] = 0xFF;",
            "y = 1;",
            "func_E143();",
            "return;",
        };
        SoftCpuReadabilityPass.RaiseAccIntoCall(abiAcrossInert);
        Assert.Contains(abiAcrossInert, l => l.Contains("func_E143(0xFD)"));
        Assert.DoesNotContain(abiAcrossInert, l => l.TrimStart() == "a = 0xFD;");
        Assert.Contains(abiAcrossInert, l => l.Contains("y = game[0x52];"));

        var condCall = new List<string>
        {
            "a = 3;",
            "x = game[0x52];",
            "if (x == 0) { a = 4; }",
            "FlushVramBuf_8A97();",
            "return;",
        };
        SoftCpuReadabilityPass.RaiseCondAccIntoCall(condCall);
        Assert.Contains(condCall, l => l.Contains("if (x == 0) { FlushVramBuf_8A97(4); }"));
        Assert.Contains(condCall, l => l.Contains("else { FlushVramBuf_8A97(3); }"));
        Assert.DoesNotContain(condCall, l => l.TrimStart().StartsWith("a =", StringComparison.Ordinal));

        var phaSub = new List<string>
        {
            "a = buf_000F[x];",
            "pha();",
            "if (!((a & 0x80) != 0)) { pla(); return; }",
            "a = vram_buf[0xD4];",
            "pha();",
            "a &= 4;", // mutation — must not rewrite buf &= 4
            "if (a != 0) return;",
        };
        SoftCpuReadabilityPass.RaiseAccIntoPha(phaSub);
        Assert.Contains(phaSub, l => l == "pha(buf_000F[x]);");
        Assert.Contains(phaSub, l => l.Contains("if (!((buf_000F[x] & 0x80) != 0))"));
        // Mutation after pha → pha(expr); a = expr; (not expr &= which would clobber mem).
        Assert.Contains(phaSub, l => l == "pha(vram_buf[0xD4]);");
        Assert.Contains(phaSub, l => l == "a = vram_buf[0xD4];");
        Assert.Contains(phaSub, l => l.TrimStart() == "a &= 4;");
        Assert.DoesNotContain(phaSub, l => l.Contains("vram_buf[0xD4] &="));

        var phaMut = new List<string>
        {
            "a = y;",
            "pha();",
            "func_F1D7();",
            "a *= 16;",
            "vram_ptr_lo = (byte)(a | vram_ptr_lo);",
        };
        SoftCpuReadabilityPass.RaiseAccIntoPha(phaMut);
        Assert.Contains(phaMut, l => l == "pha(y);");
        Assert.Contains(phaMut, l => l == "a = y;");
        Assert.Contains(phaMut, l => l == "a *= 16;");

        var fillIf = new List<string>
        {
            "a = 0;",
            "x = 4;",
            "do",
            "{",
            "if (zero) { buf_000F[x] = a; }",
            "x--;",
            "}",
            "while (!negative);",
            "return;",
        };
        SoftCpuReadabilityPass.RaiseLiteralFillThroughA(fillIf);
        Assert.DoesNotContain(fillIf, l => l.TrimStart() == "a = 0;");
        Assert.Contains(fillIf, l => l.Contains("if (zero) { buf_000F[x] = 0; }"));

        var carryXor = new List<string>
        {
            "a = game[0x24];",
            "if (!carry) { a ^= 4; }",
            "a &= 4;",
            "a |= 0x23;",
            "vram_ptr_lo = a;",
        };
        SoftCpuReadabilityPass.RaiseCarryCondAluToDest(carryXor);
        Assert.Contains(carryXor, l => l == "vram_ptr_lo = game[0x24];");
        Assert.Contains(carryXor, l => l.Contains("if (!carry) { vram_ptr_lo = (byte)(vram_ptr_lo ^ 4); }"));
        Assert.Contains(carryXor, l => l.Contains("vram_ptr_lo = (byte)((byte)(vram_ptr_lo & 4) | 0x23)"));
        Assert.DoesNotContain(carryXor, l => l.TrimStart().StartsWith("a =", StringComparison.Ordinal));

        var carryXorLive = new List<string>
        {
            "a = game[0x24];",
            "if (!carry) { a ^= 4; }",
            "a &= 4;",
            "a |= 0x23;",
            "vram_ptr_lo = a;",
            "carry = (a & 1) != 0; a >>= 1;",
            "vram_buf[x + 249] = a;",
        };
        SoftCpuReadabilityPass.RaiseCarryCondAluToDest(carryXorLive);
        Assert.Contains(carryXorLive, l => l == "vram_ptr_lo = game[0x24];");
        Assert.Contains(carryXorLive, l => l.Contains("carry = (vram_ptr_lo & 1) != 0"));
        Assert.Contains(carryXorLive, l => l.Contains("vram_buf[x + 249] = vram_ptr_lo"));
        Assert.DoesNotContain(carryXorLive, l => l.TrimStart().StartsWith("a =", StringComparison.Ordinal)
            || l.TrimStart().StartsWith("a &=", StringComparison.Ordinal)
            || l.TrimStart().StartsWith("a >>=", StringComparison.Ordinal));

        var phaRaise = new List<string>
        {
            "a = game[x + 94];",
            "pha();",
            "game[x + 94] = game[x + 101];",
            "game[x + 101] = pla();",
            "a = (byte)(x << 2);",
            "pha();",
            "y = a;",
            "a = ppu_ctrl_shadow;",
            "pha();",
            "ppu_ctrl((PPUCTRL)a);",
            "a = oam_tile[x];",
            "pha();",
            "a = 1;",
        };
        SoftCpuReadabilityPass.RaiseAccIntoPha(phaRaise);
        Assert.Contains(phaRaise, l => l == "pha(game[x + 94]);");
        Assert.Contains(phaRaise, l => l == "pha((byte)(x << 2));");
        Assert.Contains(phaRaise, l => l == "y = (byte)(x << 2);");
        Assert.Contains(phaRaise, l => l == "pha(ppu_ctrl_shadow);");
        Assert.Contains(phaRaise, l => l.Contains("ppu_ctrl((PPUCTRL)ppu_ctrl_shadow)"));
        Assert.Contains(phaRaise, l => l == "pha(oam_tile[x]);");
        Assert.Contains(phaRaise, l => l == "a = 1;");
        Assert.DoesNotContain(phaRaise, l => l.TrimStart() is "pha();" or "push(a);");
        Assert.DoesNotContain(phaRaise, l => l.TrimStart() is "a = game[x + 94];" or "a = (byte)(x << 2);"
            or "a = ppu_ctrl_shadow;" or "a = oam_tile[x];");

        var deadRe = new List<string>
        {
            "a = game[0x44];",
            "a = ppu_ctrl_shadow;",
            "pha();",
            "a = (byte)(ram_07BD >> 1);",
            "a = peek((ushort)(0xF4D9 + y));",
            "a = (byte)(pla() >> 2);",
            "a = 2;",
        };
        SoftCpuReadabilityPass.DropDeadAccBeforeReassign(deadRe);
        Assert.DoesNotContain(deadRe, l => l.Contains("game[0x44]"));
        Assert.DoesNotContain(deadRe, l => l.Contains("ram_07BD >> 1"));
        Assert.Contains(deadRe, l => l == "a = ppu_ctrl_shadow;");
        Assert.Contains(deadRe, l => l == "pla();");
        Assert.Contains(deadRe, l => l == "a = 2;");

        var cmpFlags = new List<string>
        {
            "a = buf_00CE_00;",
            "zero = a == 0x99; carry = a >= 0x99; negative = (byte)(a - 0x99) >= 0x80;",
            "y = 0;",
            "a = 1;",
            "if (carry)",
            "{",
            "    zp_1D = 3;",
            "}",
            "a = obj_00CF[x + 8];",
            "zero = a == 0xF0; carry = a >= 0xF0; negative = (byte)(a - 0xF0) >= 0x80;",
            "a = pla();",
            "if (!carry) goto L_BED1;",
        };
        SoftCpuReadabilityPass.RaiseLoadIntoCmpFlags(cmpFlags);
        Assert.Contains(cmpFlags, l => l.Contains("zero = buf_00CE_00 == 0x99"));
        Assert.Contains(cmpFlags, l => l.Contains("zero = obj_00CF[x + 8] == 0xF0"));
        Assert.DoesNotContain(cmpFlags, l => l.TrimStart() == "a = buf_00CE_00;");
        Assert.DoesNotContain(cmpFlags, l => l.TrimStart() == "a = obj_00CF[x + 8];");
        Assert.Contains(cmpFlags, l => l == "a = 1;");
        Assert.Contains(cmpFlags, l => l == "a = pla();");

        var memCmp = new List<string>
        {
            "a = obj_0087[x];",
            "{ byte _v = vram_ptr_hi; zero = a == _v; carry = a >= _v; negative = (byte)(a - _v) >= 0x80; }",
            "if ((byte)(obj_006E[x] - vram_ptr_lo - (carry ? 0 : 1)) < 0x80) return;",
            "a = ptr_06_hi;",
            "{ byte _v = obj_0087[x]; zero = a == _v; carry = a >= _v; negative = (byte)(a - _v) >= 0x80; }",
            "a = ptr_06_lo;",
            "if (!carry) return;",
            "a = (byte)(vram_buf[0xD1] & 0x0F);",
            "zero = a == 0x0F; carry = a >= 0x0F; negative = (byte)(a - 0x0F) >= 0x80;",
            "return;",
            "a = game[0x85];",
            "{ byte _v = game[0x15]; zero = a == _v; carry = a >= _v; negative = (byte)(a - _v) >= 0x80; }",
            "game[0x15] = 0;",
            "game[0x15] = a;",
        };
        SoftCpuReadabilityPass.RaiseLoadIntoMemCmpBlock(memCmp);
        SoftCpuReadabilityPass.RaiseLoadIntoCmpFlags(memCmp);
        Assert.Contains(memCmp, l => l.Contains("zero = obj_0087[x] == _v"));
        Assert.Contains(memCmp, l => l.Contains("zero = ptr_06_hi == _v"));
        Assert.Contains(memCmp, l => l.Contains("zero = (byte)(vram_buf[0xD1] & 0x0F) == 0x0F"));
        Assert.Contains(memCmp, l => l.Contains("zero = game[0x85] == _v"));
        Assert.Contains(memCmp, l => l == "game[0x15] = game[0x85];");
        Assert.Contains(memCmp, l => l == "a = ptr_06_lo;"); // live load after cmp flags
        Assert.DoesNotContain(memCmp, l => l.TrimStart() is "a = obj_0087[x];" or "a = ptr_06_hi;"
            or "a = (byte)(vram_buf[0xD1] & 0x0F);" or "a = game[0x85];");

        var idxIncByte = new List<string>
        {
            "a = (byte)(zp_02 ^ 0xFF);",
            "y = (byte)(a + 1);",
        };
        SoftCpuReadabilityPass.RaiseLoadIntoIndexInc(idxIncByte);
        Assert.Contains(idxIncByte, l => l == "y = (byte)((byte)(zp_02 ^ 0xFF) + 1);");
        Assert.DoesNotContain(idxIncByte, l => l.TrimStart().StartsWith("a =", StringComparison.Ordinal));

        var aluSkip = new List<string>
        {
            "a = game[0x7A];",
            "y = (byte)(game[0x7A] + 1);",
            "zp_FA = y;",
            "a ^= 1;",
            "a |= 0x80;",
            "game[0x7A] = a;",
        };
        SoftCpuReadabilityPass.RaiseAccAluToDest(aluSkip);
        Assert.Contains(aluSkip, l => l == "y = (byte)(game[0x7A] + 1);");
        Assert.Contains(aluSkip, l => l == "zp_FA = y;");
        Assert.Contains(aluSkip, l => l.Contains("game[0x7A] = (byte)((byte)(game[0x7A] ^ 1) | 0x80)"));
        Assert.DoesNotContain(aluSkip, l => l.TrimStart().StartsWith("a =", StringComparison.Ordinal));

        var aluAcrossBrace = new List<string>
        {
            "else",
            "{",
            "a = game[0x7A];",
            "y = (byte)(game[0x7A] + 1);",
            "zp_FA = y;",
            "a ^= 1;",
            "a |= 0x80;",
            "}",
            "game[0x7A] = a;",
        };
        SoftCpuReadabilityPass.RaiseAccAluToDest(aluAcrossBrace);
        Assert.Contains(aluAcrossBrace, l => l == "}");
        Assert.Contains(aluAcrossBrace, l => l.Contains("game[0x7A] = (byte)((byte)(game[0x7A] ^ 1) | 0x80)"));
        Assert.DoesNotContain(aluAcrossBrace, l => l.TrimStart().StartsWith("a =", StringComparison.Ordinal)
            || l.TrimStart() == "game[0x7A] = a;");

        // AccAlu may leave a=expr before } when store is outside; must not drop it.
        var liveAcrossBrace = new List<string>
        {
            "a = (byte)((byte)(game[0x7A] ^ 1) | 0x80);",
            "}",
            "game[0x7A] = a;",
        };
        SoftCpuReadabilityPass.DropDeadAccAtBlockEnd(liveAcrossBrace);
        Assert.Contains(liveAcrossBrace, l => l.TrimStart().StartsWith("a =", StringComparison.Ordinal));
        Assert.Contains(liveAcrossBrace, l => l == "game[0x7A] = a;");

        var fill = new List<string>
        {
            "a = 0;",
            "x = 6;",
            "do",
            "{",
            "buf_0110[x + 35] = a;",
            "x--;",
            "}",
            "while (!negative);",
        };
        SoftCpuReadabilityPass.RaiseLiteralFillThroughA(fill);
        Assert.Contains(fill, l => l.Contains("buf_0110[x + 35] = 0;"));
        Assert.DoesNotContain(fill, l => l.TrimStart() == "a = 0;");

        var deadPla = new List<string>
        {
            "a = pla();",
            "}",
            "a = game[0x44];",
            "}",
            "a = peek((ushort)(0xE878 + x));",
            "return;",
        };
        SoftCpuReadabilityPass.DropDeadAccAtBlockEnd(deadPla);
        Assert.Contains(deadPla, l => l == "pla();");
        Assert.DoesNotContain(deadPla, l => l.Contains("game[0x44]"));
        Assert.Contains(deadPla, l => l.Contains("peek((ushort)(0xE878 + x))")); // keep HW/ABI load before return

        var aslBit = new List<string>
        {
            "a = (byte)(zp_0A * 2);",
            "if (carry) { func_F068(); return; }",
            "a = (byte)(obj[x + 6] * 2);",
            "if (carry) goto L_B6F3;",
            "if (zero) goto L_B6F2;",
        };
        SoftCpuReadabilityPass.RaiseAslBitTest(aslBit);
        Assert.Contains(aslBit, l => l.Contains("if ((zp_0A & 0x80) != 0)"));
        Assert.DoesNotContain(aslBit, l => l.TrimStart() == "a = (byte)(zp_0A * 2);");
        // zero-after-ASL must keep residual ASL into a
        Assert.Contains(aslBit, l => l.Contains("a = (byte)(obj[x + 6] * 2)"));

        // AccAlu collapse can emit *2 after the early AslBitTest — ApplyBodyLocalPasses re-raises.
        var aslAfterAlu = new List<string>
        {
            "a = zp_0A;",
            "a *= 2;",
            "if (carry) { func_F068(); return; }",
        };
        SoftCpuReadabilityPass.ApplyBodyLocalPasses(aslAfterAlu);
        Assert.Contains(aslAfterAlu, l => l.Contains("if ((zp_0A & 0x80) != 0)"));
        Assert.DoesNotContain(aslAfterAlu, l => l.TrimStart().StartsWith("a =", StringComparison.Ordinal));

        var deadUnused = new List<string>
        {
            "a = game[0x08];",
            "if (game[0x86] == 0)",
            "{",
            "    a = obj_009F_00;",
            "    return;",
            "}",
            "a = game[0x04];",
            "a = 0x0C;",
            "func_F388();",
        };
        SoftCpuReadabilityPass.DropDeadAccWhenUnused(deadUnused);
        Assert.DoesNotContain(deadUnused, l => l.Contains("game[0x08]"));
        Assert.Contains(deadUnused, l => l == "a = 0x0C;"); // ABI for call

        var shiftAdc = new List<string>
        {
            "a = (byte)(vram_ptr_hi >> 2);",
            "vram_ptr_hi = (byte)(a + 0xC0 + (carry ? 1 : 0));",
        };
        SoftCpuReadabilityPass.RaiseShiftAdcToDest(shiftAdc);
        Assert.Contains(shiftAdc, l => l.Contains("vram_ptr_hi = (byte)((byte)(vram_ptr_hi >> 2) + 0xC0 + (carry ? 1 : 0))"));
        Assert.DoesNotContain(shiftAdc, l => l.TrimStart().StartsWith("a =", StringComparison.Ordinal));

        var peekPoke = new List<string>
        {
            "a = peek((ushort)(0x0417 + x));",
            "poke((ushort)((ushort)0x0417 + x), (byte)(a + 0xFF + (carry ? 1 : 0)));",
        };
        SoftCpuReadabilityPass.RaisePeekPokeCarryAdc(peekPoke);
        Assert.Contains(peekPoke, l => l.Contains("poke((ushort)((ushort)0x0417 + x), (byte)(peek((ushort)(0x0417 + x)) + 0xFF + (carry ? 1 : 0)))"));

        var inertClc = new List<string>
        {
            "a = buf_00CE_00;",
            "carry = false;",
            "a &= 0xF0;",
            "obj_00CF[x + 8] = a;",
        };
        SoftCpuReadabilityPass.RaiseAccAluToDest(inertClc);
        Assert.Contains(inertClc, l => l.Contains("obj_00CF[x + 8] = (byte)(buf_00CE_00 & 0xF0)"));
        Assert.DoesNotContain(inertClc, l => l.TrimStart().StartsWith("a =", StringComparison.Ordinal));
        Assert.DoesNotContain(inertClc, l => l.Contains("carry = false"));

        var liveCarry = new List<string>
        {
            "a = buf_0110[x + 14];",
            "a = (byte)(a - 8 - (carry ? 0 : 1));",
            "ClearOam_E5C1();",
            "a = vram_ptr_hi;",
            "if ((byte)(a - buf_006D_00 - (carry ? 0 : 1)) < 0x80) goto L_B7F5;",
            "a = game[0x41];",
            "zero = a == 0x20; carry = a >= 0x20; negative = (byte)(a - 0x20) >= 0x80;",
            "if (negative) return;",
            "game[0x41] = (byte)(game[0x41] - 0x20 - (carry ? 0 : 1));",
        };
        SoftCpuReadabilityPass.RaiseLiveCarryAlu(liveCarry);
        SoftCpuReadabilityPass.RaiseLoadIntoCarryCompare(liveCarry);
        SoftCpuReadabilityPass.RaiseLoadIntoCmpFlags(liveCarry);
        Assert.Contains(liveCarry, l => l.Contains("a = (byte)(buf_0110[x + 14] - 8 - (carry ? 0 : 1))"));
        Assert.Contains(liveCarry, l => l.Contains("if ((byte)(vram_ptr_hi - buf_006D_00 - (carry ? 0 : 1)) < 0x80)"));
        Assert.DoesNotContain(liveCarry, l => l.TrimStart() == "a = vram_ptr_hi;");
        Assert.Contains(liveCarry, l => l.Contains("zero = game[0x41] == 0x20"));
        Assert.DoesNotContain(liveCarry, l => l.TrimStart() == "a = game[0x41];");

        var dupFlags = new List<string>
        {
            "zero = a == 0x47; carry = a >= 0x47; negative = (byte)(a - 0x47) >= 0x80;",
            "zero = a == 0x47; carry = a >= 0x47; negative = (byte)(a - 0x47) >= 0x80;",
            "zero = a == 0x47; carry = a >= 0x47; negative = (byte)(a - 0x47) >= 0x80;",
            "y++;",
        };
        SoftCpuReadabilityPass.DropDuplicateCmpFlags(dupFlags);
        Assert.Equal(1, dupFlags.Count(l => l.Contains("zero = a == 0x47")));
    }

    [Fact]
    public void SoftCpu_LowersRaisedMemCmpFlags()
    {
        var basePrg = new byte[0x4000];
        Array.Fill(basePrg, (byte)0xEA);
        const string program = """
            byte slot; // $10
            // bank=0 $8000
            void reset()
            {
                zero = slot == 0x99; carry = slot >= 0x99; negative = (byte)(slot - 0x99) >= 0x80;
                if (carry) return;
                return;
            }
            """;
        var result = SoftCpuRecompiler.Recompile(program, basePrg);
        Assert.True(result.RoutinesLowered >= 1, string.Join("; ", result.Diagnostics));
        // LDA $10; CMP #$99; BCC over RTS; RTS
        Assert.Equal(0xA5, result.PrgRom[0]); // LDA zp
        Assert.Equal(0x10, result.PrgRom[1]);
        Assert.Equal(0xC9, result.PrgRom[2]); // CMP #
        Assert.Equal(0x99, result.PrgRom[3]);
        Assert.Equal(0x90, result.PrgRom[4]); // BCC (skip return when !carry)
    }

    [Fact]
    public void SoftCpu_LowersShiftAdcWithLiveCarry()
    {
        var basePrg = new byte[0x4000];
        Array.Fill(basePrg, (byte)0xEA);
        const string program = """
            byte vram_ptr_hi; // $10
            // bank=0 $8000
            void reset()
            {
                vram_ptr_hi = (byte)((byte)(vram_ptr_hi >> 2) + 0xC0 + (carry ? 1 : 0));
                return;
            }
            """;
        var result = SoftCpuRecompiler.Recompile(program, basePrg);
        Assert.True(result.RoutinesLowered >= 1, string.Join("; ", result.Diagnostics));
        // LDA; LSR; LSR; ADC #$C0; STA — no CLC before ADC
        Assert.Equal(0xA5, result.PrgRom[0]); // LDA zp
        Assert.Equal(0x10, result.PrgRom[1]);
        Assert.Equal(0x4A, result.PrgRom[2]); // LSR A
        Assert.Equal(0x4A, result.PrgRom[3]); // LSR A
        Assert.Equal(0x69, result.PrgRom[4]); // ADC #
        Assert.Equal(0xC0, result.PrgRom[5]);
        Assert.Equal(0x85, result.PrgRom[6]); // STA zp
        Assert.Equal(0x10, result.PrgRom[7]);
    }

    [Fact]
    public void SoftCpu_LowersPhaExpr()
    {
        var basePrg = new byte[0x4000];
        Array.Fill(basePrg, (byte)0xEA);
        const string program = """
            byte zp_09; // $09
            // bank=0 $8000
            void reset()
            {
                pha(zp_09);
                pha((byte)(zp_09 >> 1));
                pha(y);
                return;
            }
            """;
        var result = SoftCpuRecompiler.Recompile(program, basePrg);
        Assert.True(result.RoutinesLowered >= 1, string.Join("; ", result.Diagnostics));
        // LDA zp; PHA; LDA zp; LSR; PHA; TYA; PHA
        Assert.Equal(0xA5, result.PrgRom[0]); // LDA zp
        Assert.Equal(0x09, result.PrgRom[1]);
        Assert.Equal(0x48, result.PrgRom[2]); // PHA
        Assert.Equal(0xA5, result.PrgRom[3]); // LDA zp
        Assert.Equal(0x09, result.PrgRom[4]);
        Assert.Equal(0x4A, result.PrgRom[5]); // LSR A
        Assert.Equal(0x48, result.PrgRom[6]); // PHA
        Assert.Equal(0x98, result.PrgRom[7]); // TYA
        Assert.Equal(0x48, result.PrgRom[8]); // PHA
    }

    [Fact]
    public void SoftCpu_LowersMemCmpBlock()
    {
        var basePrg = new byte[0x4000];
        Array.Fill(basePrg, (byte)0xEA);
        const string program = """
            byte hi; // $10
            byte lo; // $11
            // bank=0 $8000
            void reset()
            {
                { byte _v = lo; zero = hi == _v; carry = hi >= _v; negative = (byte)(hi - _v) >= 0x80; }
                if (!carry) return;
                return;
            }
            """;
        var result = SoftCpuRecompiler.Recompile(program, basePrg);
        Assert.True(result.RoutinesLowered >= 1, string.Join("; ", result.Diagnostics));
        // LDA hi; CMP lo; BCC; RTS
        Assert.Equal(0xA5, result.PrgRom[0]); // LDA hi
        Assert.Equal(0x10, result.PrgRom[1]);
        Assert.Equal(0xC5, result.PrgRom[2]); // CMP zp
        Assert.Equal(0x11, result.PrgRom[3]);
    }

    [Fact]
    public void SoftCpu_LowersCallWithAccArg()
    {
        var basePrg = new byte[0x4000];
        Array.Fill(basePrg, (byte)0xEA);
        // Seed a JSR target at $8100 so nameToAddress resolves.
        basePrg[0x100] = 0x60; // RTS at $8100
        const string program = """
            byte zp_09; // $09
            // bank=0 $8100
            void helper()
            {
                return;
            }
            // bank=0 $8000
            void reset()
            {
                helper(zp_09);
                helper(0x24);
                helper(1); return;
            }
            """;
        var result = SoftCpuRecompiler.Recompile(program, basePrg);
        Assert.True(result.RoutinesLowered >= 1, string.Join("; ", result.Diagnostics));
        // reset at $8000: LDA zp; JSR $8100; LDA #$24; JSR $8100; LDA #1; JMP $8100
        Assert.Equal(0xA5, result.PrgRom[0]); // LDA zp
        Assert.Equal(0x09, result.PrgRom[1]);
        Assert.Equal(0x20, result.PrgRom[2]); // JSR
        Assert.Equal(0x00, result.PrgRom[3]);
        Assert.Equal(0x81, result.PrgRom[4]);
        Assert.Equal(0xA9, result.PrgRom[5]); // LDA #
        Assert.Equal(0x24, result.PrgRom[6]);
        Assert.Equal(0x20, result.PrgRom[7]); // JSR
        Assert.Equal(0x00, result.PrgRom[8]);
        Assert.Equal(0x81, result.PrgRom[9]);
        Assert.Equal(0xA9, result.PrgRom[10]); // LDA #1
        Assert.Equal(0x01, result.PrgRom[11]);
        Assert.Equal(0x4C, result.PrgRom[12]); // JMP
        Assert.Equal(0x00, result.PrgRom[13]);
        Assert.Equal(0x81, result.PrgRom[14]);
    }

    [Fact]
    public void SoftCpu_LowersLiveCarrySbcCompare()
    {
        var basePrg = new byte[0x4000];
        Array.Fill(basePrg, (byte)0xEA);
        const string program = """
            byte hi; // $10
            byte lo; // $11
            // bank=0 $8000
            void reset()
            {
                if ((byte)(hi - lo - (carry ? 0 : 1)) < 0x80) goto L_B7F5;
                return;
                L_B7F5: ;
                a = (byte)(hi - 8 - (carry ? 0 : 1));
                return;
            }
            """;
        var result = SoftCpuRecompiler.Recompile(program, basePrg);
        Assert.True(result.RoutinesLowered >= 1, string.Join("; ", result.Diagnostics));
        // LDA hi; SBC lo; CMP #$80; BCC L_OK
        Assert.Equal(0xA5, result.PrgRom[0]); // LDA hi
        Assert.Equal(0x10, result.PrgRom[1]);
        Assert.Equal(0xE5, result.PrgRom[2]); // SBC zp
        Assert.Equal(0x11, result.PrgRom[3]);
    }

    [Fact]
    public void SoftCpu_LowersRaisedCarryAluAndMul()
    {
        var basePrg = new byte[0x4000];
        Array.Fill(basePrg, (byte)0xEA);
        const string program = """
            byte lo; // $10
            byte hi; // $11
            byte sum; // $12
            byte tile; // $13
            byte shadow; // $14
            byte ptr; // $15
            // bank=0 $8000
            void reset()
            {
                sum = (byte)(lo + hi);
                tile = (byte)(tile * 4);
                shadow = (byte)((byte)(shadow & 0xFE) | ptr);
                return;
            }
            """;
        var result = SoftCpuRecompiler.Recompile(program, basePrg);
        Assert.True(result.RoutinesLowered >= 1, string.Join("; ", result.Diagnostics));
        // LDA $10; CLC; ADC $11; STA $12; LDA $13; ASL; ASL; STA $13; RTS
        Assert.Equal(0xA5, result.PrgRom[0]); // LDA zp
        Assert.Equal(0x10, result.PrgRom[1]);
        Assert.Equal(0x18, result.PrgRom[2]); // CLC
        Assert.Equal(0x65, result.PrgRom[3]); // ADC zp
        Assert.Equal(0x11, result.PrgRom[4]);
        Assert.Equal(0x85, result.PrgRom[5]); // STA zp
        Assert.Equal(0x12, result.PrgRom[6]);
        Assert.Equal(0xA5, result.PrgRom[7]); // LDA tile
        Assert.Equal(0x13, result.PrgRom[8]);
        Assert.Equal(0x0A, result.PrgRom[9]); // ASL A
        Assert.Equal(0x0A, result.PrgRom[10]);
        Assert.Equal(0x85, result.PrgRom[11]); // STA tile
        Assert.Equal(0x13, result.PrgRom[12]);
        Assert.Equal(0xA5, result.PrgRom[13]); // LDA shadow
        Assert.Equal(0x14, result.PrgRom[14]);
        Assert.Equal(0x29, result.PrgRom[15]); // AND #
        Assert.Equal(0xFE, result.PrgRom[16]);
        Assert.Equal(0x05, result.PrgRom[17]); // ORA zp
        Assert.Equal(0x15, result.PrgRom[18]);
        Assert.Equal(0x85, result.PrgRom[19]); // STA shadow
        Assert.Equal(0x14, result.PrgRom[20]);
    }

    [Fact]
    public void SoftCpu_LowersRaisedShiftIntoDest()
    {
        var basePrg = new byte[0x4000];
        Array.Fill(basePrg, (byte)0xEA);
        const string program = """
            byte hi; // $10
            // bank=0 $8000
            void reset()
            {
                hi = (byte)(hi >> 2);
                return;
            }
            """;
        var result = SoftCpuRecompiler.Recompile(program, basePrg);
        Assert.True(result.RoutinesLowered >= 1, string.Join("; ", result.Diagnostics));
        // LDA $10; LSR; LSR; STA $10; RTS
        Assert.Equal(0xA5, result.PrgRom[0]);
        Assert.Equal(0x10, result.PrgRom[1]);
        Assert.Equal(0x4A, result.PrgRom[2]); // LSR A
        Assert.Equal(0x4A, result.PrgRom[3]);
        Assert.Equal(0x85, result.PrgRom[4]);
        Assert.Equal(0x10, result.PrgRom[5]);
    }

    [Fact]
    public void SoftCpuReadability_RaisesLoadIntoCompare()
    {
        var body = new List<string>
        {
            "a = game[0x4B];",
            "if (a != 0)",
            "{",
            "    return;",
            "}",
            "a = reset_07FF;",
            "if (a == 0xA5) { y = 0xD6; }",
            "a = x;",
            "slot = a;",
        };
        SoftCpuReadabilityPass.RaiseLoadIntoCompare(body);
        SoftCpuReadabilityPass.FoldPassThroughA(body);
        Assert.Contains(body, l => l.Contains("if (game[0x4B] != 0)"));
        Assert.Contains(body, l => l.Contains("if (reset_07FF == 0xA5)"));
        Assert.Contains(body, l => l == "slot = x;");
        Assert.DoesNotContain(body, l => l.TrimStart().StartsWith("a = game", StringComparison.Ordinal));
    }

    [Fact]
    public void SoftCpuReadability_RaisesNegFlagIfAndCondLitNzAndCarryStores()
    {
        var negStore = new List<string>
        {
            "a = obj_009F[x];",
            "if (negative) { y--; }",
            "ptr_06_hi = 0;",
            "buf_00CE[x] = a;",
        };
        SoftCpuReadabilityPass.RaiseLoadIntoNegFlagIf(negStore);
        SoftCpuReadabilityPass.FoldPassThroughA(negStore);
        Assert.Contains(negStore, l => l.Contains("if ((obj_009F[x] & 0x80) != 0)"));
        Assert.Contains(negStore, l => l == "buf_00CE[x] = obj_009F[x];");
        Assert.Contains(negStore, l => l.Contains("ptr_06_hi = 0;"));
        Assert.DoesNotContain(negStore, l => l.TrimStart().StartsWith("a =", StringComparison.Ordinal));

        var negDrop = new List<string>
        {
            "a = f_E87D_0363;",
            "if (negative) { x = 0xDE; }",
            "func_EA4B((byte)(zp_ED & 0x20)); return;",
        };
        SoftCpuReadabilityPass.RaiseLoadIntoNegFlagIf(negDrop);
        Assert.Contains(negDrop, l => l.Contains("if ((f_E87D_0363 & 0x80) != 0)"));
        Assert.DoesNotContain(negDrop, l => l.TrimStart().StartsWith("a =", StringComparison.Ordinal));

        var litNz = new List<string>
        {
            "a = 0xFF;",
            "if (!negative) { buf_04AC[y] = a; }",
            "buf_04AC[y + 2] = a;",
            "x = zp_08;",
            "return;",
        };
        SoftCpuReadabilityPass.RaiseCondLitStoreOnNzFlag(litNz);
        Assert.Contains(litNz, l => l.Contains("if (!negative) { buf_04AC[y] = 0xFF; }"));
        Assert.Contains(litNz, l => l == "buf_04AC[y + 2] = 0xFF;");
        Assert.DoesNotContain(litNz, l => l.TrimStart().StartsWith("a =", StringComparison.Ordinal));

        var carryStore = new List<string>
        {
            "a = vram_ptr_lo;",
            "if (carry)",
            "{",
            "    oam_tile[y + 4] = a;",
            "    oam_tile[y] = vram_ptr_hi;",
            "    oam_tile[y] = a;",
            "}",
            "a += 8;",
            "oam_x[y + 4] = a;",
        };
        SoftCpuReadabilityPass.RaiseLoadThroughCarryIfStores(carryStore);
        Assert.Contains(carryStore, l => l.Contains("oam_tile[y + 4] = vram_ptr_lo;"));
        Assert.Contains(carryStore, l => l.Contains("oam_tile[y] = vram_ptr_lo;"));
        Assert.Contains(carryStore, l => l == "a = vram_ptr_lo;"); // live for a += 8

        var lsrIdx = new List<string>
        {
            "a = (byte)(buf_006D_00 + (byte)(carry ? 1 : 0));",
            "carry = (a & 1) != 0; a >>= 1;",
            "x = a;",
            "func_00_BFD7(); return;",
        };
        SoftCpuReadabilityPass.RaiseLsrToDest(lsrIdx);
        Assert.Contains(lsrIdx, l => l.Contains("x = (byte)((byte)(buf_006D_00 + (byte)(carry ? 1 : 0)) >> 1)"));
        Assert.DoesNotContain(lsrIdx, l => l.TrimStart().StartsWith("a =", StringComparison.Ordinal));

        var adcExpr = new List<string>
        {
            "a = (byte)((byte)((byte)(zp_02 + 0x20) * 2) * 2);",
            "ptr_04_lo = (byte)(a + ptr_04_lo + (carry ? 1 : 0));",
            "a = (byte)(ptr_04_hi + (byte)(carry ? 1 : 0));",
        };
        SoftCpuReadabilityPass.RaiseAccIntoAdcExprDest(adcExpr);
        Assert.Contains(adcExpr, l => l.Contains("ptr_04_lo = (byte)((byte)((byte)((byte)(zp_02 + 0x20) * 2) * 2) + ptr_04_lo + (carry ? 1 : 0))"));
        Assert.Contains(adcExpr, l => l.StartsWith("a = (byte)(ptr_04_hi", StringComparison.Ordinal));

        var cmpCarry = new List<string>
        {
            "a = buf_0086_00;",
            "carry = true;",
            "if (a < 0x10) { zp_33 = 2; }",
            "y = zp_33;",
        };
        SoftCpuReadabilityPass.RaiseLoadIntoCompare(cmpCarry);
        Assert.Contains(cmpCarry, l => l.Contains("if (buf_0086_00 < 0x10)"));
        Assert.DoesNotContain(cmpCarry, l => l.TrimStart().StartsWith("a =", StringComparison.Ordinal));
        Assert.DoesNotContain(cmpCarry, l => l.Contains("carry = true"));

        var litAsl = new List<string>
        {
            "a = 1;",
            "y = obj_0046[0x11];",
            "if (y != 0)",
            "{",
            "    if (negative) { carry = (a & 0x80) != 0; a <<= 1; }",
            "    zp_45 = a;",
            "}",
            "func_00_AF93();",
        };
        SoftCpuReadabilityPass.RaiseCondLitAslOnIndexNeg(litAsl);
        Assert.Contains(litAsl, l => l.Contains("zp_45 = 1;"));
        Assert.Contains(litAsl, l => l.Contains("if ((y & 0x80) != 0) { zp_45 = 2; }"));
        Assert.DoesNotContain(litAsl, l => l.TrimStart().StartsWith("a =", StringComparison.Ordinal));
    }

    [Fact]
    public void SoftCpu_LowersMemNegFlagIf()
    {
        var basePrg = new byte[0x4000];
        Array.Fill(basePrg, (byte)0xEA);
        const string program = """
            byte flag; // $10
            // bank=0 $8000
            void reset()
            {
                if ((flag & 0x80) != 0) { y--; }
                return;
            }
            """;
        var result = SoftCpuRecompiler.Recompile(program, basePrg);
        Assert.True(result.RoutinesLowered >= 1, string.Join("; ", result.Diagnostics));
        // LDA $10; AND #$80; BEQ end; DEY; end
        Assert.Equal(0xA5, result.PrgRom[0]);
        Assert.Equal(0x10, result.PrgRom[1]);
        Assert.Equal(0x29, result.PrgRom[2]); // AND #
        Assert.Equal(0x80, result.PrgRom[3]);
    }

    [Fact]
    public void SoftCpu_LowersMemCompareIf()
    {
        var basePrg = new byte[0x4000];
        Array.Fill(basePrg, (byte)0xEA);
        const string program = """
            byte game_4B; // $4B
            // bank=0 $8000
            void reset()
            {
                if (game_4B != 0) return;
                return;
            }
            """;
        var result = SoftCpuRecompiler.Recompile(program, basePrg);
        Assert.True(result.RoutinesLowered >= 1, string.Join("; ", result.Diagnostics));
        // LDA $4B; BEQ skip; RTS; skip: RTS
        Assert.Equal(0xA5, result.PrgRom[0]);
        Assert.Equal(0x4B, result.PrgRom[1]);
        Assert.Equal(0xF0, result.PrgRom[2]); // BEQ
    }

    [Fact]
    public void SoftCpuReadability_RaisesPeekAndMaskAndOamClear()
    {
        var peekBody = new List<string>
        {
            "a = peek((ushort)(0xF90D + y));",
            "zp_F0 = a;",
            "a = game[0x7A];",
            "a &= 0x80;",
            "if (a != 0) return;",
            "a = game[0x23];",
            "a &= 7;",
            "if (a == 7) return;",
            "a = x;",
            "a += 7;",
            "x = a;",
            "a = pla();",
            "ptr_04_lo = a;",
            "a = ppu_ctrl_shadow;",
            "a &= (byte)~PPUCTRL.NMI;",
            "ppu_ctrl_shadow = a;",
        };
        SoftCpuReadabilityPass.RaiseAndMaskTest(peekBody);
        SoftCpuReadabilityPass.RaiseIndexAlu(peekBody);
        SoftCpuReadabilityPass.RaiseAccAluToDest(peekBody);
        SoftCpuReadabilityPass.FoldPassThroughA(peekBody);
        Assert.Contains(peekBody, l => l.Contains("zp_F0 = peek((ushort)(0xF90D + y))"));
        Assert.Contains(peekBody, l => l.Contains("if ((game[0x7A] & 0x80) != 0) return;"));
        Assert.Contains(peekBody, l => l.Contains("if ((game[0x23] & 7) == 7) return;"));
        Assert.Contains(peekBody, l => l == "x += 7;");
        Assert.Contains(peekBody, l => l.Contains("ptr_04_lo = pla()"));
        Assert.Contains(peekBody, l => l.Contains("ppu_ctrl_shadow = (byte)(ppu_ctrl_shadow & (byte)~PPUCTRL.NMI)"));

        var oam = new List<string>
        {
            "y = 0;",
            "a = 0xF8;",
            "do",
            "{",
            "oam_y[y] = a;",
            "y++;",
            "y++;",
            "y++;",
            "y++;",
            "}",
            "while (y != 0);",
        };
        SoftCpuReadabilityPass.StructureOamClearLoops(oam);
        Assert.Contains(oam, l => l.Contains("oam_y[y] = 0xF8"));
        Assert.DoesNotContain(oam, l => l.TrimStart().StartsWith("a =", StringComparison.Ordinal));
    }

    [Fact]
    public void SoftCpu_LowersMemPeekAssignAndBitTest()
    {
        var basePrg = new byte[0x4000];
        Array.Fill(basePrg, (byte)0xEA);
        // ROM table at $8000 in PRG image maps from assemble base
        const string program = """
            byte zp_F0; // $F0
            byte flag; // $10
            const ushort tab = 0x9000;
            // bank=0 $8000
            void reset()
            {
                zp_F0 = peek((ushort)(tab + y));
                if ((flag & 0x80) != 0) return;
                return;
            }
            """;
        var result = SoftCpuRecompiler.Recompile(program, basePrg);
        Assert.True(result.RoutinesLowered >= 1, string.Join("; ", result.Diagnostics));
        // LDA abs,Y / STA zp_F0 / LDA flag / AND #$80 / BEQ / RTS
        Assert.Equal(0xB9, result.PrgRom[0]); // LDA abs,Y
    }

    [Fact]
    public void SoftCpu_LowersRaisedMemAluExpr()
    {
        var basePrg = new byte[0x4000];
        Array.Fill(basePrg, (byte)0xEA);
        const string program = """
            byte lo; // $10
            byte dest_lo; // $12
            // bank=0 $8000
            void reset()
            {
                dest_lo = (byte)((byte)(lo + 8) & 0xF0);
                return;
            }
            """;
        var result = SoftCpuRecompiler.Recompile(program, basePrg);
        Assert.True(result.RoutinesLowered >= 1, string.Join("; ", result.Diagnostics));
        // LDA $10; CLC; ADC #8; AND #$F0; STA $12
        Assert.Equal(0xA5, result.PrgRom[0]);
        Assert.Equal(0x10, result.PrgRom[1]);
        Assert.Equal(0x18, result.PrgRom[2]);
        Assert.Equal(0x69, result.PrgRom[3]);
        Assert.Equal(0x08, result.PrgRom[4]);
        Assert.Equal(0x29, result.PrgRom[5]);
        Assert.Equal(0xF0, result.PrgRom[6]);
        Assert.Equal(0x85, result.PrgRom[7]);
        Assert.Equal(0x12, result.PrgRom[8]);
    }

    [Fact]
    public void SoftCpu_LowersAdcCarryBit()
    {
        var basePrg = new byte[0x4000];
        Array.Fill(basePrg, (byte)0xEA);
        const string program = """
            // bank=0 $8000
            void reset()
            {
                a += 8;
                a += (byte)(carry ? 1 : 0);
                return;
            }
            """;
        var result = SoftCpuRecompiler.Recompile(program, basePrg);
        Assert.Empty(result.Diagnostics);
        // CLC ADC #8 ; ADC #0
        Assert.Equal(0x18, result.PrgRom[0]);
        Assert.Equal(0x69, result.PrgRom[1]);
        Assert.Equal(0x08, result.PrgRom[2]);
        Assert.Equal(0x69, result.PrgRom[3]);
        Assert.Equal(0x00, result.PrgRom[4]);
    }

    [Fact]
    public void SoftCpu_LowersMemAdc0()
    {
        var basePrg = new byte[0x4000];
        Array.Fill(basePrg, (byte)0xEA);
        const string program = """
            byte hi; // $11
            byte dest; // $13
            // bank=0 $8000
            void reset()
            {
                hi += (byte)(carry ? 1 : 0);
                dest = (byte)(hi + (carry ? 1 : 0));
                return;
            }
            """;
        var result = SoftCpuRecompiler.Recompile(program, basePrg);
        Assert.True(result.RoutinesLowered >= 1, string.Join("; ", result.Diagnostics));
        // LDA $11; ADC #0; STA $11; LDA $11; ADC #0; STA $13
        Assert.Equal(0xA5, result.PrgRom[0]);
        Assert.Equal(0x11, result.PrgRom[1]);
        Assert.Equal(0x69, result.PrgRom[2]);
        Assert.Equal(0x00, result.PrgRom[3]);
        Assert.Equal(0x85, result.PrgRom[4]);
        Assert.Equal(0x11, result.PrgRom[5]);
        Assert.Equal(0xA5, result.PrgRom[6]);
        Assert.Equal(0x11, result.PrgRom[7]);
        Assert.Equal(0x69, result.PrgRom[8]);
        Assert.Equal(0x00, result.PrgRom[9]);
        Assert.Equal(0x85, result.PrgRom[10]);
        Assert.Equal(0x13, result.PrgRom[11]);
    }

    [Fact]
    public void SoftCpuReadability_SugarsAdd16AndMul()
    {
        var addBody = new List<string>
        {
            "a = lo;",
            "a += 8;",
            "a &= 0xF0;",
            "dest_lo = a;",
            "a = hi;",
            SoftCpuReadabilityPass.Adc0Sugar,
            "dest_hi = a;",
            "dest_hi2 = a;",
        };
        SoftCpuReadabilityPass.SugarAdd16ThroughA(addBody);
        Assert.Contains(addBody, l => l.Contains("ushort _u") && l.Contains("lo | (hi << 8)"));
        Assert.Contains(addBody, l => l.Contains("+= 8"));
        Assert.Contains(addBody, l => l.Contains("dest_lo = (byte)(_u") && l.Contains("& 0xF0)"));
        Assert.Contains(addBody, l => l.Contains("dest_hi = (byte)(_u") && l.Contains(">> 8)"));
        Assert.Contains(addBody, l => l.Contains("dest_hi2 = (byte)(_u") && l.Contains(">> 8)"));

        var mul3 = new List<string>
        {
            "t = a;",
            "a <<= 1;",
            "carry = false;",
            "a += t;",
        };
        SoftCpuReadabilityPass.SugarConstMul35(mul3);
        Assert.Equal(new List<string> { "a *= 3;" }, mul3);

        var mul3Live = new List<string>
        {
            "t = a;",
            "carry = (a & 0x80) != 0; a <<= 1;",
            "a = (byte)(a + t + (carry ? 1 : 0));",
        };
        SoftCpuReadabilityPass.SugarConstMul35(mul3Live);
        Assert.Equal(new List<string> { "a *= 3;" }, mul3Live);

        var mul5 = new List<string>
        {
            "t = a;",
            "a <<= 2;",
            "carry = false;",
            "a += t;",
        };
        SoftCpuReadabilityPass.SugarConstMul35(mul5);
        Assert.Equal(new List<string> { "a *= 5;" }, mul5);

        var pow2 = new List<string> { "a <<= 3;" };
        SoftCpuReadabilityPass.SugarPow2Mul(pow2);
        Assert.Equal(new List<string> { "a *= 8;" }, pow2);
    }

    [Fact]
    public void SoftCpu_LowersAdd16UshortAndMul()
    {
        var basePrg = new byte[0x4000];
        Array.Fill(basePrg, (byte)0xEA);
        const string program = """
            byte lo; // $10
            byte hi; // $11
            byte dest_lo; // $12
            byte dest_hi; // $13
            // bank=0 $8000
            void reset()
            {
                ushort _u0 = (ushort)(lo | (hi << 8));
                _u0 += 8;
                dest_lo = (byte)(_u0 & 0xF0);
                dest_hi = (byte)(_u0 >> 8);
                a *= 4;
                a *= 3;
                return;
            }
            """;
        var result = SoftCpuRecompiler.Recompile(program, basePrg);
        Assert.True(result.RoutinesLowered >= 1, string.Join("; ", result.Diagnostics));
        // LDA $10; CLC; ADC #8; AND #$F0; STA $12; LDA $11; ADC #0; STA $13
        Assert.Equal(0xA5, result.PrgRom[0]); // LDA zp
        Assert.Equal(0x10, result.PrgRom[1]);
        Assert.Equal(0x18, result.PrgRom[2]); // CLC
        Assert.Equal(0x69, result.PrgRom[3]); // ADC #
        Assert.Equal(0x08, result.PrgRom[4]);
        Assert.Equal(0x29, result.PrgRom[5]); // AND #
        Assert.Equal(0xF0, result.PrgRom[6]);
        Assert.Equal(0x85, result.PrgRom[7]); // STA zp
        Assert.Equal(0x12, result.PrgRom[8]);
        Assert.Equal(0xA5, result.PrgRom[9]);
        Assert.Equal(0x11, result.PrgRom[10]);
        Assert.Equal(0x69, result.PrgRom[11]); // ADC #0
        Assert.Equal(0x00, result.PrgRom[12]);
        Assert.Equal(0x85, result.PrgRom[13]);
        Assert.Equal(0x13, result.PrgRom[14]);
        // a *= 4 → ASL ASL
        Assert.Equal(0x0A, result.PrgRom[15]);
        Assert.Equal(0x0A, result.PrgRom[16]);
        // a *= 3 → PHA ASL CLC TSX ADC $0101,X TAY PLA TYA
        Assert.Equal(0x48, result.PrgRom[17]); // PHA
        Assert.Equal(0x0A, result.PrgRom[18]); // ASL
        Assert.Equal(0x18, result.PrgRom[19]); // CLC
        Assert.Equal(0xBA, result.PrgRom[20]); // TSX
        Assert.Equal(0x7D, result.PrgRom[21]); // ADC abs,X
        Assert.Equal(0x01, result.PrgRom[22]);
        Assert.Equal(0x01, result.PrgRom[23]);
        Assert.Equal(0xA8, result.PrgRom[24]); // TAY
        Assert.Equal(0x68, result.PrgRom[25]); // PLA
        Assert.Equal(0x98, result.PrgRom[26]); // TYA
    }

    [Fact]
    public void SoftCpuReadability_EnsureReferencedRestoresCoveredScalars()
    {
        var bodies = new List<List<string>>
        {
            new()
            {
                "poke(f_C0CC_06D9, 0);",
                "a = peek(ptr_E9_lo);",
                "a = zp_EB;",
            },
        };
        var ram = new Dictionary<ushort, string>();
        var consts = new Dictionary<ushort, string>();
        var buffers = new List<BufferRecovery.RamBuffer>
        {
            new(0x06BE, 54, "buf_06BE"),
            new(0x00E4, 16, "obj_00E4"),
        };
        SoftCpuReadabilityPass.EnsureReferencedRamDecls(bodies, ram, consts, buffers);
        Assert.Equal("f_C0CC_06D9", ram[0x06D9]);
        Assert.Equal("ptr_E9_lo", ram[0xE9]);
        Assert.Equal("zp_EB", ram[0xEB]);
    }

    [Fact]
    public void SoftCpu_AllowsInSpanModification()
    {
        // Original: LDA #$10 / STA $2000 at $8002. Edit Program.cs imm to $11.
        var basePrg = new byte[0x4000];
        // SEI CLD LDA #$10 STA $2000 LDX #$FF TXS JMP self
        byte[] orig =
        [
            0x78, 0xD8, 0xA9, 0x10, 0x8D, 0x00, 0x20, 0xA2, 0xFF, 0x9A, 0x4C, 0x0A, 0x80,
        ];
        Array.Copy(orig, basePrg, orig.Length);

        const string program = """
            // bank=0 $8000
            void reset()
            {
                sei();
                cld();
                a = 0x11;
                poke(PPU_CTRL, a);
                x = 0xFF;
                txs();
                while (true) ;
            }
            """;

        var result = SoftCpuRecompiler.Recompile(program, basePrg);
        Assert.Equal(1, result.RoutinesLowered);
        Assert.Equal(0xA9, result.PrgRom[2]); // LDA #
        Assert.Equal(0x11, result.PrgRom[3]); // modified immediate (was $10)
        Assert.NotEqual(orig[3], result.PrgRom[3]);
    }

    [Fact]
    public void SoftCpu_DoesNotNopPadOverRealOpcodes_WhenLowerIsShort()
    {
        // Original DEC abs / RTS. Incomplete Program.cs is only return; — must keep DEC.
        var basePrg = new byte[0x4000];
        basePrg[0] = 0xDE; // DEC abs
        basePrg[1] = 0xEC;
        basePrg[2] = 0x03;
        basePrg[3] = 0x60; // RTS

        const string program = """
            // bank=0 $8000
            void reset()
            {
                return;
            }
            """;

        var result = SoftCpuRecompiler.Recompile(program, basePrg);
        Assert.Equal(0, result.RoutinesLowered);
        Assert.Equal(0xDE, result.PrgRom[0]);
        Assert.Equal(0xEC, result.PrgRom[1]);
        Assert.Equal(0x03, result.PrgRom[2]);
        Assert.Equal(0x60, result.PrgRom[3]);
        Assert.Contains(result.Diagnostics, d => d.Contains("keep gap-bridge", StringComparison.Ordinal));
    }

    [Fact]
    public void SoftCpu_LowersVramWriteAndFill()
    {
        var basePrg = new byte[0x4000];
        Array.Fill(basePrg, (byte)0xEA);

        const string program = """
            // bank=0 $8000
            void reset()
            {
                vram_adr(NTADR_A(2, 2));
                vram_write("HI");
                vram_fill(0x24, 4);
                return;
            }
            """;

        var result = SoftCpuRecompiler.Recompile(program, basePrg);
        Assert.Equal(1, result.RoutinesLowered);
        Assert.Empty(result.Diagnostics);
        byte[] head = result.PrgRom.AsSpan(0, 48).ToArray();
        // NTADR_A(2,2) = $2042 → LDA #$20 / STA $2006 …
        Assert.Equal(0xA9, head[0]);
        Assert.Equal(0x20, head[1]);
        Assert.Contains((byte)0x8D, head); // STA abs
        Assert.Contains((byte)0x06, head); // $2006
        // vram_write("HI") and fill use $2007
        Assert.Contains((byte)0x07, head);
        Assert.Contains((byte)0x48, head); // 'H'
        Assert.Contains((byte)0x49, head); // 'I'
        Assert.Contains((byte)0x24, head); // fill tile
    }

    [Fact]
    public void SoftCpu_LowersCompoundAlu_AndIfElse()
    {
        var basePrg = new byte[0x4000];
        Array.Fill(basePrg, (byte)0xEA);

        const string program = """
            // bank=0 $8000
            void reset()
            {
                a &= 0x7F;
                a |= 0x80;
                if (zero)
                {
                    a ^= 0x01;
                }
                else
                {
                    a &= 0xFE;
                }
                return;
            }
            """;

        var result = SoftCpuRecompiler.Recompile(program, basePrg);
        Assert.Equal(1, result.RoutinesLowered);
        Assert.Empty(result.Diagnostics);
        // AND #$7F / ORA #$80
        Assert.Equal(0x29, result.PrgRom[0]);
        Assert.Equal(0x7F, result.PrgRom[1]);
        Assert.Equal(0x09, result.PrgRom[2]);
        Assert.Equal(0x80, result.PrgRom[3]);
        // BNE else (if zero → fall into then)
        Assert.Equal(0xD0, result.PrgRom[4]);
    }

    [Fact]
    public void SoftCpu_LowersPadPollMsb_WithPadMsbMasks()
    {
        // SMB ReadJoypad body shape at $8000 on a blank canvas.
        var basePrg = new byte[0x4000];
        for (int i = 0; i < basePrg.Length; i++)
            basePrg[i] = 0xEA;

        const string program = """
            const ushort pad_06FC = 0x06FC;
            const ushort pad_074A = 0x074A;
            // bank=0 $8000
            void reset()
            {
                a = (byte)pad_poll_msb(x);
                poke((ushort)(pad_06FC + x), a);
                pha();
                a = (byte)(a & (byte)(PADMSB.START | PADMSB.SELECT));
                a = (byte)(a & peek((ushort)(pad_074A + x)));
                if (zero) goto L_8030;
                a = pla();
                a = (byte)(a & (byte)~(PADMSB.START | PADMSB.SELECT));
                poke((ushort)(pad_06FC + x), a);
                return;
                L_8030: ;
                a = pla();
                poke((ushort)(pad_074A + x), a);
                return;
            }
            """;

        var result = SoftCpuRecompiler.Recompile(program, basePrg);
        Assert.Equal(1, result.RoutinesLowered);
        Assert.Empty(result.Diagnostics);
        // LDY #$08 / PHA / LDA $4016,X
        Assert.Equal(0xA0, result.PrgRom[0]);
        Assert.Equal(0x08, result.PrgRom[1]);
        Assert.Equal(0x48, result.PrgRom[2]);
        Assert.Equal(0xBD, result.PrgRom[3]);
        Assert.Equal(0x16, result.PrgRom[4]);
        Assert.Equal(0x40, result.PrgRom[5]);
        // AND #$30 (START|SELECT in PADMSB)
        Assert.Contains((byte)0x30, result.PrgRom.Take(64));
        // AND #$CF (~$30)
        Assert.Contains((byte)0xCF, result.PrgRom.Take(64));
    }

    [Fact]
    public void SoftCpu_PaletteColorEdit_DoesNotSmashCode()
    {
        var basePrg = new byte[0x4000];
        // Tiny reset + palette at $8100
        byte[] reset = [0x78, 0xD8, 0x4C, 0x00, 0x80]; // SEI CLD JMP $8000
        Array.Copy(reset, basePrg, reset.Length);
        int palOff = 0x100;
        byte[] pal = [0x3F, 0x00, 0x04, 0x0F, 0x22, 0x30, 0x10]; // LightBlue at +4
        Array.Copy(pal, 0, basePrg, palOff, pal.Length);

        const string program = """
            (ushort Base, byte[] Data)[] palette =
            {
                (0x8100, new byte[] { 0x3F, 0x00, 0x04, NESColor.Black, NESColor.Red, NESColor.White, NESColor.Gray })
            };
            // bank=0 $8000
            void reset()
            {
                sei();
                // CLD
                while (true) ;
            }
            """;

        var result = SoftCpuRecompiler.Recompile(program, basePrg);
        Assert.Equal(0x16, result.PrgRom[palOff + 4]); // Red replaced LightBlue
        Assert.Equal(0x78, result.PrgRom[0]); // SEI kept / lowered
        Assert.Equal(0xD8, result.PrgRom[1]); // CLD
        // Palette header/length untouched
        Assert.Equal(0x3F, result.PrgRom[palOff]);
        Assert.Equal(0x04, result.PrgRom[palOff + 2]);
    }

    [Fact]
    public void SoftCpu_LowersPaletteHelpers_AndWritesPaletteData()
    {
        // Canvas: pointer-table load + LDA ($00),Y + absolute palette peek, then RTS.
        // Palette record parked at $8100; lo/hi tables at $805A/$806D.
        var basePrg = new byte[0x4000];
        for (int i = 0; i < basePrg.Length; i++)
            basePrg[i] = 0xEA;

        // Seed original palette (will be overwritten by Program.cs edit → Rose).
        int palOff = 0x100; // $8100
        byte[] origPal = [0x3F, 0x00, 0x04, 0x0F, 0x16, 0x27, 0x30];
        Array.Copy(origPal, 0, basePrg, palOff, origPal.Length);

        const string program = """
            byte vram_ptr_lo; // $00
            byte vram_ptr_hi; // $01
            (ushort Base, byte[] Data)[] palette =
            {
                (0x8100, new byte[] { 0x3F, 0x00, 0x04, NESColor.Black, NESColor.Rose, NESColor.Blue, NESColor.White })
            };
            // VRAM buffer pointers (lo$805A/hi$806D) — index selects RAM buffer or palette.Base.
            ushort[] vramBufPtr = { 0x0300, 0x8100 };

            // bank=0 $8000
            void reset()
            {
                {
                    ushort _p = vramBufPtr[x];
                    vram_ptr_lo = (byte)_p;
                    vram_ptr_hi = (byte)(_p >> 8);
                }
                BindVramFromZp();
                a = PeekVramBufY();
                a = palette[0].Data[3];
                ppu_ctrl((PPUCTRL)a);
                vram_put(a);
                vram_adr(0x3F00);
                ppu_scroll(a, a);
                x = ppu_status();
                return;
            }
            """;

        var result = SoftCpuRecompiler.Recompile(program, basePrg);
        Assert.Equal(1, result.RoutinesLowered);
        Assert.Empty(result.Diagnostics);

        // Palette Program.cs edit applied over seeded PRG bytes.
        Assert.Equal(0x0F, result.PrgRom[palOff + 3]); // Black
        Assert.Equal(0x15, result.PrgRom[palOff + 4]); // Rose
        Assert.Equal(0x12, result.PrgRom[palOff + 5]); // Blue (was 0x27)
        Assert.Equal(0x30, result.PrgRom[palOff + 6]); // White

        // vramBufPtr entry 1 = $8100 written to lo/hi tables
        Assert.Equal(0x00, result.PrgRom[0x5A + 1]); // lo of $8100
        Assert.Equal(0x81, result.PrgRom[0x6D + 1]); // hi

        // reset starts with LDA $805A,X / STA $00 / LDA $806D,X / STA $01 / LDA ($00),Y
        Assert.Equal(0xBD, result.PrgRom[0]); // LDA abs,X
        Assert.Equal(0x5A, result.PrgRom[1]);
        Assert.Equal(0x80, result.PrgRom[2]);
        Assert.Equal(0x85, result.PrgRom[3]); // STA zp
        Assert.Equal(0x00, result.PrgRom[4]);
        Assert.Equal(0xBD, result.PrgRom[5]); // LDA abs,X
        Assert.Equal(0x6D, result.PrgRom[6]);
        Assert.Equal(0x80, result.PrgRom[7]);
        Assert.Equal(0x85, result.PrgRom[8]); // STA zp
        Assert.Equal(0x01, result.PrgRom[9]);
        Assert.Equal(0xB1, result.PrgRom[10]); // LDA ($00),Y
        Assert.Equal(0x00, result.PrgRom[11]);
    }

    static string? FindSmbDecompDir()
    {
        string[] candidates =
        [
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "nes-recomp", "Super Mario Bros. (World)")),
            "/home/chuck/Desktop/projects/nes-recomp/Super Mario Bros. (World)",
        ];
        foreach (string c in candidates)
        {
            if (File.Exists(Path.Combine(c, "Program.cs")) && File.Exists(Path.Combine(c, "prg.bin")))
                return c;
        }
        return null;
    }
}
