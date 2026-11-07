using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using dotnes.ObjectModel;

namespace dotnes;

/// <summary>
/// Lowers soft-CPU <c>Program.cs</c> (GeneralRecoverer shape) onto real 6502 and
/// overlays routines onto a PRG image. Unlowerable / oversized routines keep the
/// gap-bridge bytes from <c>prg.bin</c>.
/// </summary>
static class SoftCpuRecompiler
{
    static readonly Regex RoutineHeader = new(
        @"^//\s*bank=(\d+)\s+\$([0-9A-Fa-f]{4})\b",
        RegexOptions.Compiled);

    static readonly Regex ConstUshort = new(
        @"^const\s+ushort\s+(\w+)\s*=\s*0x([0-9A-Fa-f]+)\s*;",
        RegexOptions.Compiled);

    /// <summary>Hot RAM: <c>byte zp_FE = 0; // $FE</c> or <c>byte ram_07D7; // $07D7</c>.</summary>
    static readonly Regex ByteRamVar = new(
        @"^byte\s+(\w+)\s*(?:=\s*0\s*)?;\s*(?://\s*\$([0-9A-Fa-f]+))?",
        RegexOptions.Compiled);

    /// <summary>Coherent buffer: <c>byte[] oam = new byte[256]; // $0200</c>.</summary>
    static readonly Regex ByteArrayDecl = new(
        @"^byte\[\]\s+(\w+)\s*=\s*new\s+byte\[\d+\]\s*;\s*(?://\s*\$([0-9A-Fa-f]+))?",
        RegexOptions.Compiled);

    /// <summary><c>a = oam[y];</c> / <c>a = vram_buf[x + 1];</c></summary>
    static readonly Regex BufIndexLoad = new(
        @"^(a|x|y)\s*=\s*([A-Za-z_]\w*)\[(?:(x|y)(?:\s*\+\s*(0x[0-9A-Fa-f]+|\d+))?|(0x[0-9A-Fa-f]+|\d+))\]\s*;\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary><c>oam[y] = a;</c> / <c>vram_buf[x + 1] = 0x00;</c></summary>
    static readonly Regex BufIndexStore = new(
        @"^([A-Za-z_]\w*)\[(?:(x|y)(?:\s*\+\s*(0x[0-9A-Fa-f]+|\d+))?|(0x[0-9A-Fa-f]+|\d+))\]\s*=\s*(.+?)\s*;\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary><c>vram_buf[x]++;</c> / <c>oam_y[y]--;</c></summary>
    static readonly Regex BufIndexIncDec = new(
        @"^([A-Za-z_]\w*)\[(?:(x|y)(?:\s*\+\s*(0x[0-9A-Fa-f]+|\d+))?|(0x[0-9A-Fa-f]+|\d+))\]\s*(\+\+|--)\s*;\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary><c>a += 8;</c> after carry collapsed (CLC + ADC #imm).</summary>
    static readonly Regex AdcAssignAdd = new(
        @"^a\s*\+=\s*(0x[0-9A-Fa-f]+|\d+)\s*;\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary><c>a += (byte)(carry ? 1 : 0);</c> → ADC #0 (hi byte of 16-bit add).</summary>
    static readonly Regex AdcCarryBit = new(
        @"^a\s*\+=\s*(?:\(byte\))?\(?\s*carry\s*\?\s*1\s*:\s*0\s*\)?\s*;\s*$",
        RegexOptions.Compiled);

    /// <summary><c>dest += (byte)(carry ? 1 : 0);</c> → LDA dest; ADC #0; STA dest.</summary>
    static readonly Regex MemAdcCarryBit = new(
        @"^([A-Za-z_]\w*(?:\[[^\]]+\])?)\s*\+=\s*(?:\(byte\))?\(?\s*carry\s*\?\s*1\s*:\s*0\s*\)?\s*;\s*$",
        RegexOptions.Compiled);

    /// <summary><c>dest = (byte)(src + (carry ? 1 : 0));</c> → LDA src; ADC #0; STA dest.</summary>
    static readonly Regex MemAdcCarryFrom = new(
        @"^([A-Za-z_]\w*(?:\[[^\]]+\])?)\s*=\s*\(byte\)\(([A-Za-z_]\w*(?:\[[^\]]+\])?)\s*\+\s*\(carry\s*\?\s*1\s*:\s*0\)\)\s*;\s*$",
        RegexOptions.Compiled);

    /// <summary><c>a = vram_ptr[y];</c> / <c>vram_ptr[y] = a;</c> → (zp),Y via $00/$01.</summary>
    static readonly Regex VramPtrIndexLoad = new(
        @"^(a|x|y)\s*=\s*vram_ptr\[(x|y)\]\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex VramPtrIndexStore = new(
        @"^vram_ptr\[(x|y)\]\s*=\s*(.+?)\s*;\s*$",
        RegexOptions.Compiled);

    /// <summary><c>a = zp_FE;</c> — load from promoted RAM variable.</summary>
    static readonly Regex MemVarLoad = new(
        @"^(a|x|y)\s*=\s*([A-Za-z_]\w*)\s*;\s*$",
        RegexOptions.Compiled);

    /// <summary><c>zp_FE = a;</c> / <c>zp_FE = 0x12;</c> — store to promoted RAM variable.</summary>
    static readonly Regex MemVarStore = new(
        @"^([A-Za-z_]\w*)\s*=\s*(.+?)\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex MemVarInc = new(
        @"^(\w+)\s*=\s*\(byte\)\(\1\s*\+\s*1\)\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex MemVarDec = new(
        @"^(\w+)\s*=\s*\(byte\)\(\1\s*-\s*1\)\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex AndMemVar = new(
        @"^a\s*=\s*\(byte\)\(a\s*&\s*(\w+)\)\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex AdcMemVar = new(
        @"^a\s*=\s*\(byte\)\(a\s*\+\s*(\w+)\s*\+\s*\(carry\s*\?\s*1\s*:\s*0\)\)\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex LabelLine = new(
        @"^(L_[0-9A-Fa-f]+)\s*:\s*;?\s*$",
        RegexOptions.Compiled);

    static readonly Regex ImmAssign = new(
        @"^(a|x|y)\s*=\s*(0x[0-9A-Fa-f]+|\d+)\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex RegTransfer = new(
        @"^(a|x|y)\s*=\s*(a|x|y)\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex RegIncDec = new(
        @"^(a|x|y)(\+\+|--)\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex Goto = new(
        @"^goto\s+(L_[0-9A-Fa-f]+)\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex IfGoto = new(
        @"^if\s*\(\s*(!)?\s*(carry|zero|negative)\s*\)\s*goto\s+(L_[0-9A-Fa-f]+)\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex IfFlagReturn = new(
        @"^if\s*\(\s*(!)?\s*(carry|zero|negative)\s*\)\s*return\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex IfRegCompareGoto = new(
        @"^if\s*\(\s*(a|x|y|[A-Za-z_]\w*(?:\[[^\]]+\])?)\s*(==|!=|>=|<)\s*(0x[0-9A-Fa-f]+|\d+)\s*\)\s*goto\s+(L_[0-9A-Fa-f]+)\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex IfRegCompareReturn = new(
        @"^if\s*\(\s*(a|x|y|[A-Za-z_]\w*(?:\[[^\]]+\])?)\s*(==|!=|>=|<)\s*(0x[0-9A-Fa-f]+|\d+)\s*\)\s*return\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex IfRegMemCompareGoto = new(
        @"^if\s*\(\s*(a|x|y)\s*(==|!=|>=|<)\s*([A-Za-z_]\w*|peek\([^)]+\))\s*\)\s*goto\s+(L_[0-9A-Fa-f]+)\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex IfRegMemCompareReturn = new(
        @"^if\s*\(\s*(a|x|y)\s*(==|!=|>=|<)\s*([A-Za-z_]\w*|peek\([^)]+\))\s*\)\s*return\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex TransferInc = new(
        @"^(x|y)\s*=\s*\(byte\)\(a\s*([+-])\s*1\)\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex MemVarIncDecOp = new(
        @"^([A-Za-z_]\w*)(\+\+|--)\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex IfFlagBlockReturn = new(
        @"^if\s*\(\s*(!)?\s*(carry|zero|negative)\s*\)\s*\{\s*(.+)\s*\}\s*$",
        RegexOptions.Compiled);

    static readonly Regex CallReturn = new(
        @"^(\w+)\s*\(\s*\)\s*;\s*return\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex CallOnly = new(
        @"^(\w+)\s*\(\s*\)\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex PeekAssign = new(
        @"^(a|x|y)\s*=\s*peek\((.+)\)\s*;\s*$",
        RegexOptions.Compiled);

    /// <summary><c>dest = peek(addr);</c> raised SoftCpu surface (no live a).</summary>
    static readonly Regex MemPeekAssign = new(
        @"^([A-Za-z_]\w*(?:\[[^\]]+\])?)\s*=\s*peek\((.+)\)\s*;\s*$",
        RegexOptions.Compiled);

    /// <summary><c>if ((src &amp; mask) ==|!= 0)</c> raised SoftCpu surface.</summary>
    static readonly Regex IfMemBitTest = new(
        @"^if\s*\(\s*\(\s*(?<src>[A-Za-z_]\w*(?:\[[^\]]+\])?|x|y)\s*&\s*(?<m>.+?)\s*\)\s*(?<op>==|!=)\s*0\s*\)(?<rest>\s*.*)$",
        RegexOptions.Compiled);

    static readonly Regex PokeCall = new(
        @"^poke\((.+),\s*(.+)\)\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex AluImm = new(
        @"^a\s*=\s*\(byte\)\(a\s*([|&^])\s*(0x[0-9A-Fa-f]+|\d+)\)\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex AluCompoundImm = new(
        @"^a\s*([|&^])=\s*(0x[0-9A-Fa-f]+|\d+)\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex AluCompoundEnum = new(
        @"^a\s*([|&^])=\s*\(byte\)(~?\([^;]+\)|~?[A-Za-z_][\w.]*)\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex IfFlagBlock = new(
        @"^if\s*\(\s*(!)?\s*(carry|zero|negative)\s*\)\s*$",
        RegexOptions.Compiled);

    static readonly Regex AdcImm = new(
        @"^a\s*=\s*\(byte\)\(a\s*\+\s*(0x[0-9A-Fa-f]+|\d+)\s*\+\s*\(carry\s*\?\s*1\s*:\s*0\)\)\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex SwitchHeader = new(
        @"^switch\s*\(\s*(?<e>a|x|y|[A-Za-z_]\w*(?:\[[^\]]+\])?)\s*\)\s*$",
        RegexOptions.Compiled);

    static readonly Regex CmpFlagsA = new(
        @"^zero\s*=\s*a\s*==\s*(0x[0-9A-Fa-f]+|\d+)\s*;\s*carry\s*=\s*a\s*>=\s*\1\s*;\s*negative\s*=\s*\(byte\)\(a\s*-\s*\1\)\s*>=\s*0x80\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex CmpFlagsX = new(
        @"^zero\s*=\s*x\s*==\s*(0x[0-9A-Fa-f]+|\d+)\s*;\s*carry\s*=\s*x\s*>=\s*\1\s*;(?:\s*negative\s*=\s*\(byte\)\(x\s*-\s*\1\)\s*>=\s*0x80\s*;)?\s*$",
        RegexOptions.Compiled);

    static readonly Regex CmpFlagsY = new(
        @"^zero\s*=\s*y\s*==\s*(0x[0-9A-Fa-f]+|\d+)\s*;\s*carry\s*=\s*y\s*>=\s*\1\s*;(?:\s*negative\s*=\s*\(byte\)\(y\s*-\s*\1\)\s*>=\s*0x80\s*;)?\s*$",
        RegexOptions.Compiled);

    static readonly Regex IncPeekPoke = new(
        @"^poke\((.+),\s*\(byte\)\(peek\(\1\)\s*\+\s*1\)\)\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex PpuCtrl = new(
        @"^ppu_ctrl\((.+)\)\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex PpuMask = new(
        @"^ppu_mask_write\((.+)\)\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex ApuStatus = new(
        @"^apu_status_write\((.+)\)\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex ApuFrame = new(
        @"^apu_frame\((.+)\)\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex ApuStatusRead = new(
        @"^(a|x|y)\s*=\s*apu_status\s*\(\s*\)\s*;\s*$",
        RegexOptions.Compiled);

    /// <summary>Runtime APU channel register writers: apu_pulse1_ctrl(expr); etc.</summary>
    static readonly Regex ApuRegWrite = new(
        @"^apu_(pulse[12]_(?:ctrl|sweep|timer_lo|timer_hi)|triangle_(?:ctrl|timer_lo|timer_hi)|noise_(?:ctrl|period|length)|dmc_(?:freq|raw|start|len))\((.+)\)\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Dictionary<string, ushort> ApuRegWriteAddrs = new(StringComparer.Ordinal)
    {
        ["pulse1_ctrl"] = 0x4000,
        ["pulse1_sweep"] = 0x4001,
        ["pulse1_timer_lo"] = 0x4002,
        ["pulse1_timer_hi"] = 0x4003,
        ["pulse2_ctrl"] = 0x4004,
        ["pulse2_sweep"] = 0x4005,
        ["pulse2_timer_lo"] = 0x4006,
        ["pulse2_timer_hi"] = 0x4007,
        ["triangle_ctrl"] = 0x4008,
        ["triangle_timer_lo"] = 0x400A,
        ["triangle_timer_hi"] = 0x400B,
        ["noise_ctrl"] = 0x400C,
        ["noise_period"] = 0x400E,
        ["noise_length"] = 0x400F,
        ["dmc_freq"] = 0x4010,
        ["dmc_raw"] = 0x4011,
        ["dmc_start"] = 0x4012,
        ["dmc_len"] = 0x4013,
    };

    static readonly Regex PeekVramAssign = new(
        @"^(a|x|y)\s*=\s*PeekVramBufY\s*\(\s*\)\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex PaletteDataAssign = new(
        @"^(a|x|y)\s*=\s*palette\[(\d+)\]\.Data\[(\d+|0x[0-9A-Fa-f]+|x|y)\]\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex VramPut = new(
        @"^vram_put\((.+)\)\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex VramAdr = new(
        @"^vram_adr\((.+)\)\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex VramFill = new(
        @"^vram_fill\((.+),\s*(.+)\)\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex VramWriteString = new(
        @"^vram_write\(""(.*)""\)\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex VramWriteBytes = new(
        @"^vram_write\(new\s+byte\[\]\s*\{\s*(.+?)\s*\}\)\s*;",
        RegexOptions.Compiled);

    static readonly Regex Ntadr = new(
        @"^NTADR_([A-D])\(\s*(0x[0-9A-Fa-f]+|\d+)\s*,\s*(0x[0-9A-Fa-f]+|\d+)\s*\)$",
        RegexOptions.Compiled);

    static readonly Regex PpuScroll = new(
        @"^ppu_scroll\((.+),\s*(.+)\)\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex OamDma = new(
        @"^oam_dma\((.+)\)\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex PpuStatusAssign = new(
        @"^(a|x|y)\s*=\s*ppu_status\s*\(\s*\)\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex AslA = new(
        @"^carry\s*=\s*\(a\s*&\s*0x80\)\s*!=\s*0\s*;\s*a\s*<<=\s*1\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex LsrA = new(
        @"^carry\s*=\s*\(a\s*&\s*1\)\s*!=\s*0\s*;\s*a\s*>>=\s*1\s*;\s*$",
        RegexOptions.Compiled);

    /// <summary>Collapsed multi-shift: <c>a &lt;&lt;= n;</c> / <c>a &gt;&gt;= n;</c> (n = 1..7).</summary>
    static readonly Regex ShiftAN = new(
        @"^a\s*(<<|>>)=\s*([1-7])\s*;\s*$",
        RegexOptions.Compiled);

    /// <summary><c>a *= 2|4|8|…|128;</c> → n×ASL. <c>a *= 3|5;</c> → shift-add via stack.</summary>
    static readonly Regex MulAConst = new(
        @"^a\s*\*=\s*(2|3|4|5|8|16|32|64|128)\s*;\s*$",
        RegexOptions.Compiled);

    /// <summary><c>ushort _u0 = (ushort)(lo | (hi &lt;&lt; 8));</c></summary>
    static readonly Regex UshortPack = new(
        @"^ushort\s+(_u\d+)\s*=\s*\(ushort\)\((.+)\s*\|\s*\((.+)\s*<<\s*8\)\)\s*;\s*$",
        RegexOptions.Compiled);

    /// <summary><c>_u0 += 8;</c></summary>
    static readonly Regex UshortAddImm = new(
        @"^(_u\d+)\s*\+=\s*(0x[0-9A-Fa-f]+|\d+)\s*;\s*$",
        RegexOptions.Compiled);

    /// <summary><c>dest = (byte)_u0;</c> / <c>dest = (byte)(_u0 & mask);</c> / <c>dest = (byte)(_u0 &gt;&gt; 8);</c></summary>
    static readonly Regex UshortStoreLo = new(
        @"^(.+?)\s*=\s*\(byte\)(?:\((_u\d+)\s*&\s*(0x[0-9A-Fa-f]+|\d+)\)|(_u\d+))\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex UshortStoreHi = new(
        @"^(.+?)\s*=\s*\(byte\)\((_u\d+)\s*>>\s*8\)\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex AdcPeek = new(
        @"^a\s*=\s*\(byte\)\(a\s*\+\s*peek\((.+)\)\s*\+\s*\(carry\s*\?\s*1\s*:\s*0\)\)\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex PadPollAssign = new(
        @"^(?<dest>a|[A-Za-z_]\w*(?:\[[^\]]+\])?)\s*=\s*\(byte\)pad_poll(?<msb>_msb)?\((?<arg>.+)\)\s*;\s*$",
        RegexOptions.Compiled);

    /// <summary><c>x += 7;</c> / <c>y &amp;= 0x3E;</c> raised SoftCpu index ALU.</summary>
    static readonly Regex IndexAluAssign = new(
        @"^(x|y)\s*(\+=|&=|\|=|\^=)\s*(0x[0-9A-Fa-f]+|\d+)\s*;\s*$",
        RegexOptions.Compiled);

    /// <summary><c>dest = pla();</c></summary>
    static readonly Regex MemPlaAssign = new(
        @"^([A-Za-z_]\w*(?:\[[^\]]+\])?|x|y)\s*=\s*(?:pla|pop)\(\s*\)\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex AluEnumImm = new(
        @"^a\s*=\s*\(byte\)\(a\s*([|&^])\s*\(byte\)(~?\([^;]+\)|~?[A-Za-z_][\w.]*)\)\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex AndPeek = new(
        @"^a\s*=\s*\(byte\)\(a\s*&\s*peek\((.+)\)\)\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex VramBufPtrLoad = new(
        @"^ushort\s+_p\s*=\s*(vramBufPtr(?:_[0-9A-Fa-f]+)?)\s*\[\s*(x|y)\s*\]\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Regex PaletteEntry = new(
        @"\(\s*0x([0-9A-Fa-f]{1,4})\s*,\s*new\s+byte\[\]\s*\{([^}]*)\}\s*\)",
        RegexOptions.Compiled);

    static readonly Regex VramPtrComment = new(
        @"^//\s*VRAM buffer pointers\s*\(lo\$([0-9A-Fa-f]{4})/hi\$([0-9A-Fa-f]{4})\)",
        RegexOptions.Compiled);

    static readonly Regex VramPtrArray = new(
        @"^ushort\[\]\s+(vramBufPtr(?:_[0-9A-Fa-f]+)?)\s*=\s*\{(.+)\}\s*;\s*$",
        RegexOptions.Compiled);

    static readonly Dictionary<string, byte> EnumFlags = BuildEnumFlags();
    static readonly Dictionary<string, byte> NesColors = BuildNesColors();

    public static SoftCpuResult Recompile(string programCs, byte[] basePrg, ILogger? logger = null)
    {
        logger ??= new NullLogger();
        byte[] prg = (byte[])basePrg.Clone();
        var consts = ParseConsts(programCs);
        var tables = ParseSoftCpuTables(programCs);
        var routines = ParseRoutines(programCs);
        var nameToAddress = new Dictionary<string, ushort>(StringComparer.Ordinal);
        foreach (var r in routines)
            nameToAddress[r.Name] = r.Address;

        // Size budgets: next routine at higher CPU address in same bank, else bank end.
        var ordered = routines.OrderBy(r => r.Bank).ThenBy(r => r.Address).ToList();
        int lowered = 0, skipped = 0;
        var diagnostics = new List<string>();

        for (int i = 0; i < ordered.Count; i++)
        {
            var routine = ordered[i];
            // Decomp may park a whole CFG on a mid-routine JSR entry (e.g. SMB $8EDD
            // with body starting at L_8E92). Overlay from the earliest in-body label.
            ushort overlayAddr = EarliestOverlayAddress(routine);
            ushort hardEnd = FindBudgetEnd(ordered, i, prg.Length);
            // Keep overlay from walking into the previous routine's entry.
            if (i > 0 && ordered[i - 1].Bank == routine.Bank)
            {
                ushort prevOverlay = EarliestOverlayAddress(ordered[i - 1]);
                if (overlayAddr < prevOverlay)
                    overlayAddr = prevOverlay;
            }
            int hardBudget = hardEnd - overlayAddr;
            if (hardBudget <= 0)
            {
                skipped++;
                diagnostics.Add($"{routine.Name} @${routine.Address:X4}: no size budget");
                continue;
            }

            if (!TryCpuToPrgOffset(overlayAddr, routine.Bank, prg.Length, out int offset))
            {
                skipped++;
                diagnostics.Add($"{routine.Name} @${routine.Address:X4}: cannot map to PRG");
                continue;
            }

            // Room for edits: original code span (stops before trailing data), not the
            // whole gap to the next routine. Blank/test canvases use the full hard budget
            // for the size check, but only pad up to the emitted length (don't NOP-wipe tables).
            int originalLen = MeasureOriginalCodeLength(prg, offset, overlayAddr, hardEnd);
            bool blankCanvas = originalLen > 0 && IsBlankCanvas(prg.AsSpan(offset, Math.Min(originalLen, hardBudget)));
            if (blankCanvas || originalLen <= 0)
                originalLen = hardBudget;
            int maxBytes = Math.Min(hardBudget, originalLen);

            if (!TryLowerRoutine(routine, overlayAddr, consts, nameToAddress, tables, maxBytes, out byte[]? bytes, out string? error))
            {
                skipped++;
                diagnostics.Add($"{routine.Name} @${routine.Address:X4}: {error}");
                continue;
            }

            if (bytes!.Length > maxBytes)
            {
                skipped++;
                diagnostics.Add(
                    $"{routine.Name} @${routine.Address:X4}: emitted {bytes.Length}B > span {maxBytes}B (edit shrank space or lowerer grew encoding)");
                continue;
            }

            if (offset + maxBytes > prg.Length)
            {
                skipped++;
                diagnostics.Add($"{routine.Name} @${routine.Address:X4}: write past PRG end");
                continue;
            }

            // Incomplete Program.cs lowers are often short. Never NOP-pad over real opcodes —
            // that is what black-screened SMB after an unrelated palette edit. Same-length
            // edits and trailing-filler shrinks are fine; blank canvases always apply.
            int padTo = blankCanvas ? bytes.Length : maxBytes;
            if (!blankCanvas && bytes.Length < maxBytes
                && !IsBlankCanvas(prg.AsSpan(offset + bytes.Length, maxBytes - bytes.Length)))
            {
                skipped++;
                diagnostics.Add(
                    $"{routine.Name} @${routine.Address:X4}: keep gap-bridge ({bytes.Length}B < {maxBytes}B span; would NOP real code)");
                continue;
            }

            // Apply Program.cs encoding (may differ from prg.bin — that is the point),
            // then NOP-pad the rest of the original span so trailing tables stay put.
            Array.Copy(bytes, 0, prg, offset, bytes.Length);
            for (int p = bytes.Length; p < padTo; p++)
                prg[offset + p] = 0xEA;
            lowered++;
            logger.WriteLine(
                $"SoftCpu: lowered {routine.Name} @${overlayAddr:X4} (entry ${routine.Address:X4}, {bytes.Length}B into {maxBytes}B span)");
        }

        // Palette / vramBufPtr data last so Program.cs table edits win over any overlay padding.
        ApplyTableData(prg, tables, logger);

        logger.WriteLine($"SoftCpu: lowered {lowered}, kept gap-bridge for {skipped} routines");
        return new SoftCpuResult(prg, lowered, skipped, diagnostics);
    }

    static ushort FindBudgetEnd(List<SoftCpuRoutine> ordered, int index, int prgLength)
    {
        var cur = ordered[index];
        for (int j = index + 1; j < ordered.Count; j++)
        {
            if (ordered[j].Bank == cur.Bank && ordered[j].Address > cur.Address)
                return ordered[j].Address;
        }

        // End of 16KB bank window containing this routine.
        ushort bankBase = (ushort)(cur.Address & 0xC000);
        if (bankBase < 0x8000)
            bankBase = 0x8000;
        uint end = bankBase + 0x4000u;
        if (end > 0x10000)
            end = 0x10000;
        // Also clamp by PRG size when NROM-128.
        if (prgLength == 0x4000)
            end = Math.Min(end, 0xC000u);
        return (ushort)end;
    }

    static bool IsBlankCanvas(ReadOnlySpan<byte> span)
    {
        for (int i = 0; i < span.Length; i++)
        {
            byte b = span[i];
            if (b != 0x00 && b != 0xEA)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Walk the existing PRG from a routine entry and return how many bytes of
    /// reachable code exist before RTS/RTI/tail-JMP or the hard budget. Keeps
    /// SoftCpu from eating trailing data tables that sit before the next routine.
    /// </summary>
    static int MeasureOriginalCodeLength(byte[] prg, int offset, ushort cpuStart, ushort cpuHardEnd)
    {
        int hardBytes = cpuHardEnd - cpuStart;
        if (hardBytes <= 0 || offset < 0 || offset >= prg.Length)
            return 0;

        var queue = new Queue<int>();
        var seen = new HashSet<int>();
        queue.Enqueue(offset);
        int maxOff = offset;

        while (queue.Count > 0)
        {
            int off = queue.Dequeue();
            if (!seen.Add(off))
                continue;
            if (off < offset || off >= offset + hardBytes || off >= prg.Length)
                continue;

            byte op = prg[off];
            if (!OpcodeTable.TryDecode(op, out Opcode opcode, out AddressMode mode))
            {
                // Illegal/unknown — treat as end of code stream.
                continue;
            }

            int size = OpcodeTable.GetInstructionSize(mode);
            if (off + size > prg.Length || off + size > offset + hardBytes)
                continue;

            maxOff = Math.Max(maxOff, off + size);
            ushort cpu = (ushort)(cpuStart + (off - offset));

            if (opcode is Opcode.RTS or Opcode.RTI or Opcode.BRK)
                continue;

            if (opcode == Opcode.JMP && mode == AddressMode.Absolute)
            {
                ushort target = (ushort)(prg[off + 1] | (prg[off + 2] << 8));
                if (target >= cpuStart && target < cpuHardEnd)
                    queue.Enqueue(offset + (target - cpuStart));
                continue; // no fallthrough
            }

            if (opcode == Opcode.JMP && mode == AddressMode.Indirect)
                continue; // unknown target — stop this path

            if (IsRelativeBranch(opcode))
            {
                sbyte rel = (sbyte)prg[off + 1];
                int targetOff = off + 2 + rel;
                queue.Enqueue(off + size);
                if (targetOff >= offset && targetOff < offset + hardBytes)
                    queue.Enqueue(targetOff);
                continue;
            }

            // JSR and everything else fall through
            queue.Enqueue(off + size);
            _ = cpu; // keep address math obvious for readers
        }

        return Math.Max(0, maxOff - offset);
    }

    static bool IsRelativeBranch(Opcode opcode) =>
        opcode is Opcode.BCC or Opcode.BCS or Opcode.BEQ or Opcode.BMI
            or Opcode.BNE or Opcode.BPL or Opcode.BVC or Opcode.BVS;

    static bool LastLoadsAWithImm(List<AsmItem> items, byte imm)
    {
        for (int i = items.Count - 1; i >= 0; i--)
        {
            Instruction? ins = items[i].Instruction;
            if (ins is null)
                continue;
            if (ins.Opcode == Opcode.LDA && ins.Mode == AddressMode.Immediate
                && ins.Operand is ImmediateOperand io && io.Value == imm)
                return true;
            // STA of A keeps the imm in A; keep scanning backward through stores of A.
            if (ins.Opcode == Opcode.STA)
                continue;
            return false;
        }
        return false;
    }

    internal static bool TryCpuToPrgOffset(ushort cpu, int bank, int prgLength, out int offset)
    {
        offset = 0;
        if (cpu < 0x8000 || prgLength <= 0)
            return false;
        if (prgLength == 0x4000)
        {
            offset = (cpu - 0x8000) & 0x3FFF;
            return true;
        }
        if (prgLength == 0x8000)
        {
            offset = cpu - 0x8000;
            return offset >= 0 && offset < prgLength;
        }

        const int bankSize = 0x4000;
        offset = bank * bankSize + (cpu & (bankSize - 1));
        return offset >= 0 && offset < prgLength;
    }

    static ushort EarliestOverlayAddress(SoftCpuRoutine routine)
    {
        ushort earliest = routine.Address;
        foreach (string raw in routine.Body)
        {
            var m = LabelLine.Match(raw.Trim());
            if (!m.Success)
                continue;
            string name = m.Groups[1].Value; // L_XXXX
            if (name.Length < 3 || name[0] != 'L' || name[1] != '_')
                continue;
            if (!ushort.TryParse(name.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort addr))
                continue;
            if (addr < earliest)
                earliest = addr;
        }
        return earliest;
    }

    static bool TryLowerRoutine(
        SoftCpuRoutine routine,
        ushort assembleBase,
        Dictionary<string, ushort> consts,
        Dictionary<string, ushort> nameToAddress,
        SoftCpuTables tables,
        int maxBytes,
        out byte[]? bytes,
        out string? error)
    {
        bytes = null;
        error = null;
        var items = new List<AsmItem>();
        int waitSeq = 0;
        // SMB pad_poll_msb leaves the button byte on the stack as well as in A;
        // the following pha() is then a no-op (already pushed inside the poll loop).
        bool padResultOnStack = false;

        for (int i = 0; i < routine.Body.Count; i++)
        {
            string line = routine.Body[i].Trim();
            if (line.Length == 0)
                continue;

            // Collapse CMP flag triple written as one logical line or three.
            if (TryConsumeCompare(routine.Body, ref i, consts, items, out string? cmpErr))
            {
                if (cmpErr != null)
                {
                    error = cmpErr;
                    return false;
                }
                padResultOnStack = false;
                continue;
            }

            if (TryConsumeVramBufPtrBlock(routine.Body, ref i, tables, consts, items, out error))
            {
                if (error != null)
                    return false;
                padResultOnStack = false;
                continue;
            }

            if (TryConsumeAdd16Ushort(routine.Body, ref i, consts, items, out error))
            {
                if (error != null)
                    return false;
                padResultOnStack = false;
                continue;
            }

            if (TryConsumeSwitch(routine.Body, ref i, consts, nameToAddress, tables, ref waitSeq, items, out error, ref padResultOnStack))
            {
                if (error != null)
                    return false;
                continue;
            }

            if (TryConsumeFor(routine.Body, ref i, consts, nameToAddress, tables, ref waitSeq, items, out error, ref padResultOnStack))
            {
                if (error != null)
                    return false;
                continue;
            }

            if (TryConsumeDoWhile(routine.Body, ref i, consts, nameToAddress, tables, ref waitSeq, items, out error, ref padResultOnStack))
            {
                if (error != null)
                    return false;
                continue;
            }

            // name--; if (name != 0) goto L; → DEC; BNE (keep Z from DEC, no reload)
            if (TryConsumeMemDecBranch(routine.Body, ref i, consts, items, out error))
            {
                if (error != null)
                    return false;
                padResultOnStack = false;
                continue;
            }

            if (TryConsumeIfElse(routine.Body, ref i, consts, nameToAddress, tables, ref waitSeq, items, out error, ref padResultOnStack))
            {
                if (error != null)
                    return false;
                continue;
            }

            try
            {
                if (line is ("pha();" or "push(a);") && padResultOnStack)
                {
                    padResultOnStack = false; // already PHA'd inside pad_poll*
                    continue;
                }

                if (!TryLowerLine(line, consts, nameToAddress, tables, ref waitSeq, items, out error, ref padResultOnStack))
                {
                    error ??= $"unsupported: {line}";
                    return false;
                }
            }
            catch (Exception ex)
            {
                error = $"{ex.Message} @ {line}";
                return false;
            }
        }

        // Empty stub → RTS
        if (items.Count == 0)
        {
            items.Add(new AsmItem(Asm.RTS()));
        }

        try
        {
            bytes = Assemble(assembleBase, items);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        if (bytes.Length > maxBytes)
        {
            error = $"emitted {bytes.Length} bytes > budget {maxBytes}";
            return false;
        }

        return true;
    }

    static bool TryConsumeCompare(
        IReadOnlyList<string> body,
        ref int i,
        Dictionary<string, ushort> consts,
        List<AsmItem> items,
        out string? error)
    {
        error = null;
        string line = body[i].Trim();

        // Single-line combined form on A/X/Y
        var ma = CmpFlagsA.Match(line);
        if (ma.Success)
        {
            items.Add(new AsmItem(Asm.CMP(ParseByte(ma.Groups[1].Value))));
            return true;
        }
        var mx = CmpFlagsX.Match(line);
        if (mx.Success)
        {
            items.Add(new AsmItem(Asm.CPX(ParseByte(mx.Groups[1].Value))));
            return true;
        }
        var my = CmpFlagsY.Match(line);
        if (my.Success)
        {
            items.Add(new AsmItem(Asm.CPY(ParseByte(my.Groups[1].Value))));
            return true;
        }

        // Raised: zero = src == N; carry = src >= N; negative = (byte)(src - N) >= 0x80;
        var memFlags = Regex.Match(line,
            @"^zero\s*=\s*(?<e>.+?)\s*==\s*(?<n>0x[0-9A-Fa-f]+|\d+)\s*;\s*carry\s*=\s*\k<e>\s*>=\s*\k<n>\s*;\s*negative\s*=\s*\(byte\)\(\k<e>\s*-\s*\k<n>\)\s*>=\s*0x80\s*;\s*$");
        if (memFlags.Success)
        {
            string expr = memFlags.Groups["e"].Value.Trim();
            if (expr is not ("a" or "x" or "y"))
            {
                if (!TryEmitByteExprIntoA(expr, consts, items, out error)
                    && !TryEmitLoadAFromMemExpr(expr, consts, items, out error))
                {
                    error ??= $"cmp flags load {expr}";
                    return true; // consumed but failed
                }
                items.Add(new AsmItem(Asm.CMP(ParseByte(memFlags.Groups["n"].Value))));
                return true;
            }
        }

        // { byte _v = rhs; zero = lhs == _v; carry = lhs >= _v; negative = (byte)(lhs - _v) >= 0x80; }
        // SoftCpu: LDA lhs; CMP rhs (lhs may already be live in A).
        var memCmpBlock = Regex.Match(line,
            @"^\{\s*byte\s+_v\s*=\s*(?<rhs>.+?);\s*zero\s*=\s*(?<lhs>.+?)\s*==\s*_v;\s*carry\s*=\s*\k<lhs>\s*>=\s*_v;\s*negative\s*=\s*\(byte\)\(\k<lhs>\s*-\s*_v\)\s*>=\s*0x80;\s*\}\s*$");
        if (memCmpBlock.Success)
        {
            string lhs = memCmpBlock.Groups["lhs"].Value.Trim();
            string rhs = memCmpBlock.Groups["rhs"].Value.Trim();
            if (lhs is not "a")
            {
                if (!TryEmitByteExprIntoA(lhs, consts, items, out error)
                    && !TryEmitLoadAFromMemExpr(lhs, consts, items, out error))
                {
                    error ??= $"mem-cmp lhs {lhs}";
                    return true;
                }
            }
            if (rhs.StartsWith("peek(", StringComparison.Ordinal) && rhs.EndsWith(")", StringComparison.Ordinal))
                rhs = rhs.Substring("peek(".Length, rhs.Length - "peek(".Length - 1).Trim();
            if (!TryResolveAddress(rhs, consts, out var cmpEa)
                && !TryResolveMemEa(rhs, consts, out cmpEa))
            {
                error = $"mem-cmp rhs {rhs}";
                return true;
            }
            items.Add(new AsmItem(EmitCmp(cmpEa)));
            return true;
        }

        // Multi-line: zero = a|x|y|mem == N; carry = …; negative = ...
        var zm = Regex.Match(line, @"^zero\s*=\s*(?<e>.+?)\s*==\s*(?<n>0x[0-9A-Fa-f]+|\d+)\s*;\s*$");
        if (zm.Success)
        {
            string expr = zm.Groups["e"].Value.Trim();
            string imm = zm.Groups["n"].Value;
            int j = i + 1;
            if (j < body.Count)
            {
                string carryLine = body[j].Trim();
                if (Regex.IsMatch(carryLine, $@"^carry\s*=\s*{Regex.Escape(expr)}\s*>=\s*{Regex.Escape(imm)}\s*;\s*$"))
                {
                    j++;
                    if (j < body.Count
                        && Regex.IsMatch(body[j].Trim(), $@"^negative\s*=\s*\(byte\)\({Regex.Escape(expr)}\s*-\s*{Regex.Escape(imm)}\)\s*>=\s*0x80\s*;\s*$"))
                        j++;
                    i = j - 1;
                    byte v = ParseByte(imm);
                    if (expr is "a")
                    {
                        items.Add(new AsmItem(Asm.CMP(v)));
                        return true;
                    }
                    if (expr is "x")
                    {
                        items.Add(new AsmItem(Asm.CPX(v)));
                        return true;
                    }
                    if (expr is "y")
                    {
                        items.Add(new AsmItem(Asm.CPY(v)));
                        return true;
                    }
                    if (!TryEmitByteExprIntoA(expr, consts, items, out error)
                        && !TryEmitLoadAFromMemExpr(expr, consts, items, out error))
                    {
                        error ??= $"cmp flags load {expr}";
                        return true;
                    }
                    items.Add(new AsmItem(Asm.CMP(v)));
                    return true;
                }
            }
        }

        return false;
    }

    static bool TryLowerLine(
        string line,
        Dictionary<string, ushort> consts,
        Dictionary<string, ushort> nameToAddress,
        SoftCpuTables tables,
        ref int waitSeq,
        List<AsmItem> items,
        out string? error,
        ref bool padResultOnStack)
    {
        error = null;
        padResultOnStack = false;

        // Strip trailing // comments (e.g. vram_write(new byte[]{…}); // can be replaced with "…")
        int trailComment = IndexOfTrailingLineComment(line);
        if (trailComment >= 0)
            line = line.Substring(0, trailComment).TrimEnd();

        // Comment-only asm leftovers: // CLD
        if (line.StartsWith("//", StringComparison.Ordinal))
        {
            string rest = line.Substring(2).Trim();
            if (rest.Equals("CLD", StringComparison.OrdinalIgnoreCase))
            {
                items.Add(new AsmItem(Asm.CLD()));
                return true;
            }
            if (rest.Equals("SED", StringComparison.OrdinalIgnoreCase))
            {
                items.Add(new AsmItem(Asm.SED()));
                return true;
            }
            // Other comments ignored
            return true;
        }

        var label = LabelLine.Match(line);
        if (label.Success)
        {
            items.Add(new AsmItem(label.Groups[1].Value));
            return true;
        }

        if (line is "sei();")
        {
            items.Add(new AsmItem(Asm.SEI()));
            return true;
        }
        if (line is "cli();")
        {
            items.Add(new AsmItem(Asm.CLI()));
            return true;
        }
        if (line is "cld();")
        {
            items.Add(new AsmItem(Asm.CLD()));
            return true;
        }
        if (line is "sed();")
        {
            items.Add(new AsmItem(Asm.SED()));
            return true;
        }
        if (line is "txs();")
        {
            items.Add(new AsmItem(Asm.TXS()));
            return true;
        }
        if (line is "tsx();")
        {
            items.Add(new AsmItem(Asm.TSX()));
            return true;
        }
        if (line is "php();")
        {
            items.Add(new AsmItem(Asm.PHP()));
            return true;
        }
        if (line is "plp();")
        {
            items.Add(new AsmItem(Asm.PLP()));
            return true;
        }
        if (line is "carry = false;")
        {
            items.Add(new AsmItem(Asm.CLC()));
            return true;
        }
        if (line is "carry = true;")
        {
            items.Add(new AsmItem(Asm.SEC()));
            return true;
        }
        if (line is "pha();" or "push(a);")
        {
            items.Add(new AsmItem(Asm.PHA()));
            return true;
        }
        // Raised: pha(expr); → load expr into A, then PHA (carry/flags from expr shifts preserved).
        var phaExpr = Regex.Match(line, @"^pha\((.+)\)\s*;\s*$");
        if (phaExpr.Success)
        {
            string expr = phaExpr.Groups[1].Value.Trim();
            if (expr is "a")
            {
                items.Add(new AsmItem(Asm.PHA()));
                return true;
            }
            if (!TryEmitByteExprIntoA(expr, consts, items, out error))
            {
                error ??= $"pha({expr})";
                return false;
            }
            items.Add(new AsmItem(Asm.PHA()));
            return true;
        }
        if (line is "a = pla();" or "pla();" or "a = pop();")
        {
            items.Add(new AsmItem(Asm.PLA()));
            return true;
        }
        var memPla = MemPlaAssign.Match(line);
        if (memPla.Success && memPla.Groups[1].Value is not ("a" or "carry" or "zero" or "negative"))
        {
            items.Add(new AsmItem(Asm.PLA()));
            string dest = memPla.Groups[1].Value;
            if (dest == "x")
                items.Add(new AsmItem(Asm.TAX()));
            else if (dest == "y")
                items.Add(new AsmItem(Asm.TAY()));
            else if (!TryEmitStoreAToMemExpr(dest, consts, items, out error))
            {
                error ??= $"pla store {dest}";
                return false;
            }
            return true;
        }
        // a|x|y = (byte)(…) / pla / ALU tree — raised SoftCpu surface
        var regFromExpr = Regex.Match(line,
            @"^(a|x|y)\s*=\s*(.+)\s*;\s*$");
        if (regFromExpr.Success
            && regFromExpr.Groups[2].Value.Trim() is not ("a" or "x" or "y")
            && !regFromExpr.Groups[2].Value.Trim().StartsWith("(byte)(a ", StringComparison.Ordinal))
        {
            string reg = regFromExpr.Groups[1].Value;
            string val = regFromExpr.Groups[2].Value.Trim();
            // Plain mem/lit already handled by MemVarLoad / BufIndexLoad / PeekAssign below.
            // Only claim byte-expr / pla / shift trees here so we don't steal LD* abs.
            bool looksExpr = val.StartsWith("(byte)(", StringComparison.Ordinal)
                || val is "pla()" or "pop()"
                || val.Contains(">>", StringComparison.Ordinal)
                || val.Contains("<<", StringComparison.Ordinal)
                || (val.Contains('+') || val.Contains('&') || val.Contains('|')
                    || val.Contains('^') || val.Contains('*') || val.Contains('-'));
            if (looksExpr)
            {
                if (!TryEmitByteExprIntoA(val, consts, items, out error))
                {
                    error ??= $"{reg} from expr {val}";
                    return false;
                }
                if (reg == "x")
                    items.Add(new AsmItem(Asm.TAX()));
                else if (reg == "y")
                    items.Add(new AsmItem(Asm.TAY()));
                return true;
            }
        }

        var idxAlu = IndexAluAssign.Match(line);
        if (idxAlu.Success)
        {
            string reg = idxAlu.Groups[1].Value;
            string op = idxAlu.Groups[2].Value;
            byte n = ParseByte(idxAlu.Groups[3].Value);
            items.Add(new AsmItem(reg == "x" ? Asm.TXA() : Asm.TYA()));
            switch (op)
            {
                case "+=":
                    items.Add(new AsmItem(Asm.CLC()));
                    items.Add(new AsmItem(Asm.ADC(n)));
                    break;
                case "&=":
                    items.Add(new AsmItem(Asm.AND(n)));
                    break;
                case "|=":
                    items.Add(new AsmItem(Asm.ORA(n)));
                    break;
                case "^=":
                    items.Add(new AsmItem(Asm.EOR(n)));
                    break;
            }
            items.Add(new AsmItem(reg == "x" ? Asm.TAX() : Asm.TAY()));
            return true;
        }
        if (line is "return;" or "return; // RTI")
        {
            items.Add(new AsmItem(line.Contains("RTI") ? Asm.RTI() : Asm.RTS()));
            return true;
        }
        if (line is "while (true) ;" or "while (true);")
        {
            // JMP to self — assemble with a synthetic label
            string spin = $"__spin_{waitSeq++}";
            items.Add(new AsmItem(spin));
            items.Add(new AsmItem(Asm.JMP(spin)));
            return true;
        }
        if (line is "waitvsync();")
        {
            string top = $"__wvs_{waitSeq++}";
            items.Add(new AsmItem(top));
            items.Add(new AsmItem(Asm.LDA_abs(0x2002)));
            items.Add(new AsmItem(Asm.BPL(top)));
            return true;
        }

        // Soft-CPU palette bind helper — no 6502 bytes (zp already holds the pointer).
        if (line is "BindVramFromZp();")
            return true;

        var peekVram = PeekVramAssign.Match(line);
        if (peekVram.Success)
        {
            // LDA ($00),Y — only A can load from (zp),Y directly.
            if (peekVram.Groups[1].Value != "a")
            {
                error = $"PeekVramBufY dest {peekVram.Groups[1].Value}";
                return false;
            }
            items.Add(new AsmItem(Asm.LDA_ind_Y(0)));
            return true;
        }

        var palData = PaletteDataAssign.Match(line);
        if (palData.Success)
        {
            int idx = int.Parse(palData.Groups[2].Value, CultureInfo.InvariantCulture);
            if (idx < 0 || idx >= tables.Palettes.Count)
            {
                error = $"palette[{idx}] out of range";
                return false;
            }
            ushort baseAddr = tables.Palettes[idx].Base;
            string off = palData.Groups[3].Value;
            string reg = palData.Groups[1].Value;
            if (off is "x" or "y")
            {
                SoftAddrMode mode = off == "x" ? SoftAddrMode.AbsoluteX : SoftAddrMode.AbsoluteY;
                items.Add(new AsmItem(EmitLoad(reg, new EffectiveAddress(baseAddr, mode))));
                return true;
            }
            if (!TryParseByteLiteral(off, out byte o))
            {
                error = $"palette offset {off}";
                return false;
            }
            items.Add(new AsmItem(EmitLoad(reg, new EffectiveAddress((ushort)(baseAddr + o), SoftAddrMode.Absolute))));
            return true;
        }

        if (AslA.IsMatch(line))
        {
            items.Add(new AsmItem(Asm.ASL_A()));
            return true;
        }
        if (LsrA.IsMatch(line))
        {
            items.Add(new AsmItem(Asm.LSR_A()));
            return true;
        }
        var mulA = MulAConst.Match(line);
        if (mulA.Success)
        {
            int factor = int.Parse(mulA.Groups[1].Value, CultureInfo.InvariantCulture);
            return TryEmitMulAConst(factor, items, out error);
        }
        var shiftN = ShiftAN.Match(line);
        if (shiftN.Success)
        {
            int n = int.Parse(shiftN.Groups[2].Value, CultureInfo.InvariantCulture);
            bool left = shiftN.Groups[1].Value == "<<";
            for (int s = 0; s < n; s++)
                items.Add(new AsmItem(left ? Asm.ASL_A() : Asm.LSR_A()));
            return true;
        }

        var adcPeek = AdcPeek.Match(line);
        if (adcPeek.Success)
        {
            if (!TryResolveAddress(adcPeek.Groups[1].Value, consts, out var ea))
            {
                error = $"adc peek {adcPeek.Groups[1].Value}";
                return false;
            }
            items.Add(new AsmItem(EmitAdc(ea)));
            return true;
        }

        var status = PpuStatusAssign.Match(line);
        if (status.Success)
        {
            // Prefer direct LD* abs (SMB uses LDX $2002) over LDA+transfer.
            string dest = status.Groups[1].Value;
            items.Add(new AsmItem(dest switch
            {
                "x" => Asm.LDX_abs(0x2002),
                "y" => Asm.LDY_abs(0x2002),
                _ => Asm.LDA_abs(0x2002),
            }));
            return true;
        }

        var vput = VramPut.Match(line);
        if (vput.Success)
        {
            string val = vput.Groups[1].Value.Trim();
            if (val is "a" or "x" or "y")
            {
                if (val != "a")
                    items.Add(new AsmItem(val == "x" ? Asm.TXA() : Asm.TYA()));
                items.Add(new AsmItem(Asm.STA_abs(0x2007)));
                return true;
            }
            if (TryParseByteOrColor(val, out byte immV))
            {
                if (!LastLoadsAWithImm(items, immV))
                    items.Add(new AsmItem(Asm.LDA(immV)));
                items.Add(new AsmItem(Asm.STA_abs(0x2007)));
                return true;
            }
            if (TryEmitByteExprIntoA(val, consts, items, out error)
                || TryEmitLoadAFromMemExpr(val, consts, items, out error))
            {
                items.Add(new AsmItem(Asm.STA_abs(0x2007)));
                return true;
            }
            error ??= $"vram_put value {val}";
            return false;
        }

        var vfill = VramFill.Match(line);
        if (vfill.Success)
        {
            if (!TryLowerVramFill(vfill.Groups[1].Value.Trim(), vfill.Groups[2].Value.Trim(), items, out error))
                return false;
            return true;
        }

        var vwStr = VramWriteString.Match(line);
        if (vwStr.Success)
        {
            // Unescape minimal C# string body → bytes, then unroll STA $2007.
            string body = UnescapeCSharpString(vwStr.Groups[1].Value);
            foreach (char ch in body)
            {
                byte b = (byte)ch;
                if (!LastLoadsAWithImm(items, b))
                    items.Add(new AsmItem(Asm.LDA(b)));
                items.Add(new AsmItem(Asm.STA_abs(0x2007)));
            }
            return true;
        }

        var vwBytes = VramWriteBytes.Match(line);
        if (vwBytes.Success)
        {
            if (!TryParseByteList(vwBytes.Groups[1].Value, out byte[] data))
            {
                error = "vram_write byte[]";
                return false;
            }
            foreach (byte b in data)
            {
                if (!LastLoadsAWithImm(items, b))
                    items.Add(new AsmItem(Asm.LDA(b)));
                items.Add(new AsmItem(Asm.STA_abs(0x2007)));
            }
            return true;
        }

        var vadr = VramAdr.Match(line);
        if (vadr.Success)
        {
            if (!TryLowerVramAdr(vadr.Groups[1].Value.Trim(), items, out error))
                return false;
            return true;
        }

        var scroll = PpuScroll.Match(line);
        if (scroll.Success)
        {
            if (!TryEmitValueToAbs(scroll.Groups[1].Value.Trim(), 0x2005, consts, items, out error)
                || !TryEmitValueToAbs(scroll.Groups[2].Value.Trim(), 0x2005, consts, items, out error))
                return false;
            return true;
        }

        var oam = OamDma.Match(line);
        if (oam.Success)
        {
            // STA $2003 (A already page-clear / zero); LDA #page ; STA $4014
            items.Add(new AsmItem(Asm.STA_abs(0x2003)));
            string page = oam.Groups[1].Value.Trim();
            if (page is "a" or "x" or "y")
            {
                if (page != "a")
                    items.Add(new AsmItem(page == "x" ? Asm.TXA() : Asm.TYA()));
            }
            else if (TryParseByteLiteral(page, out byte pageImm))
            {
                items.Add(new AsmItem(Asm.LDA(pageImm)));
            }
            else
            {
                error = $"oam_dma page {page}";
                return false;
            }
            items.Add(new AsmItem(Asm.STA_abs(0x4014)));
            return true;
        }

        var ppu = PpuCtrl.Match(line);
        if (ppu.Success)
        {
            string expr = ppu.Groups[1].Value.Trim();
            if (IsRegisterCast(expr, "PPUCTRL", out string reg))
            {
                if (!TryEmitStoreReg(reg, 0x2000, items, out error))
                    return false;
                return true;
            }
            if (TryEmitCastEnumValueToAbs(expr, "PPUCTRL", 0x2000, consts, items, out error))
                return true;
            if (!TryEvalEnumByte(expr, out byte v))
            {
                error = $"ppu_ctrl value: {expr}";
                return false;
            }
            items.Add(new AsmItem(Asm.LDA(v)));
            items.Add(new AsmItem(Asm.STA_abs(0x2000)));
            return true;
        }
        var mask = PpuMask.Match(line);
        if (mask.Success)
        {
            string expr = mask.Groups[1].Value.Trim();
            if (IsRegisterCast(expr, "MASK", out string reg))
            {
                if (!TryEmitStoreReg(reg, 0x2001, items, out error))
                    return false;
                return true;
            }
            if (TryEmitCastEnumValueToAbs(expr, "MASK", 0x2001, consts, items, out error))
                return true;
            if (!TryEvalEnumByte(expr, out byte v))
            {
                error = $"ppu_mask value: {expr}";
                return false;
            }
            items.Add(new AsmItem(Asm.LDA(v)));
            items.Add(new AsmItem(Asm.STA_abs(0x2001)));
            return true;
        }
        var apu = ApuStatus.Match(line);
        if (apu.Success)
        {
            string expr = apu.Groups[1].Value.Trim();
            if (IsRegisterCast(expr, "APUSTATUS", out string reg))
            {
                if (!TryEmitStoreReg(reg, 0x4015, items, out error))
                    return false;
                return true;
            }
            if (TryEmitCastEnumValueToAbs(expr, "APUSTATUS", 0x4015, consts, items, out error))
                return true;
            if (!TryEvalEnumByte(expr, out byte v))
            {
                error = $"apu_status value: {expr}";
                return false;
            }
            items.Add(new AsmItem(Asm.LDA(v)));
            items.Add(new AsmItem(Asm.STA_abs(0x4015)));
            return true;
        }

        var apuFrame = ApuFrame.Match(line);
        if (apuFrame.Success)
        {
            string expr = apuFrame.Groups[1].Value.Trim();
            if (IsRegisterCast(expr, "APUFRAME", out string reg))
            {
                if (!TryEmitStoreReg(reg, 0x4017, items, out error))
                    return false;
                return true;
            }
            if (TryEmitCastEnumValueToAbs(expr, "APUFRAME", 0x4017, consts, items, out error))
                return true;
            if (!TryEvalEnumByte(expr, out byte v))
            {
                error = $"apu_frame value: {expr}";
                return false;
            }
            items.Add(new AsmItem(Asm.LDA(v)));
            items.Add(new AsmItem(Asm.STA_abs(0x4017)));
            return true;
        }

        var apuReg = ApuRegWrite.Match(line);
        if (apuReg.Success)
        {
            string which = apuReg.Groups[1].Value;
            if (!ApuRegWriteAddrs.TryGetValue(which, out ushort abs))
            {
                error = $"apu reg {which}";
                return false;
            }
            if (!TryEmitValueToAbs(apuReg.Groups[2].Value.Trim(), abs, consts, items, out error))
                return false;
            return true;
        }

        var apuStatusRead = ApuStatusRead.Match(line);
        if (apuStatusRead.Success)
        {
            items.Add(new AsmItem(EmitLoad(apuStatusRead.Groups[1].Value, new EffectiveAddress(0x4015, SoftAddrMode.Absolute))));
            return true;
        }

        var imm = ImmAssign.Match(line);
        if (imm.Success)
        {
            byte v = ParseByte(imm.Groups[2].Value);
            items.Add(new AsmItem(imm.Groups[1].Value switch
            {
                "a" => Asm.LDA(v),
                "x" => Asm.LDX(v),
                _ => Asm.LDY(v),
            }));
            return true;
        }

        var xfer = RegTransfer.Match(line);
        if (xfer.Success)
        {
            string dst = xfer.Groups[1].Value;
            string src = xfer.Groups[2].Value;
            Instruction? ins = (dst, src) switch
            {
                ("a", "x") => Asm.TXA(),
                ("a", "y") => Asm.TYA(),
                ("x", "a") => Asm.TAX(),
                ("y", "a") => Asm.TAY(),
                _ => null,
            };
            if (ins is null)
            {
                error = $"transfer {src}->{dst}";
                return false;
            }
            items.Add(new AsmItem(ins));
            return true;
        }

        var inc = RegIncDec.Match(line);
        if (inc.Success)
        {
            string reg = inc.Groups[1].Value;
            bool plus = inc.Groups[2].Value == "++";
            Instruction? ins = (reg, plus) switch
            {
                ("x", true) => Asm.INX(),
                ("x", false) => Asm.DEX(),
                ("y", true) => Asm.INY(),
                ("y", false) => Asm.DEY(),
                _ => null,
            };
            if (ins is null)
            {
                error = $"inc/dec {reg}";
                return false;
            }
            items.Add(new AsmItem(ins));
            return true;
        }

        var go = Goto.Match(line);
        if (go.Success)
        {
            items.Add(new AsmItem(Asm.JMP(go.Groups[1].Value)));
            return true;
        }

        var ig = IfGoto.Match(line);
        if (ig.Success)
        {
            bool neg = ig.Groups[1].Success;
            string flag = ig.Groups[2].Value;
            string lab = ig.Groups[3].Value;
            Instruction branch = (flag, neg) switch
            {
                ("carry", false) => Asm.BCS(lab),
                ("carry", true) => Asm.BCC(lab),
                ("zero", false) => Asm.BEQ(lab),
                ("zero", true) => Asm.BNE(lab),
                ("negative", false) => Asm.BMI(lab),
                ("negative", true) => Asm.BPL(lab),
                _ => throw new InvalidOperationException("branch"),
            };
            items.Add(new AsmItem(branch));
            return true;
        }

        var ifr = IfFlagReturn.Match(line);
        if (ifr.Success)
        {
            bool neg = ifr.Groups[1].Success;
            string flag = ifr.Groups[2].Value;
            string skip = $"__retskip_{waitSeq++}";
            items.Add(new AsmItem(BranchIfNot(flag, neg, skip)));
            items.Add(new AsmItem(Asm.RTS()));
            items.Add(new AsmItem(skip));
            return true;
        }

        // if ((byte)(expr) op imm) goto/return;  — raised SoftCpu carry-ALU compare
        if (TryMatchIfByteExprCompare(line, out bool byteNot, out string byteExpr, out string byteOp,
                out string byteImm, out string? byteGotoLab, out bool byteIsReturn))
        {
            if (!TryEmitByteExprIntoA(byteExpr, consts, items, out error))
            {
                error ??= $"if byte-expr {byteExpr}";
                return false;
            }
            if (byteIsReturn)
            {
                string skip = $"__retskip_{waitSeq++}";
                if (!TryEmitRegCompareBranch("a", byteOp, byteImm, skip, items, out error, invertBranch: !byteNot))
                    return false;
                items.Add(new AsmItem(Asm.RTS()));
                items.Add(new AsmItem(skip));
                return true;
            }
            return TryEmitRegCompareBranch(
                "a", byteOp, byteImm, byteGotoLab!, items, out error, invertBranch: byteNot);
        }

        var ircg = IfRegCompareGoto.Match(line);
        if (ircg.Success)
        {
            if (!TryEmitExprCompareBranch(
                    ircg.Groups[1].Value, ircg.Groups[2].Value, ircg.Groups[3].Value,
                    ircg.Groups[4].Value, consts, items, out error))
                return false;
            return true;
        }

        var ircr = IfRegCompareReturn.Match(line);
        if (ircr.Success)
        {
            string skip = $"__retskip_{waitSeq++}";
            if (!TryEmitExprCompareBranch(
                    ircr.Groups[1].Value, ircr.Groups[2].Value, ircr.Groups[3].Value,
                    skip, consts, items, out error, invertBranch: true))
                return false;
            items.Add(new AsmItem(Asm.RTS()));
            items.Add(new AsmItem(skip));
            return true;
        }

        // if (ramVar != 0) goto L; — LDA mem / Bxx (DEC+branch peephole is separate)
        var memZeroGoto = Regex.Match(line,
            @"^if\s*\(\s*([A-Za-z_]\w*)\s*(==|!=)\s*0\s*\)\s*goto\s+(L_[0-9A-Fa-f]+)\s*;\s*$");
        if (memZeroGoto.Success
            && memZeroGoto.Groups[1].Value is not ("a" or "x" or "y")
            && consts.ContainsKey(memZeroGoto.Groups[1].Value))
        {
            if (!TryResolveAddress(memZeroGoto.Groups[1].Value, consts, out var eaZ))
            {
                error = $"mem zero-test {memZeroGoto.Groups[1].Value}";
                return false;
            }
            items.Add(new AsmItem(EmitLoad("a", eaZ)));
            items.Add(new AsmItem(memZeroGoto.Groups[2].Value == "==" ? Asm.BEQ(memZeroGoto.Groups[3].Value) : Asm.BNE(memZeroGoto.Groups[3].Value)));
            return true;
        }
        var memZeroRet = Regex.Match(line,
            @"^if\s*\(\s*([A-Za-z_]\w*)\s*(==|!=)\s*0\s*\)\s*return\s*;\s*$");
        if (memZeroRet.Success
            && memZeroRet.Groups[1].Value is not ("a" or "x" or "y")
            && consts.ContainsKey(memZeroRet.Groups[1].Value))
        {
            if (!TryResolveAddress(memZeroRet.Groups[1].Value, consts, out var eaZr))
            {
                error = $"mem zero-test {memZeroRet.Groups[1].Value}";
                return false;
            }
            string skip = $"__retskip_{waitSeq++}";
            items.Add(new AsmItem(EmitLoad("a", eaZr)));
            // Invert: skip return when condition false
            items.Add(new AsmItem(memZeroRet.Groups[2].Value == "==" ? Asm.BNE(skip) : Asm.BEQ(skip)));
            items.Add(new AsmItem(Asm.RTS()));
            items.Add(new AsmItem(skip));
            return true;
        }

        // if ((a & MASK) != 0) goto L;  — bit test that may destroy A (caller discarded it)
        var bitGoto = Regex.Match(line,
            @"^if\s*\(\s*\(a\s*&\s*(0x[0-9A-Fa-f]+|\d+)\)\s*(==|!=)\s*0\s*\)\s*goto\s+(L_[0-9A-Fa-f]+)\s*;\s*$");
        if (bitGoto.Success)
        {
            if (!TryEmitBitTestBranch(
                    bitGoto.Groups[1].Value, bitGoto.Groups[2].Value, bitGoto.Groups[3].Value,
                    items, out error, invertBranch: false))
                return false;
            return true;
        }
        var bitRet = Regex.Match(line,
            @"^if\s*\(\s*\(a\s*&\s*(0x[0-9A-Fa-f]+|\d+)\)\s*(==|!=)\s*0\s*\)\s*return\s*;\s*$");
        if (bitRet.Success)
        {
            string skip = $"__retskip_{waitSeq++}";
            if (!TryEmitBitTestBranch(
                    bitRet.Groups[1].Value, bitRet.Groups[2].Value, skip,
                    items, out error, invertBranch: true))
                return false;
            items.Add(new AsmItem(Asm.RTS()));
            items.Add(new AsmItem(skip));
            return true;
        }

        var irmcg = IfRegMemCompareGoto.Match(line);
        if (irmcg.Success)
        {
            if (!TryEmitRegMemCompareBranch(
                    irmcg.Groups[1].Value, irmcg.Groups[2].Value, irmcg.Groups[3].Value,
                    irmcg.Groups[4].Value, consts, items, out error))
                return false;
            return true;
        }

        var irmcr = IfRegMemCompareReturn.Match(line);
        if (irmcr.Success)
        {
            string skip = $"__retskip_{waitSeq++}";
            if (!TryEmitRegMemCompareBranch(
                    irmcr.Groups[1].Value, irmcr.Groups[2].Value, irmcr.Groups[3].Value,
                    skip, consts, items, out error, invertBranch: true))
                return false;
            items.Add(new AsmItem(Asm.RTS()));
            items.Add(new AsmItem(skip));
            return true;
        }

        var xferInc = TransferInc.Match(line);
        if (xferInc.Success)
        {
            // y = (byte)(a ± 1); → TAY / INY|DEY (keeps A)
            string idx = xferInc.Groups[1].Value;
            bool plus = xferInc.Groups[2].Value == "+";
            items.Add(new AsmItem(idx == "y" ? Asm.TAY() : Asm.TAX()));
            if (plus)
                items.Add(new AsmItem(idx == "y" ? Asm.INY() : Asm.INX()));
            else
                items.Add(new AsmItem(idx == "y" ? Asm.DEY() : Asm.DEX()));
            return true;
        }

        var padPoll = PadPollAssign.Match(line);
        if (padPoll.Success)
        {
            bool msb = padPoll.Groups["msb"].Success;
            string arg = padPoll.Groups["arg"].Value.Trim();
            string dest = padPoll.Groups["dest"].Value;
            if (!TryLowerPadPoll(arg, msb, items, out error))
                return false;
            padResultOnStack = true; // result also sits on stack (SMB shape)
            if (dest is not "a"
                && !TryEmitStoreAToMemExpr(dest, consts, items, out error))
            {
                error ??= $"pad_poll store {dest}";
                return false;
            }
            return true;
        }

        var alu = AluImm.Match(line);
        if (alu.Success)
        {
            byte v = ParseByte(alu.Groups[2].Value);
            items.Add(new AsmItem(EmitAluOp(alu.Groups[1].Value, v)));
            return true;
        }

        var aluComp = AluCompoundImm.Match(line);
        if (aluComp.Success)
        {
            items.Add(new AsmItem(EmitAluOp(aluComp.Groups[1].Value, ParseByte(aluComp.Groups[2].Value))));
            return true;
        }

        var aluEnum = AluEnumImm.Match(line);
        if (aluEnum.Success)
        {
            if (!TryEvalEnumByte(aluEnum.Groups[2].Value, out byte v))
            {
                error = $"alu enum {aluEnum.Groups[2].Value}";
                return false;
            }
            items.Add(new AsmItem(EmitAluOp(aluEnum.Groups[1].Value, v)));
            return true;
        }

        var aluCompEnum = AluCompoundEnum.Match(line);
        if (aluCompEnum.Success)
        {
            if (!TryEvalEnumByte(aluCompEnum.Groups[2].Value, out byte v))
            {
                error = $"alu compound enum {aluCompEnum.Groups[2].Value}";
                return false;
            }
            items.Add(new AsmItem(EmitAluOp(aluCompEnum.Groups[1].Value, v)));
            return true;
        }

        var andPeek = AndPeek.Match(line);
        if (andPeek.Success)
        {
            if (!TryResolveAddress(andPeek.Groups[1].Value, consts, out var ea))
            {
                error = $"and peek {andPeek.Groups[1].Value}";
                return false;
            }
            items.Add(new AsmItem(EmitAnd(ea)));
            return true;
        }

        var andVar = AndMemVar.Match(line);
        if (andVar.Success && consts.ContainsKey(andVar.Groups[1].Value))
        {
            if (!TryResolveAddress(andVar.Groups[1].Value, consts, out var ea))
            {
                error = $"and var {andVar.Groups[1].Value}";
                return false;
            }
            items.Add(new AsmItem(EmitAnd(ea)));
            return true;
        }

        var adcVar = AdcMemVar.Match(line);
        if (adcVar.Success && consts.ContainsKey(adcVar.Groups[1].Value))
        {
            if (!TryResolveAddress(adcVar.Groups[1].Value, consts, out var ea))
            {
                error = $"adc var {adcVar.Groups[1].Value}";
                return false;
            }
            items.Add(new AsmItem(EmitAdc(ea)));
            return true;
        }

        var adc = AdcImm.Match(line);
        if (adc.Success)
        {
            items.Add(new AsmItem(Asm.ADC(ParseByte(adc.Groups[1].Value))));
            return true;
        }

        var adcAdd = AdcAssignAdd.Match(line);
        if (adcAdd.Success)
        {
            items.Add(new AsmItem(Asm.CLC()));
            items.Add(new AsmItem(Asm.ADC(ParseByte(adcAdd.Groups[1].Value))));
            return true;
        }

        if (AdcCarryBit.IsMatch(line))
        {
            items.Add(new AsmItem(Asm.ADC(0)));
            return true;
        }

        var memAdc0 = MemAdcCarryBit.Match(line);
        if (memAdc0.Success
            && memAdc0.Groups[1].Value is not ("a" or "x" or "y" or "carry" or "zero" or "negative"))
        {
            string dest = memAdc0.Groups[1].Value;
            if (!TryEmitLoadAFromMemExpr(dest, consts, items, out error))
            {
                error ??= $"adc0 load {dest}";
                return false;
            }
            items.Add(new AsmItem(Asm.ADC(0)));
            if (!TryEmitStoreAToMemExpr(dest, consts, items, out error))
            {
                error ??= $"adc0 store {dest}";
                return false;
            }
            return true;
        }

        var memAdcFrom = MemAdcCarryFrom.Match(line);
        if (memAdcFrom.Success
            && memAdcFrom.Groups[1].Value is not ("a" or "x" or "y" or "carry" or "zero" or "negative")
            && memAdcFrom.Groups[2].Value is not ("a" or "x" or "y"))
        {
            string dest = memAdcFrom.Groups[1].Value;
            string src = memAdcFrom.Groups[2].Value;
            if (!TryEmitLoadAFromMemExpr(src, consts, items, out error))
            {
                error ??= $"adc0 from {src}";
                return false;
            }
            items.Add(new AsmItem(Asm.ADC(0)));
            if (!TryEmitStoreAToMemExpr(dest, consts, items, out error))
            {
                error ??= $"adc0 to {dest}";
                return false;
            }
            return true;
        }

        var vptrLoad = VramPtrIndexLoad.Match(line);
        if (vptrLoad.Success)
        {
            // (zp),Y only — move index into Y first when needed.
            string idx = vptrLoad.Groups[2].Value;
            string dest = vptrLoad.Groups[1].Value;
            if (idx == "x")
            {
                items.Add(new AsmItem(Asm.TXA()));
                items.Add(new AsmItem(Asm.TAY()));
            }
            items.Add(new AsmItem(Asm.LDA_ind_Y(0x00)));
            if (dest == "x")
                items.Add(new AsmItem(Asm.TAX()));
            else if (dest == "y")
                items.Add(new AsmItem(Asm.TAY()));
            return true;
        }

        var vptrStore = VramPtrIndexStore.Match(line);
        if (vptrStore.Success)
        {
            string idx = vptrStore.Groups[1].Value;
            string val = vptrStore.Groups[2].Value.Trim();
            if (idx == "x")
            {
                items.Add(new AsmItem(Asm.TXA()));
                items.Add(new AsmItem(Asm.TAY()));
            }
            if (val is "a")
            { /* A ready */ }
            else if (val is "x")
                items.Add(new AsmItem(Asm.TXA()));
            else if (val is "y")
                items.Add(new AsmItem(Asm.TYA()));
            else if (TryParseByteLiteral(val, out byte immPtr))
            {
                if (!LastLoadsAWithImm(items, immPtr))
                    items.Add(new AsmItem(Asm.LDA(immPtr)));
            }
            else
            {
                error = $"vram_ptr store {val}";
                return false;
            }
            items.Add(new AsmItem(Asm.STA_ind_Y(0x00)));
            return true;
        }

        var incMem = IncPeekPoke.Match(line);
        if (incMem.Success)
        {
            if (!TryResolveAddress(incMem.Groups[1].Value, consts, out var ea))
            {
                error = $"inc addr {incMem.Groups[1].Value}";
                return false;
            }
            items.Add(new AsmItem(EmitInc(ea)));
            return true;
        }

        var incVar = MemVarInc.Match(line);
        if (incVar.Success && consts.ContainsKey(incVar.Groups[1].Value))
        {
            if (!TryResolveAddress(incVar.Groups[1].Value, consts, out var ea))
            {
                error = $"inc var {incVar.Groups[1].Value}";
                return false;
            }
            items.Add(new AsmItem(EmitInc(ea)));
            return true;
        }

        var decVar = MemVarDec.Match(line);
        if (decVar.Success && consts.ContainsKey(decVar.Groups[1].Value))
        {
            if (!TryResolveAddress(decVar.Groups[1].Value, consts, out var ea))
            {
                error = $"dec var {decVar.Groups[1].Value}";
                return false;
            }
            items.Add(new AsmItem(EmitDec(ea)));
            return true;
        }

        var incDecOp = MemVarIncDecOp.Match(line);
        if (incDecOp.Success && consts.ContainsKey(incDecOp.Groups[1].Value))
        {
            if (!TryResolveAddress(incDecOp.Groups[1].Value, consts, out var ea))
            {
                error = $"inc/dec var {incDecOp.Groups[1].Value}";
                return false;
            }
            items.Add(new AsmItem(incDecOp.Groups[2].Value == "++" ? EmitInc(ea) : EmitDec(ea)));
            return true;
        }

        var peek = PeekAssign.Match(line);
        if (peek.Success)
        {
            if (!TryResolveAddress(peek.Groups[2].Value, consts, out var ea))
            {
                error = $"peek addr {peek.Groups[2].Value}";
                return false;
            }
            string reg = peek.Groups[1].Value;
            items.Add(new AsmItem(EmitLoad(reg, ea)));
            return true;
        }

        var memPeek = MemPeekAssign.Match(line);
        if (memPeek.Success
            && memPeek.Groups[1].Value is not ("a" or "carry" or "zero" or "negative"))
        {
            if (!TryResolveAddress(memPeek.Groups[2].Value, consts, out var peekEa))
            {
                error = $"mem peek addr {memPeek.Groups[2].Value}";
                return false;
            }
            string dest = memPeek.Groups[1].Value;
            if (dest is "x" or "y")
            {
                items.Add(new AsmItem(EmitLoad(dest, peekEa)));
                return true;
            }
            items.Add(new AsmItem(EmitLoad("a", peekEa)));
            if (!TryEmitStoreAToMemExpr(dest, consts, items, out error))
            {
                error ??= $"mem peek store {dest}";
                return false;
            }
            return true;
        }

        var bufLoad = BufIndexLoad.Match(line);
        if (bufLoad.Success && consts.ContainsKey(bufLoad.Groups[2].Value))
        {
            if (!TryResolveBufferIndex(
                    bufLoad.Groups[2].Value,
                    bufLoad.Groups[3].Value,
                    bufLoad.Groups[4].Value,
                    bufLoad.Groups[5].Value,
                    consts,
                    out var ea))
            {
                error = $"buf load {bufLoad.Groups[2].Value}";
                return false;
            }
            items.Add(new AsmItem(EmitLoad(bufLoad.Groups[1].Value, ea)));
            return true;
        }

        var memLoad = MemVarLoad.Match(line);
        if (memLoad.Success && consts.ContainsKey(memLoad.Groups[2].Value))
        {
            if (!TryResolveAddress(memLoad.Groups[2].Value, consts, out var ea))
            {
                error = $"load var {memLoad.Groups[2].Value}";
                return false;
            }
            items.Add(new AsmItem(EmitLoad(memLoad.Groups[1].Value, ea)));
            return true;
        }

        var poke = PokeCall.Match(line);
        if (poke.Success)
        {
            if (!TryResolveAddress(poke.Groups[1].Value, consts, out var ea))
            {
                error = $"poke addr {poke.Groups[1].Value}";
                return false;
            }
            string val = poke.Groups[2].Value.Trim();
            if (val is "a" or "x" or "y")
            {
                items.Add(new AsmItem(EmitStore(val, ea)));
                return true;
            }
            if (TryParseByteLiteral(val, out byte immV))
            {
                // LDA #imm ; STA a ; STA b  — reuse A when the previous load was the same imm
                // (SMB reset: LDA #$A5 / STA $07FF / STA $07A7).
                if (!LastLoadsAWithImm(items, immV))
                    items.Add(new AsmItem(Asm.LDA(immV)));
                items.Add(new AsmItem(EmitStore("a", ea)));
                return true;
            }
            // Raised: poke(addr, peek(...)) / poke(addr, mem)
            if (val.StartsWith("peek(", StringComparison.Ordinal) && val.EndsWith(")", StringComparison.Ordinal))
            {
                string peekAddr = val.Substring("peek(".Length, val.Length - "peek(".Length - 1).Trim();
                if (!TryResolveAddress(peekAddr, consts, out var srcEa))
                {
                    error = $"poke peek src {val}";
                    return false;
                }
                items.Add(new AsmItem(EmitLoad("a", srcEa)));
                items.Add(new AsmItem(EmitStore("a", ea)));
                return true;
            }
            if (TryEmitByteExprIntoA(val, consts, items, out error)
                || TryEmitLoadAFromMemExpr(val, consts, items, out error))
            {
                items.Add(new AsmItem(EmitStore("a", ea)));
                return true;
            }
            error ??= $"poke value {val}";
            return false;
        }

        var bufStore = BufIndexStore.Match(line);
        if (bufStore.Success && consts.ContainsKey(bufStore.Groups[1].Value))
        {
            if (!TryResolveBufferIndex(
                    bufStore.Groups[1].Value,
                    bufStore.Groups[2].Value,
                    bufStore.Groups[3].Value,
                    bufStore.Groups[4].Value,
                    consts,
                    out var bufEa))
            {
                error = $"buf store {bufStore.Groups[1].Value}";
                return false;
            }
            string bufVal = bufStore.Groups[5].Value.Trim();
            if (bufVal is "a" or "x" or "y")
            {
                items.Add(new AsmItem(EmitStore(bufVal, bufEa)));
                return true;
            }
            if (TryParseByteLiteral(bufVal, out byte immBuf))
            {
                if (!LastLoadsAWithImm(items, immBuf))
                    items.Add(new AsmItem(Asm.LDA(immBuf)));
                items.Add(new AsmItem(EmitStore("a", bufEa)));
                return true;
            }
            // Mem→mem via A: dest[i] = src  or  dest[i] = src[j]
            if (TryEmitByteExprIntoA(bufVal, consts, items, out error)
                || TryEmitLoadAFromMemExpr(bufVal, consts, items, out error))
            {
                items.Add(new AsmItem(EmitStore("a", bufEa)));
                return true;
            }
            error ??= $"buf store value {bufVal}";
            return false;
        }

        var bufInc = BufIndexIncDec.Match(line);
        if (bufInc.Success && consts.ContainsKey(bufInc.Groups[1].Value))
        {
            if (!TryResolveBufferIndex(
                    bufInc.Groups[1].Value,
                    bufInc.Groups[2].Value,
                    bufInc.Groups[3].Value,
                    bufInc.Groups[4].Value,
                    consts,
                    out var incEa))
            {
                error = $"buf inc {bufInc.Groups[1].Value}";
                return false;
            }
            items.Add(new AsmItem(bufInc.Groups[5].Value == "++" ? EmitInc(incEa) : EmitDec(incEa)));
            return true;
        }

        var memStore = MemVarStore.Match(line);
        if (memStore.Success
            && memStore.Groups[1].Value is not ("a" or "x" or "y" or "carry" or "zero" or "negative")
            && consts.ContainsKey(memStore.Groups[1].Value)
            && TryResolveAddress(memStore.Groups[1].Value, consts, out var storeEa))
        {
            string val = memStore.Groups[2].Value.Trim();
            if (val is "a" or "x" or "y")
            {
                items.Add(new AsmItem(EmitStore(val, storeEa)));
                return true;
            }
            if (TryParseByteLiteral(val, out byte immStore))
            {
                if (!LastLoadsAWithImm(items, immStore))
                    items.Add(new AsmItem(Asm.LDA(immStore)));
                items.Add(new AsmItem(EmitStore("a", storeEa)));
                return true;
            }
            if (TryEmitByteExprIntoA(val, consts, items, out error))
            {
                items.Add(new AsmItem(EmitStore("a", storeEa)));
                return true;
            }
            string? byteErr = error;
            if (TryEmitLoadAFromMemExpr(val, consts, items, out error))
            {
                items.Add(new AsmItem(EmitStore("a", storeEa)));
                return true;
            }
            // Claimed mem store but couldn't emit value — don't fall through as "unsupported" blur.
            error = byteErr ?? error ?? $"mem store value {val}";
            return false;
        }

        var callRet = CallReturn.Match(line);
        if (callRet.Success)
        {
            string name = callRet.Groups[1].Value;
            if (name is "sei" or "cli" or "cld" or "sed" or "txs" or "tsx" or "php" or "plp" or "waitvsync")
                return TryLowerLine(name + "();", consts, nameToAddress, tables, ref waitSeq, items, out error, ref padResultOnStack);
            if (!nameToAddress.TryGetValue(name, out ushort target))
            {
                error = $"unknown call target {name}";
                return false;
            }
            items.Add(new AsmItem(Asm.JMP(target)));
            return true;
        }

        var call = CallOnly.Match(line);
        if (call.Success)
        {
            string name = call.Groups[1].Value;
            if (name is "sei" or "cli" or "cld" or "sed" or "txs" or "tsx" or "php" or "plp" or "waitvsync"
                or "BindVramFromZp")
                return TryLowerLine(name + "();", consts, nameToAddress, tables, ref waitSeq, items, out error, ref padResultOnStack);
            if (!nameToAddress.TryGetValue(name, out ushort target))
            {
                error = $"unknown call {name}";
                return false;
            }
            items.Add(new AsmItem(Asm.JSR(target)));
            return true;
        }

        // Raised SoftCpu ABI: Name(expr); return; → load expr into A; JMP Name (tail)
        var callArgRet = Regex.Match(line, @"^([A-Za-z_]\w*)\((.+)\)\s*;\s*return\s*;\s*$");
        if (callArgRet.Success)
        {
            string name = callArgRet.Groups[1].Value;
            string arg = callArgRet.Groups[2].Value.Trim();
            if (arg.Length > 0 && !arg.Contains(',')
                && name is not ("poke" or "peek" or "ppu_ctrl" or "ppu_mask_write" or "ppu_scroll"
                    or "vram_put" or "vram_adr" or "oam_dma" or "pad_poll" or "pad_poll_msb"
                    or "sei" or "cli" or "cld" or "sed" or "pha" or "pla" or "push" or "pop")
                && !name.StartsWith("apu_", StringComparison.Ordinal)
                && nameToAddress.TryGetValue(name, out ushort jmpTarget))
            {
                if (!TryEmitByteExprIntoA(arg, consts, items, out error)
                    && !TryEmitLoadAFromMemExpr(arg, consts, items, out error))
                {
                    error ??= $"call arg {arg}";
                    return false;
                }
                items.Add(new AsmItem(Asm.JMP(jmpTarget)));
                return true;
            }
        }

        // Raised SoftCpu ABI: Name(expr); → load expr into A; JSR Name
        // (single arg only; multi-arg / neslib helpers handled above).
        var callArg = Regex.Match(line, @"^([A-Za-z_]\w*)\((.+)\)\s*;\s*$");
        if (callArg.Success)
        {
            string name = callArg.Groups[1].Value;
            string arg = callArg.Groups[2].Value.Trim();
            if (arg.Length > 0 && !arg.Contains(',')
                && name is not ("poke" or "peek" or "ppu_ctrl" or "ppu_mask_write" or "ppu_scroll"
                    or "vram_put" or "vram_adr" or "oam_dma" or "pad_poll" or "pad_poll_msb"
                    or "sei" or "cli" or "cld" or "sed" or "pha" or "pla" or "push" or "pop")
                && !name.StartsWith("apu_", StringComparison.Ordinal)
                && nameToAddress.TryGetValue(name, out ushort callTarget))
            {
                if (!TryEmitByteExprIntoA(arg, consts, items, out error)
                    && !TryEmitLoadAFromMemExpr(arg, consts, items, out error))
                {
                    error ??= $"call arg {arg}";
                    return false;
                }
                items.Add(new AsmItem(Asm.JSR(callTarget)));
                return true;
            }
        }

        error = $"unsupported: {line}";
        return false;
    }

    static Instruction EmitAluOp(string op, byte v) => op switch
    {
        "|" => Asm.ORA(v),
        "&" => Asm.AND(v),
        "^" => Asm.EOR(v),
        _ => throw new InvalidOperationException($"alu op {op}"),
    };

    /// <summary>
    /// <c>if (flag) { then } else { else }</c> or <c>if (flag) { body }</c> / one-liner forms.
    /// Emits the classic diamond: branch-if-NOT-cond to else, then-arm, JMP join, else-arm.
    /// </summary>
    /// <summary>
    /// <c>for (x = 0; x != N; x++) { … }</c> / <c>for (y = N; y != 0; y--) ;</c>
    /// Lowered as init → loop: body → step → branch-if-cond (same shape as the do/while we sugared from).
    /// </summary>
    static bool TryConsumeFor(
        IReadOnlyList<string> body,
        ref int i,
        Dictionary<string, ushort> consts,
        Dictionary<string, ushort> nameToAddress,
        SoftCpuTables tables,
        ref int waitSeq,
        List<AsmItem> items,
        out string? error,
        ref bool padResultOnStack)
    {
        error = null;
        string line = body[i].Trim();

        // One-liner: for (init; cond; step) ;
        var oneEmpty = Regex.Match(line,
            @"^for\s*\(\s*(.*?)\s*;\s*(.+?)\s*;\s*(.*?)\s*\)\s*;\s*$");
        if (oneEmpty.Success)
        {
            if (!TryLowerForParts(
                    oneEmpty.Groups[1].Value.Trim(),
                    oneEmpty.Groups[2].Value.Trim(),
                    oneEmpty.Groups[3].Value.Trim(),
                    Array.Empty<string>(),
                    consts, nameToAddress, tables, ref waitSeq, items, out error, ref padResultOnStack))
                return true;
            return true;
        }

        // Header only: for (...) \n { ... }
        var hdr = Regex.Match(line,
            @"^for\s*\(\s*(.*?)\s*;\s*(.+?)\s*;\s*(.*?)\s*\)\s*$");
        if (!hdr.Success)
            return false;
        if (i + 1 >= body.Count || body[i + 1].Trim() != "{")
        {
            error = "for: missing block";
            return true;
        }
        if (!TryExtractBraceBlock(body, i + 1, out int blockEnd, out List<string> arm))
        {
            error = "for: bad block";
            return true;
        }
        if (!TryLowerForParts(
                hdr.Groups[1].Value.Trim(),
                hdr.Groups[2].Value.Trim(),
                hdr.Groups[3].Value.Trim(),
                arm,
                consts, nameToAddress, tables, ref waitSeq, items, out error, ref padResultOnStack))
        {
            // blockEnd is first index after `}`; main loop will i++.
            i = blockEnd - 1;
            return true;
        }
        i = blockEnd - 1;
        return true;
    }

    static bool TryLowerForParts(
        string init,
        string cond,
        string step,
        IReadOnlyList<string> arm,
        Dictionary<string, ushort> consts,
        Dictionary<string, ushort> nameToAddress,
        SoftCpuTables tables,
        ref int waitSeq,
        List<AsmItem> items,
        out string? error,
        ref bool padResultOnStack)
    {
        error = null;
        if (init.Length > 0)
        {
            string initStmt = init.EndsWith(";", StringComparison.Ordinal) ? init : init + ";";
            if (!TryLowerLine(initStmt, consts, nameToAddress, tables, ref waitSeq, items, out error, ref padResultOnStack))
                return false;
        }

        var armList = arm as List<string> ?? new List<string>(arm);

        // Countdown peephole: for (y = N; y != 0; y--) → body; DEY; BNE (matches original DEX/DEY loops).
        var cdInit = Regex.Match(init, @"^(x|y)\s*=\s*(0x[0-9A-Fa-f]+|\d+)$");
        var cdCond = Regex.Match(cond, @"^(x|y)\s*!=\s*0$");
        var cdStep = Regex.Match(step, @"^(x|y)--$");
        if (cdInit.Success && cdCond.Success && cdStep.Success
            && cdInit.Groups[1].Value == cdCond.Groups[1].Value
            && cdCond.Groups[1].Value == cdStep.Groups[1].Value
            && TryParseByteLiteral(cdInit.Groups[2].Value, out byte n) && n != 0)
        {
            string reg = cdInit.Groups[1].Value;
            string loopTop = $"__for_{waitSeq++}";
            items.Add(new AsmItem(loopTop));
            if (!TryLowerArm(armList, consts, nameToAddress, tables, ref waitSeq, items, out error, ref padResultOnStack))
                return false;
            items.Add(new AsmItem(reg == "x" ? Asm.DEX() : Asm.DEY()));
            items.Add(new AsmItem(Asm.BNE(loopTop)));
            return true;
        }

        // Mem countdown: for (zp = N; zp != 0; zp--) → body; DEC; BNE
        var cdMemInit = Regex.Match(init, @"^([A-Za-z_]\w*)\s*=\s*(0x[0-9A-Fa-f]+|\d+)$");
        var cdMemCond = Regex.Match(cond, @"^([A-Za-z_]\w*)\s*!=\s*0$");
        var cdMemStep = Regex.Match(step, @"^([A-Za-z_]\w*)--$");
        if (cdMemInit.Success && cdMemCond.Success && cdMemStep.Success
            && cdMemInit.Groups[1].Value == cdMemCond.Groups[1].Value
            && cdMemCond.Groups[1].Value == cdMemStep.Groups[1].Value
            && cdMemInit.Groups[1].Value is not ("a" or "x" or "y")
            && TryParseByteLiteral(cdMemInit.Groups[2].Value, out byte nMem) && nMem != 0
            && TryResolveAddress(cdMemInit.Groups[1].Value, consts, out var memEa))
        {
            string loopTop = $"__for_{waitSeq++}";
            items.Add(new AsmItem(loopTop));
            if (!TryLowerArm(armList, consts, nameToAddress, tables, ref waitSeq, items, out error, ref padResultOnStack))
                return false;
            items.Add(new AsmItem(EmitDec(memEa)));
            items.Add(new AsmItem(Asm.BNE(loopTop)));
            return true;
        }

        string loopTop2 = $"__for_{waitSeq++}";
        items.Add(new AsmItem(loopTop2));
        if (!TryLowerArm(armList, consts, nameToAddress, tables, ref waitSeq, items, out error, ref padResultOnStack))
            return false;
        if (step.Length > 0)
        {
            string stepStmt = step.EndsWith(";", StringComparison.Ordinal) ? step : step + ";";
            if (!TryLowerLine(stepStmt, consts, nameToAddress, tables, ref waitSeq, items, out error, ref padResultOnStack))
                return false;
        }
        if (!TryEmitDoWhileBranch(cond, loopTop2, items, ref waitSeq, consts, out error))
            return false;
        return true;
    }

    /// <summary>
    /// <c>do { … } while (--x != 0);</c> / <c>while (!zero);</c> / <c>while (!negative);</c>
    /// </summary>
    static bool TryConsumeDoWhile(
        IReadOnlyList<string> body,
        ref int i,
        Dictionary<string, ushort> consts,
        Dictionary<string, ushort> nameToAddress,
        SoftCpuTables tables,
        ref int waitSeq,
        List<AsmItem> items,
        out string? error,
        ref bool padResultOnStack)
    {
        error = null;
        string line = body[i].Trim();

        // One-liner: do { stmt; } while (cond);  or  do { } while (cond);
        var one = Regex.Match(line,
            @"^do\s*\{\s*(.*?)\s*\}\s*while\s*\(\s*(.+)\s*\)\s*;\s*$");
        if (one.Success)
        {
            string top = $"__dow_{waitSeq}";
            string cond = one.Groups[2].Value.Trim();
            items.Add(new AsmItem(top));
            string bodyStmt = one.Groups[1].Value.Trim();
            if (!string.IsNullOrEmpty(bodyStmt))
            {
                string stmt = bodyStmt.TrimEnd(';') + ";";
                if (!TryLowerLine(stmt, consts, nameToAddress, tables, ref waitSeq, items, out error, ref padResultOnStack))
                    return true;
            }
            if (!TryEmitDoWhileBranch(cond, top, items, ref waitSeq, consts, out error))
                return true;
            waitSeq++;
            return true;
        }

        if (line != "do")
            return false;
        if (i + 1 >= body.Count || body[i + 1].Trim() != "{")
            return false;
        if (!TryExtractBraceBlock(body, i + 1, out int blockEnd, out List<string> arm))
        {
            error = "do: bad block";
            return true;
        }
        if (blockEnd >= body.Count)
        {
            error = "do: missing while";
            return true;
        }
        var whileLine = Regex.Match(body[blockEnd].Trim(),
            @"^while\s*\(\s*(.+)\s*\)\s*;\s*$");
        if (!whileLine.Success)
        {
            error = "do: missing while";
            return true;
        }

        string loopTop = $"__dow_{waitSeq}";
        items.Add(new AsmItem(loopTop));
        if (!TryLowerArm(arm, consts, nameToAddress, tables, ref waitSeq, items, out error, ref padResultOnStack))
        {
            i = blockEnd;
            return true;
        }
        if (!TryEmitDoWhileBranch(whileLine.Groups[1].Value.Trim(), loopTop, items, ref waitSeq, consts, out error))
        {
            i = blockEnd;
            return true;
        }
        waitSeq++;
        i = blockEnd;
        return true;
    }

    /// <summary>
    /// <c>name--; if (name != 0) goto L;</c> → DEC; BNE (Z from DEC).
    /// </summary>
    static bool TryConsumeMemDecBranch(
        IReadOnlyList<string> body,
        ref int i,
        Dictionary<string, ushort> consts,
        List<AsmItem> items,
        out string? error)
    {
        error = null;
        var dec = MemVarIncDecOp.Match(body[i].Trim());
        if (!dec.Success || dec.Groups[2].Value != "--")
            return false;
        string name = dec.Groups[1].Value;
        if (name is "a" or "x" or "y" || !consts.ContainsKey(name))
            return false;
        if (i + 1 >= body.Count)
            return false;

        string next = body[i + 1].Trim();
        var g = Regex.Match(next,
            @"^if\s*\(\s*" + Regex.Escape(name) + @"\s*(==|!=)\s*0\s*\)\s*goto\s+(L_[0-9A-Fa-f]+)\s*;\s*$");
        if (!g.Success)
        {
            var r = Regex.Match(next,
                @"^if\s*\(\s*" + Regex.Escape(name) + @"\s*(==|!=)\s*0\s*\)\s*return\s*;\s*$");
            if (!r.Success)
                return false;
            if (!TryResolveAddress(name, consts, out var eaR))
            {
                error = $"dec-branch {name}";
                return true;
            }
            items.Add(new AsmItem(EmitDec(eaR)));
            string skip = $"__retskip_{items.Count}";
            // if (name != 0) return → BNE to RTS; if (name == 0) return → BEQ to RTS
            items.Add(new AsmItem(r.Groups[1].Value == "!=" ? Asm.BEQ(skip) : Asm.BNE(skip)));
            items.Add(new AsmItem(Asm.RTS()));
            items.Add(new AsmItem(skip));
            i++;
            return true;
        }

        if (!TryResolveAddress(name, consts, out var ea))
        {
            error = $"dec-branch {name}";
            return true;
        }
        items.Add(new AsmItem(EmitDec(ea)));
        items.Add(new AsmItem(g.Groups[1].Value == "!=" ? Asm.BNE(g.Groups[2].Value) : Asm.BEQ(g.Groups[2].Value)));
        i++;
        return true;
    }

    static bool TryEmitDoWhileBranch(
        string cond, string loopTop, List<AsmItem> items, ref int waitSeq,
        Dictionary<string, ushort> consts, out string? error)
    {
        error = null;
        // while (--x != 0) / while (--y != 0)
        var dec = Regex.Match(cond, @"^--(x|y)\s*!=\s*0$");
        if (dec.Success)
        {
            string reg = dec.Groups[1].Value;
            items.Add(new AsmItem(reg == "x" ? Asm.DEX() : Asm.DEY()));
            items.Add(new AsmItem(Asm.BNE(loopTop)));
            return true;
        }

        // while (--zp != 0) → DEC zp; BNE
        var memDec = Regex.Match(cond, @"^--([A-Za-z_]\w*)\s*!=\s*0$");
        if (memDec.Success
            && memDec.Groups[1].Value is not ("a" or "x" or "y")
            && TryResolveAddress(memDec.Groups[1].Value, consts, out var memEa))
        {
            items.Add(new AsmItem(EmitDec(memEa)));
            items.Add(new AsmItem(Asm.BNE(loopTop)));
            return true;
        }

        // while (x < N) / while (y >= N) / while (a != 0) …
        var cmp = Regex.Match(cond, @"^(a|x|y)\s*(==|!=|>=|<)\s*(0x[0-9A-Fa-f]+|\d+)$");
        if (cmp.Success)
        {
            if (!TryEmitRegCompareBranch(
                    cmp.Groups[1].Value, cmp.Groups[2].Value, cmp.Groups[3].Value,
                    loopTop, items, out error, invertBranch: false))
                return false;
            return true;
        }

        // while (!zero) / while (zero) / while (!carry) / …
        var flag = Regex.Match(cond, @"^(!)?(zero|carry|negative)$");
        if (flag.Success)
        {
            bool neg = flag.Groups[1].Success;
            string f = flag.Groups[2].Value;
            // Branch back to top when condition is TRUE
            Instruction br = (f, neg) switch
            {
                ("zero", false) => Asm.BEQ(loopTop),
                ("zero", true) => Asm.BNE(loopTop),
                ("carry", false) => Asm.BCS(loopTop),
                ("carry", true) => Asm.BCC(loopTop),
                ("negative", false) => Asm.BMI(loopTop),
                ("negative", true) => Asm.BPL(loopTop),
                _ => throw new InvalidOperationException(cond),
            };
            items.Add(new AsmItem(br));
            return true;
        }

        error = $"do/while cond {cond}";
        return false;
    }

    static bool TryConsumeRegCompareIfElse(
        IReadOnlyList<string> body,
        ref int i,
        Match regHdr,
        Dictionary<string, ushort> consts,
        Dictionary<string, ushort> nameToAddress,
        SoftCpuTables tables,
        ref int waitSeq,
        List<AsmItem> items,
        out string? error,
        ref bool padResultOnStack)
    {
        error = null;
        if (!TryExtractBraceBlock(body, i + 1, out int thenEnd, out List<string> thenArm))
        {
            error = "if: bad then block";
            return true;
        }
        List<string>? elseArm = null;
        int cursor = thenEnd;
        if (cursor < body.Count && body[cursor].Trim() == "else")
        {
            if (cursor + 1 >= body.Count || body[cursor + 1].Trim() != "{")
            {
                error = "if: bad else block";
                return true;
            }
            if (!TryExtractBraceBlock(body, cursor + 1, out int elseEnd, out elseArm))
            {
                error = "if: bad else block";
                return true;
            }
            cursor = elseEnd;
        }

        string elseLab = $"__else_{waitSeq}";
        string joinLab = $"__join_{waitSeq++}";
        string lhs = regHdr.Groups[1].Value;
        string op = regHdr.Groups[2].Value;
        string imm = regHdr.Groups[3].Value;

        if (elseArm is null)
        {
            if (!TryEmitExprCompareBranch(lhs, op, imm, joinLab, consts, items, out error, invertBranch: true)
                || !TryLowerArm(thenArm, consts, nameToAddress, tables, ref waitSeq, items, out error, ref padResultOnStack))
            {
                i = cursor - 1;
                return true;
            }
            items.Add(new AsmItem(joinLab));
        }
        else
        {
            if (!TryEmitExprCompareBranch(lhs, op, imm, elseLab, consts, items, out error, invertBranch: true)
                || !TryLowerArm(thenArm, consts, nameToAddress, tables, ref waitSeq, items, out error, ref padResultOnStack))
            {
                i = cursor - 1;
                return true;
            }
            items.Add(new AsmItem(Asm.JMP(joinLab)));
            items.Add(new AsmItem(elseLab));
            if (!TryLowerArm(elseArm, consts, nameToAddress, tables, ref waitSeq, items, out error, ref padResultOnStack))
            {
                i = cursor - 1;
                return true;
            }
            items.Add(new AsmItem(joinLab));
        }
        i = cursor - 1;
        return true;
    }

    static bool TryEmitRegCompareBranch(
        string reg,
        string op,
        string immText,
        string label,
        List<AsmItem> items,
        out string? error,
        bool invertBranch = false)
    {
        error = null;
        byte imm = ParseByte(immText);
        items.Add(new AsmItem(reg switch
        {
            "a" => Asm.CMP(imm),
            "x" => Asm.CPX(imm),
            "y" => Asm.CPY(imm),
            _ => throw new InvalidOperationException(reg),
        }));
        EmitCompareBranch(op, label, items, invertBranch);
        return true;
    }

    /// <summary>
    /// Compare raised SoftCpu surface (<c>mem</c>/<c>mem[i]</c>/<c>a|x|y</c>) to an imm and branch.
    /// Mem forms: LDA; [CMP #]; Bxx — leaves A holding the loaded value.
    /// </summary>
    static bool TryEmitExprCompareBranch(
        string lhs,
        string op,
        string immText,
        string label,
        Dictionary<string, ushort> consts,
        List<AsmItem> items,
        out string? error,
        bool invertBranch = false)
    {
        error = null;
        lhs = lhs.Trim();
        if (lhs is "a" or "x" or "y")
            return TryEmitRegCompareBranch(lhs, op, immText, label, items, out error, invertBranch);

        if (!TryEmitLoadAFromMemExpr(lhs, consts, items, out error))
        {
            error ??= $"compare load {lhs}";
            return false;
        }

        if (!TryParseByteLiteral(immText, out byte imm))
        {
            error = $"compare imm {immText}";
            return false;
        }

        // LDA already sets Z/N; ==0 / !=0 need no CMP.
        if (imm == 0 && op is "==" or "!=")
        {
            // invertBranch true → branch when condition FALSE
            items.Add(new AsmItem(
                (op, invertBranch) switch
                {
                    ("==", false) => Asm.BEQ(label),
                    ("==", true) => Asm.BNE(label),
                    ("!=", false) => Asm.BNE(label),
                    ("!=", true) => Asm.BEQ(label),
                    _ => Asm.BNE(label),
                }));
            return true;
        }

        items.Add(new AsmItem(Asm.CMP(imm)));
        EmitCompareBranch(op, label, items, invertBranch);
        return true;
    }

    /// <summary>
    /// <c>if ((a &amp; MASK) != 0)</c> → AND #MASK / BNE (destroys A — only for discarded-A sugar).
    /// </summary>
    static bool TryEmitBitTestBranch(
        string maskText,
        string op,
        string label,
        List<AsmItem> items,
        out string? error,
        bool invertBranch = false)
    {
        error = null;
        if (!TryParseByteLiteral(maskText, out byte mask) && !TryEvalEnumByte(maskText, out mask))
        {
            error = $"bit mask {maskText}";
            return false;
        }
        items.Add(new AsmItem(Asm.AND(mask)));
        // != 0 → BNE; == 0 → BEQ. invertBranch flips for "skip then".
        string effective = invertBranch
            ? op switch { "==" => "!=", "!=" => "==", _ => op }
            : op;
        items.Add(new AsmItem(effective switch
        {
            "==" => Asm.BEQ(label),
            "!=" => Asm.BNE(label),
            _ => throw new InvalidOperationException(op),
        }));
        return true;
    }

    /// <summary><c>if ((src &amp; mask) != 0)</c> → LDA src; AND #; Bxx.</summary>
    static bool TryEmitMemBitTestBranch(
        string src,
        string maskText,
        string op,
        string label,
        Dictionary<string, ushort> consts,
        List<AsmItem> items,
        out string? error,
        bool invertBranch = false)
        => TryEmitMemMaskedCompareBranch(src, maskText, op, "0", label, consts, items, out error, invertBranch);

    /// <summary><c>if ((src &amp; mask) op imm)</c> → LDA; AND #; [CMP #]; Bxx.</summary>
    static bool TryEmitMemMaskedCompareBranch(
        string src,
        string maskText,
        string op,
        string rhsText,
        string label,
        Dictionary<string, ushort> consts,
        List<AsmItem> items,
        out string? error,
        bool invertBranch = false)
    {
        error = null;
        src = src.Trim();
        if (src == "x")
            items.Add(new AsmItem(Asm.TXA()));
        else if (src == "y")
            items.Add(new AsmItem(Asm.TYA()));
        else if (!TryEmitLoadAFromMemExpr(src, consts, items, out error))
        {
            error ??= $"masked-compare load {src}";
            return false;
        }

        if (!TryParseByteLiteral(maskText, out byte mask) && !TryEvalEnumByte(maskText, out mask))
        {
            error = $"masked-compare mask {maskText}";
            return false;
        }
        items.Add(new AsmItem(Asm.AND(mask)));

        if (!TryParseByteLiteral(rhsText, out byte rhs))
        {
            error = $"masked-compare rhs {rhsText}";
            return false;
        }

        // A already holds (src & mask). Z/N from AND — ==0/!=0 need no CMP.
        if (rhs == 0 && op is "==" or "!=")
        {
            items.Add(new AsmItem(
                (op, invertBranch) switch
                {
                    ("==", false) => Asm.BEQ(label),
                    ("==", true) => Asm.BNE(label),
                    ("!=", false) => Asm.BNE(label),
                    ("!=", true) => Asm.BEQ(label),
                    _ => Asm.BNE(label),
                }));
            return true;
        }

        items.Add(new AsmItem(Asm.CMP(rhs)));
        EmitCompareBranch(op, label, items, invertBranch);
        return true;
    }

    static bool TryEmitRegMemCompareBranch(
        string reg,
        string op,
        string addrExpr,
        string label,
        Dictionary<string, ushort> consts,
        List<AsmItem> items,
        out string? error,
        bool invertBranch = false)
    {
        error = null;
        if (reg is not "a")
        {
            error = $"mem compare only supported for a, got {reg}";
            return false;
        }
        string addr = addrExpr.Trim();
        if (addr.StartsWith("peek(", StringComparison.Ordinal) && addr.EndsWith(")", StringComparison.Ordinal))
            addr = addr.Substring("peek(".Length, addr.Length - "peek(".Length - 1).Trim();
        if (!TryResolveAddress(addr, consts, out var ea))
        {
            error = $"mem compare addr {addrExpr}";
            return false;
        }
        items.Add(new AsmItem(EmitCmp(ea)));
        EmitCompareBranch(op, label, items, invertBranch);
        return true;
    }

    static void EmitCompareBranch(string op, string label, List<AsmItem> items, bool invertBranch)
    {
        string effective = invertBranch
            ? op switch { "==" => "!=", "!=" => "==", ">=" => "<", "<" => ">=", _ => op }
            : op;
        Instruction br = effective switch
        {
            "==" => Asm.BEQ(label),
            "!=" => Asm.BNE(label),
            ">=" => Asm.BCS(label),
            "<" => Asm.BCC(label),
            _ => throw new InvalidOperationException($"compare op {op}"),
        };
        items.Add(new AsmItem(br));
    }

    static Instruction EmitCmp(EffectiveAddress ea) => ea.Mode switch
    {
        SoftAddrMode.ZeroPage => Asm.CMP_zpg((byte)ea.Address),
        SoftAddrMode.ZeroPageX => Asm.CMP_zpg_X((byte)ea.Address),
        SoftAddrMode.Absolute => new Instruction(Opcode.CMP, AddressMode.Absolute, new AbsoluteOperand(ea.Address)),
        SoftAddrMode.AbsoluteX => new Instruction(Opcode.CMP, AddressMode.AbsoluteX, new AbsoluteOperand(ea.Address)),
        SoftAddrMode.AbsoluteY => Asm.CMP_abs_Y(ea.Address),
        _ => throw new InvalidOperationException($"cmp {ea.Mode}"),
    };

    static bool TryConsumeIfElse(
        IReadOnlyList<string> body,
        ref int i,
        Dictionary<string, ushort> consts,
        Dictionary<string, ushort> nameToAddress,
        SoftCpuTables tables,
        ref int waitSeq,
        List<AsmItem> items,
        out string? error,
        ref bool padResultOnStack)
    {
        error = null;
        string line = body[i].Trim();

        // One-liner: if (cond) { stmt; }
        var oneLine = Regex.Match(line,
            @"^if\s*\(\s*(!)?\s*(carry|zero|negative)\s*\)\s*\{\s*(.+)\s*\}\s*$");
        if (oneLine.Success)
        {
            bool neg = oneLine.Groups[1].Success;
            string flag = oneLine.Groups[2].Value;
            string stmt = oneLine.Groups[3].Value.Trim().TrimEnd(';') + ";";
            string end = $"__ifend_{waitSeq++}";
            items.Add(new AsmItem(BranchIfNot(flag, neg, end)));
            if (!TryLowerLine(stmt, consts, nameToAddress, tables, ref waitSeq, items, out error, ref padResultOnStack))
                return true; // consumed shape; error set
            items.Add(new AsmItem(end));
            return true;
        }

        // One-liner: if (a|x|y|mem|mem[i] == N) { stmt; }
        var oneLineReg = Regex.Match(line,
            @"^if\s*\(\s*(a|x|y|[A-Za-z_]\w*(?:\[[^\]]+\])?)\s*(==|!=|>=|<)\s*(0x[0-9A-Fa-f]+|\d+)\s*\)\s*\{\s*(.+)\s*\}\s*$");
        if (oneLineReg.Success)
        {
            string end = $"__ifend_{waitSeq++}";
            if (!TryEmitExprCompareBranch(
                    oneLineReg.Groups[1].Value, oneLineReg.Groups[2].Value, oneLineReg.Groups[3].Value,
                    end, consts, items, out error, invertBranch: true))
                return true;
            string stmt = oneLineReg.Groups[4].Value.Trim().TrimEnd(';') + ";";
            if (!TryLowerLine(stmt, consts, nameToAddress, tables, ref waitSeq, items, out error, ref padResultOnStack))
                return true;
            items.Add(new AsmItem(end));
            return true;
        }

        // One-liner: if ((a & MASK) != 0) { stmt; }
        var oneLineBit = Regex.Match(line,
            @"^if\s*\(\s*\(a\s*&\s*(0x[0-9A-Fa-f]+|\d+)\)\s*(==|!=)\s*0\s*\)\s*\{\s*(.+)\s*\}\s*$");
        if (oneLineBit.Success)
        {
            string end = $"__ifend_{waitSeq++}";
            if (!TryEmitBitTestBranch(
                    oneLineBit.Groups[1].Value, oneLineBit.Groups[2].Value, end,
                    items, out error, invertBranch: true))
                return true;
            string stmt = oneLineBit.Groups[3].Value.Trim().TrimEnd(';') + ";";
            if (!TryLowerLine(stmt, consts, nameToAddress, tables, ref waitSeq, items, out error, ref padResultOnStack))
                return true;
            items.Add(new AsmItem(end));
            return true;
        }

        // if ((mem & mask) op imm) goto L;
        var memBitGoto = Regex.Match(line,
            @"^if\s*\(\s*\(\s*([A-Za-z_]\w*(?:\[[^\]]+\])?|x|y)\s*&\s*(.+?)\s*\)\s*(==|!=|>=|<)\s*(0x[0-9A-Fa-f]+|\d+)\s*\)\s*goto\s+(L_[0-9A-Fa-f]+)\s*;\s*$");
        if (memBitGoto.Success)
        {
            if (!TryEmitMemMaskedCompareBranch(
                    memBitGoto.Groups[1].Value, memBitGoto.Groups[2].Value,
                    memBitGoto.Groups[3].Value, memBitGoto.Groups[4].Value,
                    memBitGoto.Groups[5].Value, consts, items, out error, invertBranch: false))
                return false;
            return true;
        }

        // if ((mem & mask) op imm) return;
        var memBitRet = Regex.Match(line,
            @"^if\s*\(\s*\(\s*([A-Za-z_]\w*(?:\[[^\]]+\])?|x|y)\s*&\s*(.+?)\s*\)\s*(==|!=|>=|<)\s*(0x[0-9A-Fa-f]+|\d+)\s*\)\s*return\s*;\s*$");
        if (memBitRet.Success)
        {
            string skip = $"__retskip_{waitSeq++}";
            if (!TryEmitMemMaskedCompareBranch(
                    memBitRet.Groups[1].Value, memBitRet.Groups[2].Value,
                    memBitRet.Groups[3].Value, memBitRet.Groups[4].Value,
                    skip, consts, items, out error, invertBranch: true))
                return false;
            items.Add(new AsmItem(Asm.RTS()));
            items.Add(new AsmItem(skip));
            return true;
        }

        // One-liner: if ((mem & mask) op imm) { stmt; }
        var memBitOne = Regex.Match(line,
            @"^if\s*\(\s*\(\s*([A-Za-z_]\w*(?:\[[^\]]+\])?|x|y)\s*&\s*(.+?)\s*\)\s*(==|!=|>=|<)\s*(0x[0-9A-Fa-f]+|\d+)\s*\)\s*\{\s*(.+)\s*\}\s*$");
        if (memBitOne.Success)
        {
            string end = $"__ifend_{waitSeq++}";
            if (!TryEmitMemMaskedCompareBranch(
                    memBitOne.Groups[1].Value, memBitOne.Groups[2].Value,
                    memBitOne.Groups[3].Value, memBitOne.Groups[4].Value,
                    end, consts, items, out error, invertBranch: true))
                return true;
            string stmt = memBitOne.Groups[5].Value.Trim().TrimEnd(';') + ";";
            if (!TryLowerLine(stmt, consts, nameToAddress, tables, ref waitSeq, items, out error, ref padResultOnStack))
                return true;
            items.Add(new AsmItem(end));
            return true;
        }

        // One-liner: if (ramVar != 0) { stmt; }
        var oneLineMemZ = Regex.Match(line,
            @"^if\s*\(\s*([A-Za-z_]\w*)\s*(==|!=)\s*0\s*\)\s*\{\s*(.+)\s*\}\s*$");
        if (oneLineMemZ.Success
            && oneLineMemZ.Groups[1].Value is not ("a" or "x" or "y")
            && consts.ContainsKey(oneLineMemZ.Groups[1].Value))
        {
            string end = $"__ifend_{waitSeq++}";
            if (!TryResolveAddress(oneLineMemZ.Groups[1].Value, consts, out var eaMz))
            {
                error = $"mem zero if {oneLineMemZ.Groups[1].Value}";
                return true;
            }
            items.Add(new AsmItem(EmitLoad("a", eaMz)));
            items.Add(new AsmItem(oneLineMemZ.Groups[2].Value == "==" ? Asm.BNE(end) : Asm.BEQ(end)));
            string stmt = oneLineMemZ.Groups[3].Value.Trim().TrimEnd(';') + ";";
            if (!TryLowerLine(stmt, consts, nameToAddress, tables, ref waitSeq, items, out error, ref padResultOnStack))
                return true;
            items.Add(new AsmItem(end));
            return true;
        }

        // Multi-line: if (a|x|y|mem|mem[i] == N) { ... } [else { ... }]
        var regHdr = Regex.Match(line,
            @"^if\s*\(\s*(a|x|y|[A-Za-z_]\w*(?:\[[^\]]+\])?)\s*(==|!=|>=|<)\s*(0x[0-9A-Fa-f]+|\d+)\s*\)\s*$");
        if (regHdr.Success
            && i + 1 < body.Count && body[i + 1].Trim() == "{"
            && TryConsumeRegCompareIfElse(
                body, ref i, regHdr, consts, nameToAddress, tables, ref waitSeq, items,
                out error, ref padResultOnStack))
            return true;

        // Multi-line: if (ramVar != 0) { ... }
        var memZHdr = Regex.Match(line,
            @"^if\s*\(\s*([A-Za-z_]\w*)\s*(==|!=)\s*0\s*\)\s*$");
        if (memZHdr.Success
            && memZHdr.Groups[1].Value is not ("a" or "x" or "y")
            && consts.ContainsKey(memZHdr.Groups[1].Value)
            && i + 1 < body.Count && body[i + 1].Trim() == "{")
        {
            if (!TryExtractBraceBlock(body, i + 1, out int mzThenEnd, out List<string> mzThenArm))
            {
                error = "if: bad then block";
                return true;
            }
            int mzCursor = mzThenEnd;
            List<string>? mzElseArm = null;
            if (mzCursor < body.Count && body[mzCursor].Trim() == "else")
            {
                if (mzCursor + 1 >= body.Count || body[mzCursor + 1].Trim() != "{")
                {
                    error = "if: bad else block";
                    return true;
                }
                if (!TryExtractBraceBlock(body, mzCursor + 1, out int mzElseEnd, out mzElseArm))
                {
                    error = "if: bad else block";
                    return true;
                }
                mzCursor = mzElseEnd;
            }
            if (!TryResolveAddress(memZHdr.Groups[1].Value, consts, out var eaMz2))
            {
                error = $"mem zero if {memZHdr.Groups[1].Value}";
                i = mzCursor - 1;
                return true;
            }
            string mzElseLab = $"__else_{waitSeq}";
            string mzJoinLab = $"__join_{waitSeq++}";
            items.Add(new AsmItem(EmitLoad("a", eaMz2)));
            string op = memZHdr.Groups[2].Value;
            if (mzElseArm is null)
            {
                items.Add(new AsmItem(op == "==" ? Asm.BNE(mzJoinLab) : Asm.BEQ(mzJoinLab)));
                if (!TryLowerArm(mzThenArm, consts, nameToAddress, tables, ref waitSeq, items, out error, ref padResultOnStack))
                {
                    i = mzCursor - 1;
                    return true;
                }
                items.Add(new AsmItem(mzJoinLab));
            }
            else
            {
                items.Add(new AsmItem(op == "==" ? Asm.BNE(mzElseLab) : Asm.BEQ(mzElseLab)));
                if (!TryLowerArm(mzThenArm, consts, nameToAddress, tables, ref waitSeq, items, out error, ref padResultOnStack))
                {
                    i = mzCursor - 1;
                    return true;
                }
                items.Add(new AsmItem(Asm.JMP(mzJoinLab)));
                items.Add(new AsmItem(mzElseLab));
                if (!TryLowerArm(mzElseArm, consts, nameToAddress, tables, ref waitSeq, items, out error, ref padResultOnStack))
                {
                    i = mzCursor - 1;
                    return true;
                }
                items.Add(new AsmItem(mzJoinLab));
            }
            i = mzCursor - 1;
            return true;
        }

        // Multi-line: if ((mem & MASK) op imm) { ... }
        var memBitHdr = Regex.Match(line,
            @"^if\s*\(\s*\(\s*([A-Za-z_]\w*(?:\[[^\]]+\])?|x|y)\s*&\s*(.+?)\s*\)\s*(==|!=|>=|<)\s*(0x[0-9A-Fa-f]+|\d+)\s*\)\s*$");
        if (memBitHdr.Success
            && memBitHdr.Groups[1].Value is not "a"
            && i + 1 < body.Count && body[i + 1].Trim() == "{")
        {
            if (!TryExtractBraceBlock(body, i + 1, out int mbThenEnd, out List<string> mbThenArm))
            {
                error = "if: bad then block";
                return true;
            }
            int mbCursor = mbThenEnd;
            List<string>? mbElseArm = null;
            if (mbCursor < body.Count && body[mbCursor].Trim() == "else")
            {
                if (mbCursor + 1 >= body.Count || body[mbCursor + 1].Trim() != "{")
                {
                    error = "if: bad else block";
                    return true;
                }
                if (!TryExtractBraceBlock(body, mbCursor + 1, out int mbElseEnd, out mbElseArm))
                {
                    error = "if: bad else block";
                    return true;
                }
                mbCursor = mbElseEnd;
            }
            string mbElseLab = $"__else_{waitSeq}";
            string mbJoinLab = $"__join_{waitSeq++}";
            if (mbElseArm is null)
            {
                if (!TryEmitMemMaskedCompareBranch(
                        memBitHdr.Groups[1].Value, memBitHdr.Groups[2].Value,
                        memBitHdr.Groups[3].Value, memBitHdr.Groups[4].Value,
                        mbJoinLab, consts, items, out error, invertBranch: true)
                    || !TryLowerArm(mbThenArm, consts, nameToAddress, tables, ref waitSeq, items, out error, ref padResultOnStack))
                {
                    i = mbCursor - 1;
                    return true;
                }
                items.Add(new AsmItem(mbJoinLab));
            }
            else
            {
                if (!TryEmitMemMaskedCompareBranch(
                        memBitHdr.Groups[1].Value, memBitHdr.Groups[2].Value,
                        memBitHdr.Groups[3].Value, memBitHdr.Groups[4].Value,
                        mbElseLab, consts, items, out error, invertBranch: true)
                    || !TryLowerArm(mbThenArm, consts, nameToAddress, tables, ref waitSeq, items, out error, ref padResultOnStack))
                {
                    i = mbCursor - 1;
                    return true;
                }
                items.Add(new AsmItem(Asm.JMP(mbJoinLab)));
                items.Add(new AsmItem(mbElseLab));
                if (!TryLowerArm(mbElseArm, consts, nameToAddress, tables, ref waitSeq, items, out error, ref padResultOnStack))
                {
                    i = mbCursor - 1;
                    return true;
                }
                items.Add(new AsmItem(mbJoinLab));
            }
            i = mbCursor - 1;
            return true;
        }

        // Multi-line: if ((a & MASK) != 0) { ... }
        var bitHdr = Regex.Match(line,
            @"^if\s*\(\s*\(a\s*&\s*(0x[0-9A-Fa-f]+|\d+)\)\s*(==|!=)\s*0\s*\)\s*$");
        if (bitHdr.Success && i + 1 < body.Count && body[i + 1].Trim() == "{")
        {
            if (!TryExtractBraceBlock(body, i + 1, out int bitThenEnd, out List<string> bitThenArm))
            {
                error = "if: bad then block";
                return true;
            }
            int bitCursor = bitThenEnd;
            List<string>? bitElseArm = null;
            if (bitCursor < body.Count && body[bitCursor].Trim() == "else")
            {
                if (bitCursor + 1 >= body.Count || body[bitCursor + 1].Trim() != "{")
                {
                    error = "if: bad else block";
                    return true;
                }
                if (!TryExtractBraceBlock(body, bitCursor + 1, out int bitElseEnd, out bitElseArm))
                {
                    error = "if: bad else block";
                    return true;
                }
                bitCursor = bitElseEnd;
            }
            string bitElseLab = $"__else_{waitSeq}";
            string bitJoinLab = $"__join_{waitSeq++}";
            if (bitElseArm is null)
            {
                if (!TryEmitBitTestBranch(
                        bitHdr.Groups[1].Value, bitHdr.Groups[2].Value, bitJoinLab,
                        items, out error, invertBranch: true)
                    || !TryLowerArm(bitThenArm, consts, nameToAddress, tables, ref waitSeq, items, out error, ref padResultOnStack))
                {
                    i = bitCursor - 1;
                    return true;
                }
                items.Add(new AsmItem(bitJoinLab));
            }
            else
            {
                if (!TryEmitBitTestBranch(
                        bitHdr.Groups[1].Value, bitHdr.Groups[2].Value, bitElseLab,
                        items, out error, invertBranch: true)
                    || !TryLowerArm(bitThenArm, consts, nameToAddress, tables, ref waitSeq, items, out error, ref padResultOnStack))
                {
                    i = bitCursor - 1;
                    return true;
                }
                items.Add(new AsmItem(Asm.JMP(bitJoinLab)));
                items.Add(new AsmItem(bitElseLab));
                if (!TryLowerArm(bitElseArm, consts, nameToAddress, tables, ref waitSeq, items, out error, ref padResultOnStack))
                {
                    i = bitCursor - 1;
                    return true;
                }
                items.Add(new AsmItem(bitJoinLab));
            }
            i = bitCursor - 1;
            return true;
        }

        var hdr = IfFlagBlock.Match(line);
        if (!hdr.Success)
            return false;
        if (i + 1 >= body.Count || body[i + 1].Trim() != "{")
            return false;

        bool negFlag = hdr.Groups[1].Success;
        string flagName = hdr.Groups[2].Value;

        if (!TryExtractBraceBlock(body, i + 1, out int thenEnd, out List<string> thenArm))
        {
            error = "if: bad then block";
            return true;
        }

        List<string>? elseArm = null;
        int cursor = thenEnd;
        if (cursor < body.Count && body[cursor].Trim() == "else")
        {
            if (cursor + 1 >= body.Count || body[cursor + 1].Trim() != "{")
            {
                error = "if: bad else block";
                return true;
            }
            if (!TryExtractBraceBlock(body, cursor + 1, out int elseEnd, out elseArm))
            {
                error = "if: bad else block";
                return true;
            }
            cursor = elseEnd;
        }

        // Advance i to last consumed line (caller increments... actually caller uses continue after
        // TryConsumeIfElse, and i points at if line — set i to cursor-1 so loop's i++ lands on cursor).
        // Wait: for-loop does i++ after continue. We need i = cursor - 1.
        // cursor is index of first line AFTER the if/else construct.
        // ExtractBraceBlock returns end = index after closing brace.

        string elseLab = $"__else_{waitSeq}";
        string joinLab = $"__join_{waitSeq++}";

        if (elseArm is null)
        {
            // if (cond) { then }  →  branch-if-!cond past then
            items.Add(new AsmItem(BranchIfNot(flagName, negFlag, joinLab)));
            if (!TryLowerArm(thenArm, consts, nameToAddress, tables, ref waitSeq, items, out error, ref padResultOnStack))
            {
                i = cursor - 1;
                return true;
            }
            items.Add(new AsmItem(joinLab));
        }
        else
        {
            // if (cond) { then } else { else }
            // → Bxx else; then; JMP join; else: else; join:
            items.Add(new AsmItem(BranchIfNot(flagName, negFlag, elseLab)));
            if (!TryLowerArm(thenArm, consts, nameToAddress, tables, ref waitSeq, items, out error, ref padResultOnStack))
            {
                i = cursor - 1;
                return true;
            }
            items.Add(new AsmItem(Asm.JMP(joinLab)));
            items.Add(new AsmItem(elseLab));
            if (!TryLowerArm(elseArm, consts, nameToAddress, tables, ref waitSeq, items, out error, ref padResultOnStack))
            {
                i = cursor - 1;
                return true;
            }
            items.Add(new AsmItem(joinLab));
        }

        i = cursor - 1;
        return true;
    }

    static bool TryExtractBraceBlock(IReadOnlyList<string> body, int openIdx, out int endIdx, out List<string> arm)
    {
        arm = new List<string>();
        endIdx = openIdx;
        if (openIdx >= body.Count || body[openIdx].Trim() != "{")
            return false;
        int depth = 1;
        int j = openIdx + 1;
        for (; j < body.Count && depth > 0; j++)
        {
            string t = body[j].Trim();
            if (t == "{")
                depth++;
            else if (t == "}")
            {
                depth--;
                if (depth == 0)
                    break;
            }
            if (depth > 0 && t.Length > 0)
                arm.Add(t);
        }
        if (depth != 0)
            return false;
        endIdx = j + 1; // first index after closing brace
        return true;
    }

    static bool TryLowerArm(
        List<string> arm,
        Dictionary<string, ushort> consts,
        Dictionary<string, ushort> nameToAddress,
        SoftCpuTables tables,
        ref int waitSeq,
        List<AsmItem> items,
        out string? error,
        ref bool padResultOnStack)
    {
        error = null;
        for (int k = 0; k < arm.Count; k++)
        {
            if (TryConsumeDoWhile(arm, ref k, consts, nameToAddress, tables, ref waitSeq, items, out error, ref padResultOnStack))
            {
                if (error != null)
                    return false;
                continue;
            }
            // Nested if/else inside arm
            if (TryConsumeIfElse(arm, ref k, consts, nameToAddress, tables, ref waitSeq, items, out error, ref padResultOnStack))
            {
                if (error != null)
                    return false;
                continue;
            }
            if (arm[k] is ("pha();" or "push(a);") && padResultOnStack)
            {
                padResultOnStack = false;
                continue;
            }
            if (!TryLowerLine(arm[k], consts, nameToAddress, tables, ref waitSeq, items, out error, ref padResultOnStack))
                return false;
        }
        return true;
    }

    static Instruction BranchIfNot(string flag, bool condAlreadyNegated, string label)
    {
        // Branch when the if-condition is FALSE (skip then-arm / go to else).
        // if (zero) → branch when !zero → BNE
        // if (!zero) → branch when zero → BEQ
        bool wantNeg = !condAlreadyNegated;
        return (flag, wantNeg) switch
        {
            ("zero", true) => Asm.BNE(label),
            ("zero", false) => Asm.BEQ(label),
            ("carry", true) => Asm.BCC(label),
            ("carry", false) => Asm.BCS(label),
            ("negative", true) => Asm.BPL(label),
            ("negative", false) => Asm.BMI(label),
            _ => throw new InvalidOperationException($"branch !{flag}"),
        };
    }

    /// <summary>
    /// <c>ushort _uN = (ushort)(lo | (hi &lt;&lt; 8)); _uN += imm; destLo = (byte)_uN[&amp;m]; destHi = (byte)(_uN &gt;&gt; 8);</c>
    /// → LDA lo; CLC; ADC #imm; [AND #m]; STA destLo; LDA hi; ADC #0; STA destHi…
    /// </summary>
    static bool TryConsumeAdd16Ushort(
        IReadOnlyList<string> body,
        ref int i,
        Dictionary<string, ushort> consts,
        List<AsmItem> items,
        out string? error)
    {
        error = null;
        var pack = UshortPack.Match(body[i].Trim());
        if (!pack.Success)
            return false;
        if (i + 2 >= body.Count)
        {
            error = "ushort add16 truncated";
            return true;
        }

        string uName = pack.Groups[1].Value;
        string loExpr = pack.Groups[2].Value.Trim();
        string hiExpr = pack.Groups[3].Value.Trim();
        var add = UshortAddImm.Match(body[i + 1].Trim());
        if (!add.Success || add.Groups[1].Value != uName)
        {
            error = "ushort add16 missing += imm";
            return true;
        }
        if (!TryParseByteLiteral(add.Groups[2].Value, out byte imm))
        {
            error = $"ushort add16 imm {add.Groups[2].Value}";
            return true;
        }

        int at = i + 2;
        var loStore = UshortStoreLo.Match(body[at].Trim());
        if (!loStore.Success)
        {
            error = "ushort add16 missing lo store";
            return true;
        }
        string loU = loStore.Groups[2].Success ? loStore.Groups[2].Value : loStore.Groups[4].Value;
        if (loU != uName)
        {
            error = "ushort add16 lo name mismatch";
            return true;
        }
        string destLo = loStore.Groups[1].Value.Trim();
        byte? andMask = null;
        if (loStore.Groups[2].Success && loStore.Groups[3].Success)
        {
            if (!TryParseByteLiteral(loStore.Groups[3].Value, out byte m))
            {
                error = $"ushort add16 mask {loStore.Groups[3].Value}";
                return true;
            }
            andMask = m;
        }
        at++;

        var hiDests = new List<string>();
        while (at < body.Count)
        {
            var hiStore = UshortStoreHi.Match(body[at].Trim());
            if (!hiStore.Success || hiStore.Groups[2].Value != uName)
                break;
            hiDests.Add(hiStore.Groups[1].Value.Trim());
            at++;
        }
        if (hiDests.Count == 0)
        {
            error = "ushort add16 missing hi store";
            return true;
        }

        if (!TryEmitLoadAFromMemExpr(loExpr, consts, items, out error))
        {
            error ??= $"ushort add16 lo {loExpr}";
            return true;
        }
        items.Add(new AsmItem(Asm.CLC()));
        items.Add(new AsmItem(Asm.ADC(imm)));
        if (andMask.HasValue)
            items.Add(new AsmItem(Asm.AND(andMask.Value)));
        if (!TryEmitStoreAToMemExpr(destLo, consts, items, out error))
        {
            error ??= $"ushort add16 destLo {destLo}";
            return true;
        }
        if (!TryEmitLoadAFromMemExpr(hiExpr, consts, items, out error))
        {
            error ??= $"ushort add16 hi {hiExpr}";
            return true;
        }
        items.Add(new AsmItem(Asm.ADC(0)));
        foreach (string d in hiDests)
        {
            if (!TryEmitStoreAToMemExpr(d, consts, items, out error))
            {
                error ??= $"ushort add16 destHi {d}";
                return true;
            }
        }

        i = at - 1; // loop i++ lands on next stmt
        return true;
    }

    static bool TryEmitStoreAToMemExpr(
        string expr, Dictionary<string, ushort> consts, List<AsmItem> items, out string? error)
    {
        error = null;
        expr = expr.Trim();
        var indexed = Regex.Match(expr,
            @"^([A-Za-z_]\w*)\[(?:(x|y)(?:\s*\+\s*(0x[0-9A-Fa-f]+|\d+))?|(0x[0-9A-Fa-f]+|\d+))\]$");
        if (indexed.Success && consts.ContainsKey(indexed.Groups[1].Value))
        {
            if (!TryResolveBufferIndex(
                    indexed.Groups[1].Value,
                    indexed.Groups[2].Value,
                    indexed.Groups[3].Value,
                    indexed.Groups[4].Value,
                    consts,
                    out var ea))
            {
                error = $"store mem {expr}";
                return false;
            }
            items.Add(new AsmItem(EmitStore("a", ea)));
            return true;
        }
        if (consts.ContainsKey(expr) && TryResolveAddress(expr, consts, out var abs))
        {
            items.Add(new AsmItem(EmitStore("a", abs)));
            return true;
        }
        return false;
    }

    /// <summary>
    /// <c>{ ushort _p = vramBufPtr[idx]; poke(zp_00,(byte)_p); poke(zp_01,(byte)(_p>>8)); } BindVramFromZp();</c>
    /// → LDA lo,idx / STA $00 / LDA hi,idx / STA $01
    /// </summary>
    static bool TryConsumeVramBufPtrBlock(
        IReadOnlyList<string> body,
        ref int i,
        SoftCpuTables tables,
        Dictionary<string, ushort> consts,
        List<AsmItem> items,
        out string? error)
    {
        error = null;
        if (body[i].Trim() != "{")
            return false;
        if (i + 4 >= body.Count)
            return false;

        var load = VramBufPtrLoad.Match(body[i + 1].Trim());
        if (!load.Success)
            return false;
        string poke0 = body[i + 2].Trim();
        string poke1 = body[i + 3].Trim();
        if (body[i + 4].Trim() != "}")
            return false;
        // poke(zp_00, …) or zp_00 = … (after RAM-variable rewrite)
        if (!IsZp00Store(poke0) || !IsZp01Store(poke1))
            return false;

        string tableName = load.Groups[1].Value;
        string idx = load.Groups[2].Value;
        if (!tables.VramPointers.TryGetValue(tableName, out var ptr))
        {
            error = $"unknown pointer table {tableName}";
            return true; // consumed shape but failed
        }

        SoftAddrMode mode = idx == "x" ? SoftAddrMode.AbsoluteX : SoftAddrMode.AbsoluteY;
        items.Add(new AsmItem(EmitLoad("a", new EffectiveAddress(ptr.LoAddr, mode))));
        if (!TryResolveAddress("vram_ptr_lo", consts, out var zp0)
            && !TryResolveAddress("zp_00", consts, out zp0))
            zp0 = new EffectiveAddress(0x00, SoftAddrMode.ZeroPage);
        items.Add(new AsmItem(EmitStore("a", zp0)));
        items.Add(new AsmItem(EmitLoad("a", new EffectiveAddress(ptr.HiAddr, mode))));
        if (!TryResolveAddress("vram_ptr_hi", consts, out var zp1)
            && !TryResolveAddress("zp_01", consts, out zp1))
            zp1 = new EffectiveAddress(0x01, SoftAddrMode.ZeroPage);
        items.Add(new AsmItem(EmitStore("a", zp1)));

        i += 4; // sit on closing brace
        if (i + 1 < body.Count && body[i + 1].Trim() is "BindVramFromZp();")
            i++; // skip soft-only bind
        return true;
    }

    static bool TryLowerVramFill(string fillExpr, string countExpr, List<AsmItem> items, out string? error)
    {
        error = null;
        if (!TryParseByteOrColor(fillExpr, out byte fill))
        {
            error = $"vram_fill value {fillExpr}";
            return false;
        }
        if (!TryParseUshortLiteral(countExpr, out ushort count) || count == 0)
        {
            error = $"vram_fill count {countExpr}";
            return false;
        }

        // SMB nametable wipe: LDA #c / LDX #4 / LDY #$C0 / STA $2007 / DEY / BNE / DEX / BNE
        if (count == 960)
        {
            // outer and inner share the STA (classic SMB wipe). One label on STA; DEX BNE jumps there.
            string id = items.Count.ToString(CultureInfo.InvariantCulture);
            string loop = $"__vfill960_{id}";
            items.Add(new AsmItem(Asm.LDA(fill)));
            items.Add(new AsmItem(Asm.LDX(4)));
            items.Add(new AsmItem(Asm.LDY(0xC0)));
            items.Add(new AsmItem(loop));
            items.Add(new AsmItem(Asm.STA_abs(0x2007)));
            items.Add(new AsmItem(Asm.DEY()));
            items.Add(new AsmItem(Asm.BNE(loop)));
            items.Add(new AsmItem(Asm.DEX()));
            items.Add(new AsmItem(Asm.BNE(loop)));
            return true;
        }

        if (count <= 255)
        {
            string lab = $"__vfill_{items.Count}";
            items.Add(new AsmItem(Asm.LDA(fill)));
            items.Add(new AsmItem(Asm.LDY((byte)count)));
            items.Add(new AsmItem(lab));
            items.Add(new AsmItem(Asm.STA_abs(0x2007)));
            items.Add(new AsmItem(Asm.DEY()));
            items.Add(new AsmItem(Asm.BNE(lab)));
            return true;
        }

        error = $"vram_fill count {count} too large";
        return false;
    }

    static bool TryParseUshortLiteral(string text, out ushort value)
    {
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return ushort.TryParse(text.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        return ushort.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    static string UnescapeCSharpString(string body)
    {
        var sb = new StringBuilder(body.Length);
        for (int i = 0; i < body.Length; i++)
        {
            if (body[i] != '\\' || i + 1 >= body.Length)
            {
                sb.Append(body[i]);
                continue;
            }
            i++;
            switch (body[i])
            {
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case '\\': sb.Append('\\'); break;
                case '"': sb.Append('"'); break;
                case 'x' when i + 2 < body.Length
                    && byte.TryParse(body.Substring(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte xb):
                    sb.Append((char)xb);
                    i += 2;
                    break;
                default:
                    sb.Append(body[i]);
                    break;
            }
        }
        return sb.ToString();
    }

    static int IndexOfTrailingLineComment(string line)
    {
        bool inStr = false;
        for (int i = 0; i < line.Length - 1; i++)
        {
            char c = line[i];
            if (c == '"' && (i == 0 || line[i - 1] != '\\'))
                inStr = !inStr;
            if (!inStr && c == '/' && line[i + 1] == '/')
                return i;
        }
        return -1;
    }

    static bool TryLowerVramAdr(string expr, List<AsmItem> items, out string? error)
    {
        error = null;
        expr = expr.Trim();

        var nt = Ntadr.Match(expr);
        if (nt.Success)
        {
            char bank = nt.Groups[1].Value[0];
            byte x = ParseByte(nt.Groups[2].Value);
            byte y = ParseByte(nt.Groups[3].Value);
            ushort baseAddr = bank switch
            {
                'A' => 0x2000,
                'B' => 0x2400,
                'C' => 0x2800,
                _ => 0x2C00,
            };
            ushort abs = (ushort)(baseAddr + y * 32 + x);
            return TryLowerVramAdr($"0x{abs:X4}", items, out error);
        }

        // vram_adr(0x3F00) / (ushort)0x3F00
        string lit = expr;
        if (lit.StartsWith("(ushort)", StringComparison.Ordinal))
            lit = lit.Substring(8).Trim();
        if (lit.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && ushort.TryParse(lit.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort absAddr))
        {
            ushort abs = absAddr;
            // SMB: vram_adr(0x3F00); vram_adr(0x0000); → second is STA $2006; STA $2006 with A already 0.
            if (abs == 0 && LastLoadsAWithImm(items, 0))
            {
                items.Add(new AsmItem(Asm.STA_abs(0x2006)));
                items.Add(new AsmItem(Asm.STA_abs(0x2006)));
                return true;
            }
            byte hi = (byte)(abs >> 8);
            byte lo = (byte)(abs & 0xFF);
            items.Add(new AsmItem(Asm.LDA(hi)));
            items.Add(new AsmItem(Asm.STA_abs(0x2006)));
            if (!LastLoadsAWithImm(items, lo))
                items.Add(new AsmItem(Asm.LDA(lo)));
            items.Add(new AsmItem(Asm.STA_abs(0x2006)));
            return true;
        }

        // (ushort)(a << 8)  → STA $2006; LDA #0; STA $2006
        if (Regex.IsMatch(expr, @"^\(ushort\)\(\s*a\s*<<\s*8\s*\)$"))
        {
            items.Add(new AsmItem(Asm.STA_abs(0x2006)));
            items.Add(new AsmItem(Asm.LDA(0)));
            items.Add(new AsmItem(Asm.STA_abs(0x2006)));
            return true;
        }

        // (ushort)((a << 8) | lo)
        var orLo = Regex.Match(expr,
            @"^\(ushort\)\(\s*\(\s*a\s*<<\s*8\s*\)\s*\|\s*(.+?)\s*\)$");
        if (orLo.Success)
        {
            items.Add(new AsmItem(Asm.STA_abs(0x2006)));
            string loExpr = orLo.Groups[1].Value.Trim();
            if (loExpr is "a" or "x" or "y")
            {
                if (!TryEmitStoreReg(loExpr, 0x2006, items, out error))
                    return false;
            }
            else if (TryParseByteLiteral(loExpr, out byte loImm))
            {
                items.Add(new AsmItem(Asm.LDA(loImm)));
                items.Add(new AsmItem(Asm.STA_abs(0x2006)));
            }
            else
            {
                error = $"vram_adr lo {loExpr}";
                return false;
            }
            return true;
        }

        error = $"vram_adr {expr}";
        return false;
    }

    static bool TryEmitStoreReg(string reg, ushort abs, List<AsmItem> items, out string? error)
    {
        error = null;
        reg = reg.Trim();
        switch (reg)
        {
            case "a":
                items.Add(new AsmItem(Asm.STA_abs(abs)));
                return true;
            case "x":
                items.Add(new AsmItem(Asm.STX_abs(abs)));
                return true;
            case "y":
                items.Add(new AsmItem(Asm.STY_abs(abs)));
                return true;
            default:
                error = $"store reg {reg}";
                return false;
        }
    }

    /// <summary>STA abs from reg / imm / mem / byte-expr.</summary>
    static bool TryEmitValueToAbs(
        string expr, ushort abs, Dictionary<string, ushort> consts, List<AsmItem> items, out string? error)
    {
        if (TryEmitStoreReg(expr, abs, items, out error))
            return true;
        error = null;
        if (TryEmitByteExprIntoA(expr, consts, items, out error)
            || TryEmitLoadAFromMemExpr(expr, consts, items, out error))
        {
            items.Add(new AsmItem(Asm.STA_abs(abs)));
            return true;
        }
        error ??= $"value to ${abs:X4}: {expr}";
        return false;
    }

    /// <summary><c>(ENUM)expr</c> → evaluate expr into A; STA abs.</summary>
    static bool TryEmitCastEnumValueToAbs(
        string expr,
        string enumName,
        ushort abs,
        Dictionary<string, ushort> consts,
        List<AsmItem> items,
        out string? error)
    {
        error = null;
        expr = expr.Trim();
        string prefix = $"({enumName})";
        if (!expr.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        string inner = expr.Substring(prefix.Length).Trim();
        if (inner.Length >= 2 && inner[0] == '(' && inner[inner.Length - 1] == ')')
            inner = inner.Substring(1, inner.Length - 2).Trim();
        return TryEmitValueToAbs(inner, abs, consts, items, out error);
    }

    static bool IsRegisterCast(string expr, string enumName, out string reg)
    {
        reg = "";
        expr = expr.Trim();
        if (expr is "a" or "x" or "y")
        {
            reg = expr;
            return true;
        }
        var m = Regex.Match(expr, $@"^\({enumName}\)\s*(a|x|y)$");
        if (!m.Success)
            return false;
        reg = m.Groups[1].Value;
        return true;
    }

    static Instruction EmitAdc(EffectiveAddress ea) => ea.Mode switch
    {
        SoftAddrMode.ZeroPage => Asm.ADC_zpg((byte)ea.Address),
        SoftAddrMode.ZeroPageX => Asm.ADC_zpg_X((byte)ea.Address),
        SoftAddrMode.AbsoluteY => Asm.ADC_abs_Y(ea.Address),
        SoftAddrMode.Absolute => new Instruction(Opcode.ADC, AddressMode.Absolute, new AbsoluteOperand(ea.Address)),
        SoftAddrMode.AbsoluteX => new Instruction(Opcode.ADC, AddressMode.AbsoluteX, new AbsoluteOperand(ea.Address)),
        _ => throw new InvalidOperationException($"adc {ea.Mode}"),
    };

    static Instruction EmitSbc(EffectiveAddress ea) => ea.Mode switch
    {
        SoftAddrMode.ZeroPage => Asm.SBC_zpg((byte)ea.Address),
        SoftAddrMode.ZeroPageX => Asm.SBC_zpg_X((byte)ea.Address),
        SoftAddrMode.AbsoluteY => Asm.SBC_abs_Y(ea.Address),
        SoftAddrMode.Absolute => new Instruction(Opcode.SBC, AddressMode.Absolute, new AbsoluteOperand(ea.Address)),
        SoftAddrMode.AbsoluteX => new Instruction(Opcode.SBC, AddressMode.AbsoluteX, new AbsoluteOperand(ea.Address)),
        _ => throw new InvalidOperationException($"sbc {ea.Mode}"),
    };

    static Instruction EmitAnd(EffectiveAddress ea) => ea.Mode switch
    {
        SoftAddrMode.ZeroPage => Asm.AND_zpg((byte)ea.Address),
        SoftAddrMode.ZeroPageX => Asm.AND_zpg_X((byte)ea.Address),
        SoftAddrMode.Absolute => Asm.AND_abs(ea.Address),
        SoftAddrMode.AbsoluteX => new Instruction(Opcode.AND, AddressMode.AbsoluteX, new AbsoluteOperand(ea.Address)),
        SoftAddrMode.AbsoluteY => new Instruction(Opcode.AND, AddressMode.AbsoluteY, new AbsoluteOperand(ea.Address)),
        _ => throw new InvalidOperationException($"and {ea.Mode}"),
    };

    static Instruction EmitOra(EffectiveAddress ea) => ea.Mode switch
    {
        SoftAddrMode.ZeroPage => Asm.ORA_zpg((byte)ea.Address),
        SoftAddrMode.ZeroPageX => Asm.ORA_zpg_X((byte)ea.Address),
        SoftAddrMode.Absolute => Asm.ORA_abs(ea.Address),
        SoftAddrMode.AbsoluteX => new Instruction(Opcode.ORA, AddressMode.AbsoluteX, new AbsoluteOperand(ea.Address)),
        SoftAddrMode.AbsoluteY => Asm.ORA_abs_Y(ea.Address),
        _ => throw new InvalidOperationException($"ora {ea.Mode}"),
    };

    static Instruction EmitEor(EffectiveAddress ea) => ea.Mode switch
    {
        SoftAddrMode.ZeroPage => Asm.EOR_zpg((byte)ea.Address),
        SoftAddrMode.ZeroPageX => Asm.EOR_zpg_X((byte)ea.Address),
        SoftAddrMode.Absolute => new Instruction(Opcode.EOR, AddressMode.Absolute, new AbsoluteOperand(ea.Address)),
        SoftAddrMode.AbsoluteX => new Instruction(Opcode.EOR, AddressMode.AbsoluteX, new AbsoluteOperand(ea.Address)),
        SoftAddrMode.AbsoluteY => Asm.EOR_Y_abs(ea.Address),
        _ => throw new InvalidOperationException($"eor {ea.Mode}"),
    };

    /// <summary>
    /// Inline <see cref="NESLib.pad_poll"/> / <see cref="NESLib.pad_poll_msb"/>.
    /// SMB noise-filtered serial read (double-read + LSR) with X as port index.
    /// A on entry seeds the shift register (strobe paths leave A=1); after the loop
    /// A holds the buttons and the same byte remains on the stack (SMB shape).
    /// </summary>
    static bool TryLowerPadPoll(string arg, bool msbFirst, List<AsmItem> items, out string? error)
    {
        error = null;
        arg = arg.Trim();
        // Port in X (SMB). Literal 0/1 → LDX #n first.
        if (arg is not "x")
        {
            if (TryParseByteLiteral(arg, out byte port))
            {
                items.Add(new AsmItem(Asm.LDX(port)));
            }
            else if (arg == "a")
            {
                items.Add(new AsmItem(Asm.TAX()));
            }
            else if (arg == "y")
            {
                items.Add(new AsmItem(Asm.TYA()));
                items.Add(new AsmItem(Asm.TAX()));
            }
            else
            {
                error = $"pad_poll arg {arg}";
                return false;
            }
        }

        // SMB: LDY #8 / loop: PHA / LDA $4016,X / filter / PLA / ROL|ROR / DEY / BNE loop
        // First PHA seeds from A (strobe leaves A=1); last PHA leaves buttons on stack.
        string loop = $"__pad_{items.Count}";
        items.Add(new AsmItem(Asm.LDY(8)));
        items.Add(new AsmItem(loop));
        items.Add(new AsmItem(Asm.PHA()));
        items.Add(new AsmItem(Asm.LDA_abs_X(0x4016)));
        items.Add(new AsmItem(Asm.STA_zpg(0)));
        items.Add(new AsmItem(Asm.LSR_A()));
        items.Add(new AsmItem(Asm.ORA_zpg(0)));
        items.Add(new AsmItem(Asm.LSR_A()));
        items.Add(new AsmItem(Asm.PLA()));
        items.Add(new AsmItem(msbFirst ? Asm.ROL_A() : Asm.ROR_A()));
        items.Add(new AsmItem(Asm.DEY()));
        items.Add(new AsmItem(Asm.BNE(loop)));
        // A = buttons; same byte still on stack (matches SMB before STA $06FC,X).
        return true;
    }

    static SoftCpuTables ParseSoftCpuTables(string programCs)
    {
        var palettes = new List<PaletteRow>();
        foreach (Match m in PaletteEntry.Matches(programCs))
        {
            if (!ushort.TryParse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort bas))
                continue;
            if (!TryParseByteList(m.Groups[2].Value, out byte[] data))
                continue;
            palettes.Add(new PaletteRow(bas, data));
        }

        var ptrs = new Dictionary<string, VramPtrTable>(StringComparer.Ordinal);
        var lines = programCs.Replace("\r\n", "\n").Split('\n');
        ushort pendingLo = 0, pendingHi = 0;
        bool havePending = false;
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            var cm = VramPtrComment.Match(line);
            if (cm.Success)
            {
                pendingLo = ushort.Parse(cm.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                pendingHi = ushort.Parse(cm.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                havePending = true;
                continue;
            }
            var am = VramPtrArray.Match(line);
            if (!am.Success || !havePending)
                continue;
            if (!TryParseUshortList(am.Groups[2].Value, out ushort[] entries))
                continue;
            ptrs[am.Groups[1].Value] = new VramPtrTable(pendingLo, pendingHi, entries);
            havePending = false;
        }

        return new SoftCpuTables(palettes, ptrs);
    }

    static void ApplyTableData(byte[] prg, SoftCpuTables tables, ILogger logger)
    {
        int palBytes = 0;
        foreach (var row in tables.Palettes)
        {
            if (!TryCpuToPrgOffset(row.Base, 0, prg.Length, out int off))
                continue;
            int n = Math.Min(row.Data.Length, prg.Length - off);
            if (n <= 0)
                continue;
            Array.Copy(row.Data, 0, prg, off, n);
            palBytes += n;
        }

        int ptrBytes = 0;
        foreach (var kv in tables.VramPointers)
        {
            var t = kv.Value;
            for (int i = 0; i < t.Entries.Length; i++)
            {
                ushort p = t.Entries[i];
                if (TryCpuToPrgOffset((ushort)(t.LoAddr + i), 0, prg.Length, out int loOff))
                {
                    prg[loOff] = (byte)(p & 0xFF);
                    ptrBytes++;
                }
                if (TryCpuToPrgOffset((ushort)(t.HiAddr + i), 0, prg.Length, out int hiOff))
                {
                    prg[hiOff] = (byte)(p >> 8);
                    ptrBytes++;
                }
            }
        }

        if (palBytes > 0 || ptrBytes > 0)
            logger.WriteLine($"SoftCpu: wrote {palBytes}B palette data, {ptrBytes}B vramBufPtr entries from Program.cs");
    }

    static bool TryParseByteList(string text, out byte[] data)
    {
        var list = new List<byte>();
        foreach (string part in text.Split(','))
        {
            string p = part.Trim();
            if (p.Length == 0)
                continue;
            if (!TryParseByteOrColor(p, out byte v))
            {
                data = Array.Empty<byte>();
                return false;
            }
            list.Add(v);
        }
        data = list.ToArray();
        return data.Length > 0;
    }

    static bool TryParseUshortList(string text, out ushort[] data)
    {
        var list = new List<ushort>();
        foreach (string part in text.Split(','))
        {
            string p = part.Trim();
            if (p.Length == 0)
                continue;
            if (p.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (!ushort.TryParse(p.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort v))
                {
                    data = Array.Empty<ushort>();
                    return false;
                }
                list.Add(v);
            }
            else if (ushort.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort d))
            {
                list.Add(d);
            }
            else
            {
                data = Array.Empty<ushort>();
                return false;
            }
        }
        data = list.ToArray();
        return data.Length > 0;
    }

    static bool TryParseByteOrColor(string text, out byte value)
    {
        text = text.Trim();
        if (text.StartsWith("NESColor.", StringComparison.Ordinal))
        {
            string name = text.Substring("NESColor.".Length);
            if (NesColors.TryGetValue(name, out value))
                return true;
            value = 0;
            return false;
        }
        return TryParseByteLiteral(text, out value);
    }

    static Dictionary<string, byte> BuildNesColors()
    {
        var d = new Dictionary<string, byte>(StringComparer.Ordinal);
        foreach (var field in typeof(NESColor).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
        {
            if (field.FieldType != typeof(byte))
                continue;
            d[field.Name] = (byte)field.GetValue(null)!;
        }
        return d;
    }

    static Instruction EmitLoad(string reg, EffectiveAddress ea)
    {
        // Promote illegal zp modes to absolute (LDA has no zp,Y; LDX has no abs,X / zp,X; etc.)
        SoftAddrMode mode = ea.Mode;
        ushort addr = ea.Address;
        if (reg == "a" && mode == SoftAddrMode.ZeroPageY)
            mode = SoftAddrMode.AbsoluteY;
        if (reg == "x" && mode is SoftAddrMode.AbsoluteX or SoftAddrMode.ZeroPageX)
            mode = SoftAddrMode.AbsoluteY; // LDX abs,Y / zp,Y only for indexed
        if (reg == "x" && mode == SoftAddrMode.ZeroPageY)
            return Asm.LDX_zpg_Y((byte)addr);
        if (reg == "x" && mode == SoftAddrMode.AbsoluteY)
            return new Instruction(Opcode.LDX, AddressMode.AbsoluteY, new AbsoluteOperand(addr));
        if (reg == "y" && mode == SoftAddrMode.AbsoluteY)
            mode = SoftAddrMode.AbsoluteX; // LDY abs,X
        if (reg == "y" && mode == SoftAddrMode.ZeroPageY)
            mode = SoftAddrMode.ZeroPageX;

        return (reg, mode) switch
        {
            ("a", SoftAddrMode.Absolute) => Asm.LDA_abs(addr),
            ("a", SoftAddrMode.AbsoluteX) => Asm.LDA_abs_X(addr),
            ("a", SoftAddrMode.AbsoluteY) => Asm.LDA_abs_Y(addr),
            ("a", SoftAddrMode.ZeroPage) => Asm.LDA_zpg((byte)addr),
            ("a", SoftAddrMode.ZeroPageX) => Asm.LDA_zpg_X((byte)addr),
            ("x", SoftAddrMode.Absolute) => Asm.LDX_abs(addr),
            ("x", SoftAddrMode.ZeroPage) => Asm.LDX_zpg((byte)addr),
            ("y", SoftAddrMode.Absolute) => Asm.LDY_abs(addr),
            ("y", SoftAddrMode.AbsoluteX) => Asm.LDY_abs_X(addr),
            ("y", SoftAddrMode.ZeroPage) => Asm.LDY_zpg((byte)addr),
            ("y", SoftAddrMode.ZeroPageX) => Asm.LDY_zpg_X((byte)addr),
            _ => throw new InvalidOperationException($"load {reg} {mode}"),
        };
    }

    static Instruction EmitStore(string reg, EffectiveAddress ea)
    {
        SoftAddrMode mode = ea.Mode;
        ushort addr = ea.Address;
        if (reg == "a" && mode == SoftAddrMode.ZeroPageY)
            mode = SoftAddrMode.AbsoluteY;
        if (reg == "x" && mode is SoftAddrMode.AbsoluteX or SoftAddrMode.ZeroPageX or SoftAddrMode.AbsoluteY)
        {
            if (mode == SoftAddrMode.ZeroPageY || (mode == SoftAddrMode.AbsoluteY && addr <= 0xFF))
                return Asm.STX_zpg_Y((byte)addr);
            throw new InvalidOperationException($"store x {mode}");
        }
        if (reg == "y" && mode == SoftAddrMode.AbsoluteY)
            mode = SoftAddrMode.AbsoluteX;
        if (reg == "y" && mode == SoftAddrMode.ZeroPageY)
            mode = SoftAddrMode.ZeroPageX;

        return (reg, mode) switch
        {
            ("a", SoftAddrMode.Absolute) => Asm.STA_abs(addr),
            ("a", SoftAddrMode.AbsoluteX) => Asm.STA_abs_X(addr),
            ("a", SoftAddrMode.AbsoluteY) => Asm.STA_abs_Y(addr),
            ("a", SoftAddrMode.ZeroPage) => Asm.STA_zpg((byte)addr),
            ("a", SoftAddrMode.ZeroPageX) => Asm.STA_zpg_X((byte)addr),
            ("x", SoftAddrMode.Absolute) => Asm.STX_abs(addr),
            ("x", SoftAddrMode.ZeroPage) => Asm.STX_zpg((byte)addr),
            ("y", SoftAddrMode.Absolute) => Asm.STY_abs(addr),
            ("y", SoftAddrMode.ZeroPage) => Asm.STY_zpg((byte)addr),
            ("y", SoftAddrMode.ZeroPageX) => Asm.STY_zpg_X((byte)addr),
            ("y", SoftAddrMode.AbsoluteX) => new Instruction(Opcode.STY, AddressMode.AbsoluteX, new AbsoluteOperand(addr)),
            _ => throw new InvalidOperationException($"store {reg} {mode}"),
        };
    }

    static Instruction EmitInc(EffectiveAddress ea) => ea.Mode switch
    {
        SoftAddrMode.Absolute => Asm.INC_abs(ea.Address),
        SoftAddrMode.ZeroPage => Asm.INC_zpg((byte)ea.Address),
        SoftAddrMode.ZeroPageX => Asm.INC_zpg_X((byte)ea.Address),
        SoftAddrMode.AbsoluteX => new Instruction(Opcode.INC, AddressMode.AbsoluteX, new AbsoluteOperand(ea.Address)),
        _ => throw new InvalidOperationException($"inc {ea.Mode}"),
    };

    static Instruction EmitDec(EffectiveAddress ea) => ea.Mode switch
    {
        SoftAddrMode.Absolute => Asm.DEC_abs(ea.Address),
        SoftAddrMode.ZeroPage => Asm.DEC_zpg((byte)ea.Address),
        SoftAddrMode.ZeroPageX => Asm.DEC_zpg_X((byte)ea.Address),
        SoftAddrMode.AbsoluteX => new Instruction(Opcode.DEC, AddressMode.AbsoluteX, new AbsoluteOperand(ea.Address)),
        _ => throw new InvalidOperationException($"dec {ea.Mode}"),
    };

    static bool TryResolveAddress(string expr, Dictionary<string, ushort> consts, out EffectiveAddress ea)
    {
        ea = default;
        expr = expr.Trim();

        // (ushort)(base + x) or (ushort)(base + y)
        var indexed = Regex.Match(expr,
            @"^\(ushort\)\((.+?)\s*\+\s*(x|y)\)\s*$");
        if (indexed.Success)
        {
            if (!TryResolveSimpleAddr(indexed.Groups[1].Value.Trim(), consts, out ushort baseAddr))
                return false;
            string idx = indexed.Groups[2].Value;
            if (baseAddr <= 0xFF)
            {
                ea = new EffectiveAddress(baseAddr, idx == "x" ? SoftAddrMode.ZeroPageX : SoftAddrMode.ZeroPageY);
                // LDX doesn't support zp,X — callers handle failure
                if (idx == "y" && baseAddr <= 0xFF)
                    ea = new EffectiveAddress(baseAddr, SoftAddrMode.ZeroPageY);
            }
            else
            {
                ea = new EffectiveAddress(baseAddr, idx == "x" ? SoftAddrMode.AbsoluteX : SoftAddrMode.AbsoluteY);
            }
            return true;
        }

        // bare + x without cast: ram_0201 + x
        indexed = Regex.Match(expr, @"^(.+?)\s*\+\s*(x|y)\s*$");
        if (indexed.Success && !expr.Contains("peek", StringComparison.Ordinal))
        {
            if (!TryResolveSimpleAddr(indexed.Groups[1].Value.Trim(), consts, out ushort baseAddr))
                return false;
            string idx = indexed.Groups[2].Value;
            if (baseAddr <= 0xFF)
                ea = new EffectiveAddress(baseAddr, idx == "x" ? SoftAddrMode.ZeroPageX : SoftAddrMode.ZeroPageY);
            else
                ea = new EffectiveAddress(baseAddr, idx == "x" ? SoftAddrMode.AbsoluteX : SoftAddrMode.AbsoluteY);
            return true;
        }

        if (!TryResolveSimpleAddr(expr, consts, out ushort abs))
            return false;
        ea = new EffectiveAddress(abs, abs <= 0xFF ? SoftAddrMode.ZeroPage : SoftAddrMode.Absolute);
        // Prefer absolute for MMIO and RAM that original used as abs (SMB uses abs for $07xx)
        if (abs >= 0x100)
            ea = new EffectiveAddress(abs, SoftAddrMode.Absolute);
        return true;
    }

    static bool TryResolveSimpleAddr(string expr, Dictionary<string, ushort> consts, out ushort addr)
    {
        addr = 0;
        expr = expr.Trim();
        if (expr.StartsWith("(ushort)", StringComparison.Ordinal))
            expr = expr.Substring("(ushort)".Length).Trim();
        if (expr.Length >= 2 && expr[0] == '(' && expr[expr.Length - 1] == ')')
            expr = expr.Substring(1, expr.Length - 2).Trim();

        // Named NESLib constants
        if (TryResolveNamedMmio(expr, out addr))
            return true;

        if (consts.TryGetValue(expr, out addr))
            return true;

        if (expr.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && ushort.TryParse(expr.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out addr))
            return true;

        if (ushort.TryParse(expr, NumberStyles.Integer, CultureInfo.InvariantCulture, out addr))
            return true;

        return false;
    }

    static bool TryResolveNamedMmio(string name, out ushort addr)
    {
        switch (name)
        {
            case "PPU_CTRL": addr = 0x2000; return true;
            case "PPU_MASK": addr = 0x2001; return true;
            case "PPU_STATUS": addr = 0x2002; return true;
            case "PPU_OAM_ADDR": addr = 0x2003; return true;
            case "PPU_OAM_DATA": addr = 0x2004; return true;
            case "PPU_SCROLL": addr = 0x2005; return true;
            case "PPU_ADDR": addr = 0x2006; return true;
            case "PPU_DATA": addr = 0x2007; return true;
            case "PPU_OAM_DMA": addr = 0x4014; return true;
            case "APU_STATUS": addr = 0x4015; return true;
            case "APU_FRAME": addr = 0x4017; return true;
            case "APU_DMC_RAW": addr = 0x4011; return true;
            case "APU_PULSE1_CTRL": addr = 0x4000; return true;
            case "APU_PULSE1_SWEEP": addr = 0x4001; return true;
            case "APU_PULSE1_TIMER_LO": addr = 0x4002; return true;
            case "APU_PULSE1_TIMER_HI": addr = 0x4003; return true;
            case "APU_PULSE2_CTRL": addr = 0x4004; return true;
            case "APU_PULSE2_SWEEP": addr = 0x4005; return true;
            case "APU_PULSE2_TIMER_LO": addr = 0x4006; return true;
            case "APU_PULSE2_TIMER_HI": addr = 0x4007; return true;
            case "APU_TRIANGLE_CTRL": addr = 0x4008; return true;
            case "APU_TRIANGLE_TIMER_LO": addr = 0x400A; return true;
            case "APU_TRIANGLE_TIMER_HI": addr = 0x400B; return true;
            case "APU_NOISE_CTRL": addr = 0x400C; return true;
            case "APU_NOISE_PERIOD": addr = 0x400E; return true;
            case "APU_NOISE_LENGTH": addr = 0x400F; return true;
            case "JOYPAD1": addr = 0x4016; return true;
            case "JOYPAD2": addr = 0x4017; return true;
            default:
                addr = 0;
                return false;
        }
    }

    static byte[] Assemble(ushort baseAddress, List<AsmItem> items)
    {
        var labels = new LabelTable();
        ushort pc = baseAddress;
        foreach (var item in items)
        {
            if (item.Label != null)
                labels.Define(item.Label, pc);
            if (item.Instruction != null)
                pc = (ushort)(pc + item.Instruction.Size);
        }

        var ms = new MemoryStream();
        pc = baseAddress;
        foreach (var item in items)
        {
            if (item.Instruction is null)
                continue;
            byte[] enc = item.Instruction.ToBytes(pc, labels);
            ms.Write(enc, 0, enc.Length);
            pc = (ushort)(pc + enc.Length);
        }
        return ms.ToArray();
    }

    static Dictionary<string, ushort> ParseConsts(string programCs)
    {
        var map = new Dictionary<string, ushort>(StringComparer.Ordinal);
        foreach (string raw in programCs.Split('\n'))
        {
            string line = raw.Trim();
            var c = ConstUshort.Match(line);
            if (c.Success
                && ushort.TryParse(c.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort cv))
            {
                map[c.Groups[1].Value] = cv;
                continue;
            }

            var arr = ByteArrayDecl.Match(line);
            if (arr.Success)
            {
                string arrName = arr.Groups[1].Value;
                if (arr.Groups[2].Success
                    && ushort.TryParse(arr.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort arrAddr))
                    map[arrName] = arrAddr;
                else if (TryAddrFromBufferName(arrName, out ushort arrFromName))
                    map[arrName] = arrFromName;
                continue;
            }

            var b = ByteRamVar.Match(line);
            if (!b.Success)
                continue;
            string name = b.Groups[1].Value;
            if (b.Groups[2].Success
                && ushort.TryParse(b.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort fromComment))
            {
                map[name] = fromComment;
            }
            else if (TryAddrFromRamVarName(name, out ushort fromName))
            {
                map[name] = fromName;
            }
        }
        return map;
    }

    /// <summary>
    /// Resolve <c>buf[x]</c>, <c>buf[y + 1]</c>, or <c>buf[0x01]</c> to absolute / abs,X / abs,Y.
    /// </summary>
    static bool TryResolveBufferIndex(
        string bufName,
        string indexReg,
        string indexPlusLit,
        string absoluteLit,
        Dictionary<string, ushort> consts,
        out EffectiveAddress ea)
    {
        ea = default;
        if (!consts.TryGetValue(bufName, out ushort baseAddr)
            && !TryResolveSimpleAddr(bufName, consts, out baseAddr))
            return false;

        if (!string.IsNullOrEmpty(absoluteLit))
        {
            if (!TryParseOffsetLiteral(absoluteLit, out int off))
                return false;
            ushort abs = (ushort)(baseAddr + off);
            ea = new EffectiveAddress(abs, abs <= 0xFF ? SoftAddrMode.ZeroPage : SoftAddrMode.Absolute);
            if (abs >= 0x100)
                ea = new EffectiveAddress(abs, SoftAddrMode.Absolute);
            return true;
        }

        if (string.IsNullOrEmpty(indexReg))
            return false;

        int plus = 0;
        if (!string.IsNullOrEmpty(indexPlusLit) && !TryParseOffsetLiteral(indexPlusLit, out plus))
            return false;

        ushort addr = (ushort)(baseAddr + plus);
        if (addr <= 0xFF)
            ea = new EffectiveAddress(addr, indexReg == "x" ? SoftAddrMode.ZeroPageX : SoftAddrMode.ZeroPageY);
        else
            ea = new EffectiveAddress(addr, indexReg == "x" ? SoftAddrMode.AbsoluteX : SoftAddrMode.AbsoluteY);
        return true;
    }

    static bool TryParseOffsetLiteral(string text, out int value)
    {
        value = 0;
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(text.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    static bool IsZp00Store(string line) =>
        line.StartsWith("poke(vram_ptr_lo,", StringComparison.Ordinal)
        || line.StartsWith("poke(zp_00,", StringComparison.Ordinal)
        || line.StartsWith("poke((ushort)0x00,", StringComparison.OrdinalIgnoreCase)
        || line.StartsWith("vram_ptr_lo =", StringComparison.Ordinal)
        || line.StartsWith("zp_00 =", StringComparison.Ordinal);

    static bool IsZp01Store(string line) =>
        line.StartsWith("poke(vram_ptr_hi,", StringComparison.Ordinal)
        || line.StartsWith("poke(zp_01,", StringComparison.Ordinal)
        || line.StartsWith("poke((ushort)0x01,", StringComparison.OrdinalIgnoreCase)
        || line.StartsWith("vram_ptr_hi =", StringComparison.Ordinal)
        || line.StartsWith("zp_01 =", StringComparison.Ordinal);

    /// <summary>
    /// Fallback when a <c>byte name; // $addr</c> comment is missing:
    /// zp_/ram_/pad_ hex suffixes, plus known role aliases.
    /// </summary>
    /// <summary>LDA from <c>name</c> or <c>name[x|y(+n)|lit]</c> into A.</summary>
    static bool TryEmitLoadAFromMemExpr(
        string expr, Dictionary<string, ushort> consts, List<AsmItem> items, out string? error)
    {
        error = null;
        expr = expr.Trim();
        var indexed = Regex.Match(expr,
            @"^([A-Za-z_]\w*)\[(?:(x|y)(?:\s*\+\s*(0x[0-9A-Fa-f]+|\d+))?|(0x[0-9A-Fa-f]+|\d+))\]$");
        if (indexed.Success && consts.ContainsKey(indexed.Groups[1].Value))
        {
            if (!TryResolveBufferIndex(
                    indexed.Groups[1].Value,
                    indexed.Groups[2].Value,
                    indexed.Groups[3].Value,
                    indexed.Groups[4].Value,
                    consts,
                    out var ea))
            {
                error = $"load mem {expr}";
                return false;
            }
            items.Add(new AsmItem(EmitLoad("a", ea)));
            return true;
        }
        if (consts.ContainsKey(expr) && TryResolveAddress(expr, consts, out var abs))
        {
            items.Add(new AsmItem(EmitLoad("a", abs)));
            return true;
        }
        return false;
    }

    /// <summary>
    /// Evaluate <c>(byte)(…)</c> / mem / imm ALU trees into A (raised SoftCpu surface — no live <c>a</c>).
    /// Supports nested <c>+</c>/<c>-</c>/<c>*</c>/<c>&amp;</c>/<c>|</c>/<c>^</c> with imm or mem RHS.
    /// </summary>
    static bool TryEmitByteExprIntoA(
        string expr, Dictionary<string, ushort> consts, List<AsmItem> items, out string? error)
    {
        error = null;
        expr = expr.Trim();
        if (expr.Length == 0)
            return false;

        // Unwrap stacked (byte)(…) only when the closing paren matches the cast's '('.
        const string byteWrap = "(byte)(";
        while (expr.StartsWith(byteWrap, StringComparison.Ordinal))
        {
            int open = byteWrap.Length - 1; // '(' of (byte)(
            int depth = 0;
            int close = -1;
            for (int i = open; i < expr.Length; i++)
            {
                char ch = expr[i];
                if (ch == '(') depth++;
                else if (ch == ')')
                {
                    depth--;
                    if (depth == 0)
                    {
                        close = i;
                        break;
                    }
                }
            }
            if (close < 0 || close != expr.Length - 1)
                break;
            string inner = expr.Substring(byteWrap.Length, close - byteWrap.Length).Trim();
            if (inner.Length == 0)
                break;
            expr = inner;
        }

        if (TryParseByteLiteral(expr, out byte imm))
        {
            if (!LastLoadsAWithImm(items, imm))
                items.Add(new AsmItem(Asm.LDA(imm)));
            return true;
        }

        if (expr == "a")
            return true; // A already holds the value (live residual)

        if (expr == "x")
        {
            items.Add(new AsmItem(Asm.TXA()));
            return true;
        }
        if (expr == "y")
        {
            items.Add(new AsmItem(Asm.TYA()));
            return true;
        }

        if (expr is "pla()" or "pop()")
        {
            items.Add(new AsmItem(Asm.PLA()));
            return true;
        }

        if (expr is "PeekVramBufY()")
        {
            items.Add(new AsmItem(Asm.LDA_ind_Y(0)));
            return true;
        }

        if (expr.StartsWith("peek(", StringComparison.Ordinal) && expr.EndsWith(")", StringComparison.Ordinal))
        {
            string addr = expr.Substring("peek(".Length, expr.Length - "peek(".Length - 1).Trim();
            if (!TryResolveAddress(addr, consts, out var peekEa))
            {
                error = $"peek expr {expr}";
                return false;
            }
            items.Add(new AsmItem(EmitLoad("a", peekEa)));
            return true;
        }

        if (TryEmitLoadAFromMemExpr(expr, consts, items, out error))
            return true;

        // Shift: (byte)(src >> n) / (byte)(src << n) → n×LSR/ASL
        if (TrySplitShift(expr, out string shiftLeft, out bool shiftLeftward, out string shiftRight))
        {
            if (!TryEmitByteExprIntoA(shiftLeft, consts, items, out error))
            {
                error ??= $"shift lhs {shiftLeft}";
                return false;
            }
            if (!TryParseByteLiteral(shiftRight, out byte shiftN) || shiftN is 0 or > 7)
            {
                error = $"shift count {shiftRight}";
                return false;
            }
            for (int s = 0; s < shiftN; s++)
                items.Add(new AsmItem(shiftLeftward ? Asm.ASL_A() : Asm.LSR_A()));
            return true;
        }

        // Split top-level binary op (rightmost imm-friendly op outside parens).
        if (!TrySplitBinary(expr, out string left, out char op, out string right))
            return false;

        // … + (carry ? 1 : 0) → ADC (live 6502 carry — no CLC).
        // Must run before emitting left: (expr + imm) + carrybit → emit expr; ADC #imm
        // (not CLC+ADC #imm then ADC #0).
        if (op == '+' && IsCarryBitExpr(right))
        {
            if (TrySplitBinary(left, out string carryLeft, out char carryOp, out string carryRight)
                && carryOp == '+'
                && (TryParseByteLiteral(carryRight, out byte carryImm) || TryEvalEnumByte(carryRight, out carryImm)))
            {
                if (!TryEmitByteExprIntoA(carryLeft, consts, items, out error))
                {
                    error ??= $"adc-carry lhs {carryLeft}";
                    return false;
                }
                items.Add(new AsmItem(Asm.ADC(carryImm)));
                return true;
            }
            if (!TryEmitByteExprIntoA(left, consts, items, out error))
            {
                error ??= $"adc0 lhs {left}";
                return false;
            }
            items.Add(new AsmItem(Asm.ADC(0)));
            return true;
        }

        // … - (carry ? 0 : 1) → SBC (live 6502 carry — no SEC).
        if (op == '-' && IsBorrowBitExpr(right))
        {
            if (TrySplitBinary(left, out string borLeft, out char borOp, out string borRight)
                && borOp == '-')
            {
                if (!TryEmitByteExprIntoA(borLeft, consts, items, out error))
                {
                    error ??= $"sbc-carry lhs {borLeft}";
                    return false;
                }
                if (TryParseByteLiteral(borRight, out byte borImm) || TryEvalEnumByte(borRight, out borImm))
                {
                    items.Add(new AsmItem(Asm.SBC(borImm)));
                    return true;
                }
                if (TryResolveMemEa(borRight, consts, out var borEa))
                {
                    items.Add(new AsmItem(EmitSbc(borEa)));
                    return true;
                }
                error = $"sbc-carry rhs {borRight}";
                return false;
            }
            if (!TryEmitByteExprIntoA(left, consts, items, out error))
            {
                error ??= $"sbc0 lhs {left}";
                return false;
            }
            items.Add(new AsmItem(Asm.SBC(0)));
            return true;
        }

        if (!TryEmitByteExprIntoA(left, consts, items, out error))
        {
            error ??= $"byte expr lhs {left}";
            return false;
        }

        if (TryParseByteLiteral(right, out byte rhs) || TryEvalEnumByte(right, out rhs))
        {
            switch (op)
            {
                case '+':
                    items.Add(new AsmItem(Asm.CLC()));
                    items.Add(new AsmItem(Asm.ADC(rhs)));
                    return true;
                case '-':
                    items.Add(new AsmItem(Asm.SEC()));
                    items.Add(new AsmItem(Asm.SBC(rhs)));
                    return true;
                case '*':
                    return TryEmitMulAConst(rhs, items, out error);
                case '&':
                    items.Add(new AsmItem(Asm.AND(rhs)));
                    return true;
                case '|':
                    items.Add(new AsmItem(Asm.ORA(rhs)));
                    return true;
                case '^':
                    items.Add(new AsmItem(Asm.EOR(rhs)));
                    return true;
                default:
                    error = $"byte expr op {op}";
                    return false;
            }
        }

        // Mem RHS: dest = (byte)(src ±|&|^ mem)
        if (op is '+' or '-' or '&' or '|' or '^'
            && TryResolveMemEa(right, consts, out var ea))
        {
            switch (op)
            {
                case '+':
                    items.Add(new AsmItem(Asm.CLC()));
                    items.Add(new AsmItem(EmitAdc(ea)));
                    return true;
                case '-':
                    items.Add(new AsmItem(Asm.SEC()));
                    items.Add(new AsmItem(EmitSbc(ea)));
                    return true;
                case '&':
                    items.Add(new AsmItem(EmitAnd(ea)));
                    return true;
                case '|':
                    items.Add(new AsmItem(EmitOra(ea)));
                    return true;
                case '^':
                    items.Add(new AsmItem(EmitEor(ea)));
                    return true;
            }
        }

        error = $"byte expr rhs {right}";
        return false;
    }

    static bool TryEmitMulAConst(int factor, List<AsmItem> items, out string? error)
    {
        error = null;
        if (factor is 2 or 4 or 8 or 16 or 32 or 64 or 128)
        {
            int n = 0;
            for (int f = factor; f > 1; f >>= 1)
                n++;
            for (int k = 0; k < n; k++)
                items.Add(new AsmItem(Asm.ASL_A()));
            return true;
        }
        if (factor is not (3 or 5))
        {
            error = $"mul factor {factor}";
            return false;
        }
        items.Add(new AsmItem(Asm.PHA()));
        int shifts = factor == 3 ? 1 : 2;
        for (int k = 0; k < shifts; k++)
            items.Add(new AsmItem(Asm.ASL_A()));
        items.Add(new AsmItem(Asm.CLC()));
        items.Add(new AsmItem(Asm.TSX()));
        items.Add(new AsmItem(Asm.ADC_abs_X(0x0101)));
        items.Add(new AsmItem(Asm.TAY()));
        items.Add(new AsmItem(Asm.PLA()));
        items.Add(new AsmItem(Asm.TYA()));
        return true;
    }

    static bool TryResolveMemEa(
        string expr, Dictionary<string, ushort> consts, out EffectiveAddress ea)
    {
        ea = default;
        expr = expr.Trim();
        var indexed = Regex.Match(expr,
            @"^([A-Za-z_]\w*)\[(?:(x|y)(?:\s*\+\s*(0x[0-9A-Fa-f]+|\d+))?|(0x[0-9A-Fa-f]+|\d+))\]$");
        if (indexed.Success && consts.ContainsKey(indexed.Groups[1].Value))
        {
            return TryResolveBufferIndex(
                indexed.Groups[1].Value,
                indexed.Groups[2].Value,
                indexed.Groups[3].Value,
                indexed.Groups[4].Value,
                consts,
                out ea);
        }
        return consts.ContainsKey(expr) && TryResolveAddress(expr, consts, out ea);
    }

    static bool IsCarryBitExpr(string expr)
    {
        expr = expr.Trim();
        if (expr.StartsWith("(", StringComparison.Ordinal)
            && expr.EndsWith(")", StringComparison.Ordinal)
            && expr.Length >= 2)
            expr = expr.Substring(1, expr.Length - 2).Trim();
        return Regex.IsMatch(expr, @"^carry\s*\?\s*1\s*:\s*0$");
    }

    static bool IsBorrowBitExpr(string expr)
    {
        expr = expr.Trim();
        if (expr.StartsWith("(", StringComparison.Ordinal)
            && expr.EndsWith(")", StringComparison.Ordinal)
            && expr.Length >= 2)
            expr = expr.Substring(1, expr.Length - 2).Trim();
        return Regex.IsMatch(expr, @"^carry\s*\?\s*0\s*:\s*1$");
    }

    /// <summary>
    /// <c>if ((byte)(…) op imm) goto L;</c> / <c>return;</c> with balanced <c>(byte)(…)</c>.
    /// </summary>
    static bool TryMatchIfByteExprCompare(
        string line,
        out bool negated,
        out string byteExpr,
        out string op,
        out string imm,
        out string? gotoLab,
        out bool isReturn)
    {
        negated = false;
        byteExpr = op = imm = "";
        gotoLab = null;
        isReturn = false;
        var head = Regex.Match(line, @"^if\s*\(\s*(!)?");
        if (!head.Success)
            return false;
        negated = head.Groups[1].Success;
        int i = head.Length;
        // Skip whitespace and extra grouping '(' before (byte)(
        while (i < line.Length && (line[i] is ' ' or '\t' or '('))
        {
            if (line.AsSpan(i).StartsWith("(byte)(", StringComparison.Ordinal))
                break;
            if (line[i] == '(')
            {
                i++;
                continue;
            }
            i++;
        }
        if (i + 6 >= line.Length || !line.AsSpan(i).StartsWith("(byte)(", StringComparison.Ordinal))
            return false;
        int open = i + 6; // '(' of (byte)(
        int depth = 0;
        int close = -1;
        for (int j = open; j < line.Length; j++)
        {
            if (line[j] == '(') depth++;
            else if (line[j] == ')')
            {
                depth--;
                if (depth == 0)
                {
                    close = j;
                    break;
                }
            }
        }
        if (close < 0)
            return false;
        byteExpr = line.Substring(i, close - i + 1);
        var tail = Regex.Match(line.Substring(close + 1),
            @"^\s*(==|!=|>=|<=|<|>)\s*(0x[0-9A-Fa-f]+|\d+)\s*\)?\s*\)\s*(goto\s+(L_[0-9A-Fa-f]+)\s*;|return\s*;)\s*$");
        if (!tail.Success)
            return false;
        op = tail.Groups[1].Value;
        imm = tail.Groups[2].Value;
        if (tail.Groups[3].Value.StartsWith("goto", StringComparison.Ordinal))
            gotoLab = tail.Groups[4].Value;
        else
            isReturn = true;
        return true;
    }

    static bool TrySplitBinary(string expr, out string left, out char op, out string right)
    {
        left = right = "";
        op = '\0';
        int depth = 0;
        for (int i = expr.Length - 1; i >= 0; i--)
        {
            char c = expr[i];
            if (c == ')')
                depth++;
            else if (c == '(')
                depth--;
            else if (depth == 0 && (c == '+' || c == '-' || c == '*' || c == '&' || c == '|' || c == '^'))
            {
                // Don't treat leading +/−
                if (i == 0)
                    return false;
                left = expr.Substring(0, i).Trim();
                right = expr.Substring(i + 1).Trim();
                op = c;
                return left.Length > 0 && right.Length > 0;
            }
        }
        return false;
    }

    static bool TrySplitShift(string expr, out string left, out bool leftward, out string right)
    {
        left = right = "";
        leftward = false;
        int depth = 0;
        // Start at Length-1 so a trailing ')' updates depth (Length-2 skipped it and
        // mis-split "(byte)(x >> n)" as shift of "(byte)(x").
        for (int i = expr.Length - 1; i >= 0; i--)
        {
            char c = expr[i];
            if (c == ')')
                depth++;
            else if (c == '(')
                depth--;
            else if (depth == 0 && i + 1 < expr.Length && (c == '<' || c == '>') && expr[i + 1] == c)
            {
                left = expr.Substring(0, i).Trim();
                right = expr.Substring(i + 2).Trim();
                leftward = c == '<';
                return left.Length > 0 && right.Length > 0;
            }
        }
        return false;
    }

    static bool TryAddrFromBufferName(string name, out ushort addr)
    {
        addr = 0;
        if (name == "oam" || name == "oam_y") { addr = 0x0200; return true; }
        if (name == "oam_tile") { addr = 0x0201; return true; }
        if (name == "oam_attr") { addr = 0x0202; return true; }
        if (name == "oam_x") { addr = 0x0203; return true; }
        if (name == "vram_buf") { addr = 0x0300; return true; }
        if (name is "pad_buf" or "game" or "score")
            return false; // require // $addr comment
        if (name.StartsWith("obj_", StringComparison.Ordinal)
            || name.StartsWith("pad_buf_", StringComparison.Ordinal)
            || name.StartsWith("game_", StringComparison.Ordinal)
            || name.StartsWith("score_", StringComparison.Ordinal))
        {
            int under = name.LastIndexOf('_');
            if (under > 0
                && ushort.TryParse(name.Substring(under + 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out addr))
                return true;
        }
        if (name.StartsWith("buf_", StringComparison.Ordinal)
            && ushort.TryParse(name.Substring(4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out addr))
            return true;
        if (name.StartsWith("rom_", StringComparison.Ordinal)
            && ushort.TryParse(name.Substring(4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out addr))
            return true;
        return TryAddrFromRamVarName(name, out addr);
    }

    /// <summary>
    /// <c>switch (a) { case N: …; return; … }</c> → CMP #N / BNE next / body.
    /// </summary>
    static bool TryConsumeSwitch(
        IReadOnlyList<string> body,
        ref int i,
        Dictionary<string, ushort> consts,
        Dictionary<string, ushort> nameToAddress,
        SoftCpuTables tables,
        ref int waitSeq,
        List<AsmItem> items,
        out string? error,
        ref bool padResultOnStack)
    {
        error = null;
        var swHdr = SwitchHeader.Match(body[i].Trim());
        if (!swHdr.Success)
            return false;
        string expr = swHdr.Groups["e"].Success ? swHdr.Groups["e"].Value : swHdr.Groups[1].Value;
        if (i + 1 >= body.Count || body[i + 1].Trim() != "{")
        {
            error = "switch: missing block";
            return true;
        }
        if (!TryExtractBraceBlock(body, i + 1, out int blockEnd, out List<string> arm))
        {
            error = "switch: bad block";
            return true;
        }

        // Raised switch (mem): load into A once; cases CMP A.
        string reg = expr is "a" or "x" or "y" ? expr : "a";
        if (expr is not ("a" or "x" or "y"))
        {
            if (!TryEmitLoadAFromMemExpr(expr, consts, items, out error))
            {
                error ??= $"switch load {expr}";
                return true;
            }
        }

        // Parse case arms from flattened block lines.
        var cases = new List<(byte Imm, List<string> Stmts)>();
        for (int c = 0; c < arm.Count; )
        {
            string t = arm[c].Trim();
            var cm = Regex.Match(t, @"^case\s+(0x[0-9A-Fa-f]+|\d+)\s*:\s*$");
            if (!cm.Success)
            {
                error = $"switch: expected case, got {t}";
                return true;
            }
            if (!TryParseByteLiteral(cm.Groups[1].Value, out byte imm))
            {
                error = $"switch: bad case {cm.Groups[1].Value}";
                return true;
            }
            c++;
            var stmts = new List<string>();
            while (c < arm.Count)
            {
                string s = arm[c].Trim();
                if (s.StartsWith("case ", StringComparison.Ordinal) || s == "default:")
                    break;
                if (s == "break;")
                {
                    c++;
                    break;
                }
                stmts.Add(arm[c]);
                c++;
                if (s is "return;" or "return")
                    break;
            }
            cases.Add((imm, stmts));
        }

        string endLab = $"__sw_end_{waitSeq++}";
        for (int ci = 0; ci < cases.Count; ci++)
        {
            string next = ci + 1 < cases.Count ? $"__sw_{waitSeq}_{ci}" : endLab;
            byte imm = cases[ci].Imm;
            // CMP reg,#imm / BNE next
            if (reg == "a")
                items.Add(new AsmItem(Asm.CMP(imm)));
            else if (reg == "x")
                items.Add(new AsmItem(Asm.CPX(imm)));
            else
                items.Add(new AsmItem(Asm.CPY(imm)));
            items.Add(new AsmItem(Asm.BNE(next)));
            if (!TryLowerArm(cases[ci].Stmts, consts, nameToAddress, tables, ref waitSeq, items, out error, ref padResultOnStack))
                return true;
            string last = cases[ci].Stmts.Count == 0
                ? ""
                : cases[ci].Stmts[cases[ci].Stmts.Count - 1].Trim();
            bool fellThrough = last != "return;" && last != "return";
            if (fellThrough && ci + 1 < cases.Count)
                items.Add(new AsmItem(Asm.JMP(endLab)));
            if (ci + 1 < cases.Count)
                items.Add(new AsmItem(next));
        }
        items.Add(new AsmItem(endLab));
        i = blockEnd - 1;
        return true;
    }

    static bool TryAddrFromRamVarName(string name, out ushort addr)
    {
        addr = 0;
        if (name is "vram_ptr_lo") { addr = 0x00; return true; }
        if (name is "vram_ptr_hi") { addr = 0x01; return true; }
        if (name.StartsWith("zp_", StringComparison.Ordinal)
            && ushort.TryParse(name.Substring(3), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out addr))
            return true;
        if (name.StartsWith("ram_", StringComparison.Ordinal)
            && ushort.TryParse(name.Substring(4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out addr))
            return true;
        if (name.StartsWith("pad_", StringComparison.Ordinal)
            && ushort.TryParse(name.Substring(4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out addr))
            return true;
        // nmi_FE / reset_0778 / f_8EE6_09 — trailing hex after last '_'
        int under = name.LastIndexOf('_');
        if (under > 0 && under < name.Length - 1)
        {
            string hex = name.Substring(under + 1);
            if (hex.Length is >= 1 and <= 4
                && ushort.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out addr))
                return true;
        }
        return false;
    }

    static List<SoftCpuRoutine> ParseRoutines(string programCs)
    {
        var lines = programCs.Replace("\r\n", "\n").Split('\n');
        var routines = new List<SoftCpuRoutine>();
        for (int i = 0; i < lines.Length; i++)
        {
            var hm = RoutineHeader.Match(lines[i].Trim());
            if (!hm.Success)
                continue;
            int bank = int.Parse(hm.Groups[1].Value, CultureInfo.InvariantCulture);
            ushort addr = ushort.Parse(hm.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

            // next non-empty: void name()
            int j = i + 1;
            while (j < lines.Length && string.IsNullOrWhiteSpace(lines[j]))
                j++;
            if (j >= lines.Length)
                break;
            var nm = Regex.Match(lines[j].Trim(), @"^void\s+(\w+)\s*\(\s*\)\s*$");
            if (!nm.Success)
                continue;
            string name = nm.Groups[1].Value;
            j++;
            while (j < lines.Length && string.IsNullOrWhiteSpace(lines[j]))
                j++;
            if (j >= lines.Length || lines[j].Trim() != "{")
                continue;
            j++;
            var body = new List<string>();
            int depth = 1;
            for (; j < lines.Length && depth > 0; j++)
            {
                string t = lines[j];
                string trimmed = t.Trim();
                if (trimmed == "{")
                    depth++;
                else if (trimmed == "}")
                {
                    depth--;
                    if (depth == 0)
                        break;
                }
                if (depth > 0)
                    body.Add(trimmed);
            }
            routines.Add(new SoftCpuRoutine(bank, addr, name, body));
            i = j;
        }
        return routines;
    }

    static byte ParseByte(string text)
    {
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return byte.Parse(text.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return byte.Parse(text, CultureInfo.InvariantCulture);
    }

    static bool TryParseByteLiteral(string text, out byte value)
    {
        value = 0;
        text = text.Trim();
        if (text.StartsWith("(byte)", StringComparison.Ordinal))
            text = text.Substring(6).Trim();
        try
        {
            value = ParseByte(text);
            return true;
        }
        catch
        {
            return false;
        }
    }

    static bool TryEvalEnumByte(string expr, out byte value)
    {
        value = 0;
        expr = expr.Trim();
        bool complement = false;
        if (expr.StartsWith("~", StringComparison.Ordinal))
        {
            complement = true;
            expr = expr.Substring(1).Trim();
        }
        if (expr.StartsWith("(byte)", StringComparison.Ordinal))
            expr = expr.Substring(6).Trim();
        if (expr.StartsWith("~", StringComparison.Ordinal))
        {
            complement = true;
            expr = expr.Substring(1).Trim();
        }
        // Strip a single wrapping ( ... )
        if (expr.StartsWith("(", StringComparison.Ordinal) && expr.EndsWith(")", StringComparison.Ordinal))
            expr = expr.Substring(1, expr.Length - 2).Trim();

        byte acc = 0;
        foreach (string part in expr.Split('|'))
        {
            string p = part.Trim();
            if (p.StartsWith("(byte)", StringComparison.Ordinal))
                p = p.Substring(6).Trim();
            if (!TryEvalEnumOrLiteralAtom(p, out byte bit))
                return false;
            acc |= bit;
        }
        value = complement ? (byte)~acc : acc;
        return true;
    }

    static bool TryEvalEnumOrLiteralAtom(string p, out byte bit)
    {
        bit = 0;
        // Strip (ENUM) casts used in lifted SoftCpu: (APUFRAME)0x3F
        if (p.Length > 3 && p[0] == '(')
        {
            int close = p.IndexOf(')');
            if (close > 1 && close + 1 < p.Length)
            {
                string castName = p.Substring(1, close - 1).Trim();
                if (castName.Length > 0 && char.IsLetter(castName[0]))
                    p = p.Substring(close + 1).Trim();
            }
        }
        // Prefer qualified names: PADMSB.START vs PAD.START (different values).
        if (p.StartsWith("PADMSB.", StringComparison.Ordinal))
        {
            string name = p.Substring("PADMSB.".Length);
            return PadMsbFlags.TryGetValue(name, out bit);
        }
        if (p.StartsWith("PAD.", StringComparison.Ordinal))
        {
            string name = p.Substring("PAD.".Length);
            return PadLsbFlags.TryGetValue(name, out bit);
        }

        int dot = p.LastIndexOf('.');
        string key = dot >= 0 ? p.Substring(dot + 1) : p;
        if (EnumFlags.TryGetValue(key, out bit))
            return true;
        return TryParseByteLiteral(p, out bit);
    }

    static readonly Dictionary<string, byte> PadLsbFlags = new(StringComparer.Ordinal)
    {
        ["A"] = 0x01,
        ["B"] = 0x02,
        ["SELECT"] = 0x04,
        ["START"] = 0x08,
        ["UP"] = 0x10,
        ["DOWN"] = 0x20,
        ["LEFT"] = 0x40,
        ["RIGHT"] = 0x80,
    };

    static readonly Dictionary<string, byte> PadMsbFlags = new(StringComparer.Ordinal)
    {
        ["RIGHT"] = 0x01,
        ["LEFT"] = 0x02,
        ["DOWN"] = 0x04,
        ["UP"] = 0x08,
        ["START"] = 0x10,
        ["SELECT"] = 0x20,
        ["B"] = 0x40,
        ["A"] = 0x80,
    };

    static Dictionary<string, byte> BuildEnumFlags()
    {
        var d = new Dictionary<string, byte>(StringComparer.Ordinal)
        {
            ["NAMETABLE_X"] = 0x01,
            ["NAMETABLE_Y"] = 0x02,
            ["VRAM_INC_32"] = 0x04,
            ["SPR_TABLE_1000"] = 0x08,
            ["BG_TABLE_1000"] = 0x10,
            ["SPR_SIZE_8X16"] = 0x20,
            ["MASTER_SLAVE"] = 0x40,
            ["NMI"] = 0x80,
            ["MONO"] = 0x01,
            ["EDGE_BG"] = 0x02,
            ["EDGE_SPR"] = 0x04,
            ["BG"] = 0x08,
            ["SPR"] = 0x10,
            ["EMPH_RED"] = 0x20,
            ["EMPH_GREEN"] = 0x40,
            ["EMPH_BLUE"] = 0x80,
            ["PULSE1"] = 0x01,
            ["PULSE2"] = 0x02,
            ["TRIANGLE"] = 0x04,
            ["NOISE"] = 0x08,
            ["DMC"] = 0x10,
            ["IRQ_INHIBIT"] = 0x40,
            ["MODE_5STEP"] = 0x80,
            ["Latch"] = 0x01,
            ["Shift"] = 0x00,
        };
        return d;
    }

    readonly record struct SoftCpuRoutine(int Bank, ushort Address, string Name, List<string> Body);

    readonly record struct SoftCpuTables(
        IReadOnlyList<PaletteRow> Palettes,
        IReadOnlyDictionary<string, VramPtrTable> VramPointers);

    readonly record struct PaletteRow(ushort Base, byte[] Data);

    readonly record struct VramPtrTable(ushort LoAddr, ushort HiAddr, ushort[] Entries);

    readonly record struct EffectiveAddress(ushort Address, SoftAddrMode Mode);

    enum SoftAddrMode
    {
        Absolute,
        AbsoluteX,
        AbsoluteY,
        ZeroPage,
        ZeroPageX,
        ZeroPageY,
    }

    sealed class AsmItem
    {
        public string? Label { get; }
        public Instruction? Instruction { get; }
        public AsmItem(string label) { Label = label; }
        public AsmItem(Instruction instruction) { Instruction = instruction; }
    }
}

sealed class SoftCpuResult
{
    public SoftCpuResult(byte[] prgRom, int routinesLowered, int routinesSkipped, IReadOnlyList<string> diagnostics)
    {
        PrgRom = prgRom;
        RoutinesLowered = routinesLowered;
        RoutinesSkipped = routinesSkipped;
        Diagnostics = diagnostics;
    }

    public byte[] PrgRom { get; }
    public int RoutinesLowered { get; }
    public int RoutinesSkipped { get; }
    public IReadOnlyList<string> Diagnostics { get; }
}
