using System.Text;
using dotnes.Analysis;
using dotnes.ObjectModel;
using NES;

namespace dotnes;

/// <summary>
/// Recovers high-level NESLib C# from a dotnes-layout ROM.
/// </summary>
sealed class SemanticRecoverer
{
    readonly NESRomReader _rom;
    readonly ILogger _logger;
    readonly Dictionary<ushort, string> _symbolTable = new();
    readonly ArrayRecovery _arrays = new();
    readonly Dictionary<ushort, string> _localVariables = new();
    readonly HashSet<ushort> _padVariables = new(); // Locals that store pad_poll results (typed as PAD)
    readonly List<ForLoopInfo> _forLoops = new();
    readonly HashSet<ushort> _forLoopLocals = new();
    readonly List<(ushort PpuAddress, byte[] Data)> _chrRamTileData = new();
    readonly List<(ushort Address, string Name)> _userFunctionAddresses = new();
    readonly HashSet<string> _userFunctionNames = new();

    ushort _mainAddress;
    ushort _mainEnd;
    int _dataCounter;
    ushort? _lastVramAdrTarget;
    byte? _ppuAddrHigh; // Tracks first byte written to PPU_ADDR ($2006) for CHR RAM detection

    record ForLoopInfo(
        ushort LocalAddress,     // counter variable address ($03xx)
        byte InitValue,          // initial value (usually 0)
        byte Limit,              // upper bound (N in i < N)
        int InitIndex,           // index of LDA #imm (first init instruction)
        int BodyStartIndex,      // index of first body instruction
        int FooterStartIndex,    // index of first footer instruction
        int AfterLoopIndex       // index of first instruction after BCC
    );

    public SemanticRecoverer(NESRomReader rom, ILogger? logger = null)
    {
        _rom = rom;
        _logger = logger ?? new NullLogger();
    }

    /// <summary>
    /// Decompile the ROM and return C# source code.
    /// </summary>
    public string Decompile()
    {
        _logger.WriteLine($"Decompiling ROM: PRG={_rom.PrgBanks} banks, CHR={_rom.ChrBanks} banks, Mapper={_rom.Mapper}");
        _logger.WriteLine($"Vectors: NMI=${_rom.NmiVector:X4}, RESET=${_rom.ResetVector:X4}, IRQ=${_rom.IrqVector:X4}");

        // Phase 1: Build symbol table by assembling the known built-in subroutines
        // and reading addresses from the reference program layout
        BuildSymbolTable();

        // Phase 2: Find and decompile the main program
        var mainCode = DecompileMain();

        // Phase 3: Detect and decompile user-defined functions
        var userFunctions = DecompileUserFunctions();

        // Phase 4: Generate C# source
        return GenerateCSharp(mainCode, userFunctions);
    }

    /// <summary>
    /// Build a symbol table by assembling the known built-in subroutines at $8000,
    /// finding the main address from the initlib JSR target, then scanning the
    /// final built-ins after main to identify runtime support subroutines.
    /// </summary>
    void BuildSymbolTable()
    {
        _logger.WriteLine($"Building symbol table from built-in subroutines...");

        // Build the initial built-in subroutines to get their addresses
        var program = Program6502.CreateWithBuiltIns();
        program.ResolveAddresses();

        var labels = program.GetLabels();
        ushort builtInsEnd = (ushort)(0x8000 + program.TotalSize);

        // Map all initial built-in labels to addresses
        foreach (var kvp in labels)
        {
            if (kvp.Value != 0)
                _symbolTable[kvp.Value] = kvp.Key;
        }

        _logger.WriteLine($"Initial built-ins end at ${builtInsEnd:X4}");

        // Find main address by reading the JMP target from the detectNTSC block.
        // The startup sequence ends with detectNTSC which has a JMP to main as its last instruction.
        if (labels.TryGetValue("detectNTSC", out ushort detectAddr))
        {
            int detectOffset = detectAddr - 0x8000;
            int blockSize = program.Blocks.FirstOrDefault(b => b.Label == "detectNTSC")?.Size ?? 0;

            // Scan the detectNTSC block for JMP (0x4C) targeting an address >= builtInsEnd
            if (blockSize > 0)
            {
                int blockEnd = detectOffset + blockSize;
                for (int i = detectOffset; i < blockEnd && i < _rom.PrgRom.Length - 2; i++)
                {
                    if (_rom.PrgRom[i] == 0x4C) // JMP absolute
                    {
                        ushort target = (ushort)(_rom.PrgRom[i + 1] | (_rom.PrgRom[i + 2] << 8));
                        if (target >= builtInsEnd && target < 0xFFFA)
                        {
                            _mainAddress = target;
                            _symbolTable[target] = "main";
                            _logger.WriteLine($"Found main at ${_mainAddress:X4}");
                            break;
                        }
                    }
                }
            }
        }

        if (_mainAddress == 0)
        {
            _mainAddress = builtInsEnd;
            _symbolTable[_mainAddress] = "main";
            _logger.WriteLine($"Main address (fallback) at ${_mainAddress:X4}");
        }

        // Scan for the end of main (JMP to self, backward JMP game loop, or first RTS after main)
        _mainEnd = FindMainEnd();

        // Identify final built-ins after main by scanning JSR targets from within main
        // and matching byte patterns at those addresses
        IdentifyFinalBuiltIns(_mainEnd);

        // Detect user-defined functions: unresolved JSR targets from main that
        // point to addresses after main (between main and the final built-ins)
        DetectUserFunctions();
    }

    /// <summary>
    /// Find the end of the main function by scanning for infinite loop patterns:
    /// 1. JMP to self (while(true);) - simple infinite loop
    /// 2. Backward JMP to an address within main (while(true){body}) - game loop
    ///    detected as the last backward JMP before the first RTS instruction
    /// </summary>
    ushort FindMainEnd()
    {
        int offset = _mainAddress - 0x8000;
        ushort address = _mainAddress;
        ushort lastBackwardJmpEnd = 0;

        while (offset < _rom.PrgRom.Length - 2)
        {
            byte opcode = _rom.PrgRom[offset];

            if (opcode == 0x4C) // JMP absolute
            {
                ushort target = (ushort)(_rom.PrgRom[offset + 1] | (_rom.PrgRom[offset + 2] << 8));

                // JMP to self = while(true);
                if (target == address)
                    return (ushort)(address + 3);

                // Backward JMP within main = potential while(true){body} game loop
                if (target >= _mainAddress && target < address)
                    lastBackwardJmpEnd = (ushort)(address + 3);
            }

            // RTS marks the end of a subroutine. If we've seen a backward JMP,
            // we've passed the game loop and entered a user function.
            if (opcode == 0x60 && lastBackwardJmpEnd != 0) // RTS
            {
                _logger.WriteLine($"Found game loop end at ${lastBackwardJmpEnd - 3:X4} (backward JMP before RTS at ${address:X4})");
                return lastBackwardJmpEnd;
            }

            int size = GetInstructionSize(opcode);
            offset += size;
            address += (ushort)size;
        }

        // If we found a backward JMP but no RTS, still use it
        if (lastBackwardJmpEnd != 0)
            return lastBackwardJmpEnd;

        return address;
    }

    /// <summary>
    /// Identify final built-in subroutines (popa, pusha, popax, etc.) that appear after main.
    /// Uses byte-pattern matching against the first few bytes of known subroutines.
    /// </summary>
    void IdentifyFinalBuiltIns(ushort searchStart)
    {
        // Collect all JSR targets from the entire main block so we know what addresses to identify
        var jsrTargets = new HashSet<ushort>();
        int offset = _mainAddress - 0x8000;
        ushort address = _mainAddress;

        // Scan main for JSR targets
        while (address < searchStart && offset < _rom.PrgRom.Length - 2)
        {
            if (_rom.PrgRom[offset] == 0x20) // JSR
            {
                ushort target = (ushort)(_rom.PrgRom[offset + 1] | (_rom.PrgRom[offset + 2] << 8));
                if (!_symbolTable.ContainsKey(target))
                    jsrTargets.Add(target);
            }
            int size = GetInstructionSize(_rom.PrgRom[offset]);
            offset += size;
            address += (ushort)size;
        }

        // Also scan the area after main for JSR targets (final built-ins may call each other)
        offset = searchStart - 0x8000;
        address = searchStart;
        int scanEnd = Math.Min(_rom.PrgRom.Length, (_rom.PrgBanks * 16384) - 6);
        while (offset < scanEnd - 2)
        {
            if (_rom.PrgRom[offset] == 0x20) // JSR
            {
                ushort target = (ushort)(_rom.PrgRom[offset + 1] | (_rom.PrgRom[offset + 2] << 8));
                if (!_symbolTable.ContainsKey(target))
                    jsrTargets.Add(target);
            }
            byte opcode = _rom.PrgRom[offset];
            int size = GetInstructionSize(opcode);
            offset += size;
            address += (ushort)size;
        }

        // Try to match each JSR target against known patterns
        foreach (ushort target in jsrTargets)
        {
            if (_symbolTable.ContainsKey(target))
                continue;

            int targetOffset = target - 0x8000;
            if (targetOffset < 0 || targetOffset >= _rom.PrgRom.Length - 6)
                continue;

            foreach (var (name, sig) in SignatureDatabase.FinalBuiltIns)
            {
                if (_symbolTable.ContainsValue(name))
                    continue;

                if (SignatureDatabase.Matches(_rom.PrgRom, targetOffset, sig))
                {
                    _symbolTable[target] = name;
                    _logger.WriteLine($"  Found {name} at ${target:X4}");
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Detect user-defined functions by scanning main for JSR targets that are
    /// not in the symbol table and point to addresses after main. These are
    /// subroutines written by the user (not built-in NESLib subroutines).
    /// </summary>
    void DetectUserFunctions()
    {
        int offset = _mainAddress - 0x8000;
        ushort address = _mainAddress;
        var candidates = new SortedSet<ushort>();

        while (address < _mainEnd && offset < _rom.PrgRom.Length - 2)
        {
            if (_rom.PrgRom[offset] == 0x20) // JSR
            {
                ushort target = (ushort)(_rom.PrgRom[offset + 1] | (_rom.PrgRom[offset + 2] << 8));
                if (target < 0x8000)
                {
                    // Skip JSR into RAM — not a PRG ROM subroutine
                }
                else if (!_symbolTable.ContainsKey(target) && target >= _mainEnd)
                {
                    int targetOffset = target - 0x8000;
                    if (_rom.PrgRom.Length == 0x4000)
                        targetOffset %= 0x4000;
                    if (targetOffset >= 0 && targetOffset < _rom.PrgRom.Length)
                        candidates.Add(target);
                }
            }
            int size = GetInstructionSize(_rom.PrgRom[offset]);
            offset += size;
            address += (ushort)size;
        }

        foreach (var target in candidates)
        {
            string name = $"func_{target:X4}";
            _symbolTable[target] = name;
            _userFunctionAddresses.Add((target, name));
            _userFunctionNames.Add(name);
            _logger.WriteLine($"  Detected user function '{name}' at ${target:X4}");
        }
    }

    /// <summary>
    /// Decompile all detected user-defined functions.
    /// Returns a list of (name, statements) pairs in address order.
    /// </summary>
    List<(string Name, List<string> Statements)> DecompileUserFunctions()
    {
        var result = new List<(string Name, List<string> Statements)>();

        foreach (var (addr, name) in _userFunctionAddresses)
        {
            _logger.WriteLine($"Decompiling user function '{name}' at ${addr:X4}...");
            var statements = DecompileBlock(addr);
            result.Add((name, statements));
            _logger.WriteLine($"  {statements.Count} statements");
        }

        return result;
    }

    /// <summary>
    /// Decompile a block of 6502 code starting at the given address, ending at RTS.
    /// Used for user-defined function bodies.
    /// </summary>
    List<string> DecompileBlock(ushort startAddress)
    {
        // Collect instructions from start to RTS
        var instructions = new List<(ushort Address, byte Opcode, byte? Op1, byte? Op2)>();
        int offset = startAddress - 0x8000;
        // NROM-128 (16KB PRG): $C000-$FFFF mirrors $8000-$BFFF
        if (_rom.PrgRom.Length == 0x4000)
            offset %= 0x4000;
        ushort addr = startAddress;

        while (offset < _rom.PrgRom.Length)
        {
            byte opcode = _rom.PrgRom[offset];
            int size = GetInstructionSize(opcode);
            byte? op1 = size >= 2 && offset + 1 < _rom.PrgRom.Length ? _rom.PrgRom[offset + 1] : null;
            byte? op2 = size >= 3 && offset + 2 < _rom.PrgRom.Length ? _rom.PrgRom[offset + 2] : null;
            instructions.Add((addr, opcode, op1, op2));

            if (opcode == 0x60) break; // RTS marks end of function

            offset += size;
            addr += (ushort)size;
        }

        return ProcessInstructions(instructions, startAddress);
    }

    /// <summary>
    /// Decompile the main program block into a list of C# statements.
    /// Recognizes multi-instruction patterns like LDA #arg / JSR pusha / LDA #arg / JSR pal_col.
    /// </summary>
    List<string> DecompileMain()
    {
        int offset = _mainAddress - 0x8000;
        ushort address = _mainAddress;

        _logger.WriteLine($"Decompiling main at ${address:X4} (offset 0x{offset:X4})...");

        // Collect a flat list of decoded instructions first
        var instructions = new List<(ushort Address, byte Opcode, byte? Op1, byte? Op2)>();
        int tempOffset = offset;
        ushort tempAddr = address;

        while (tempOffset < _rom.PrgRom.Length)
        {
            byte opcode = _rom.PrgRom[tempOffset];
            int size = GetInstructionSize(opcode);

            byte? op1 = size >= 2 && tempOffset + 1 < _rom.PrgRom.Length ? _rom.PrgRom[tempOffset + 1] : null;
            byte? op2 = size >= 3 && tempOffset + 2 < _rom.PrgRom.Length ? _rom.PrgRom[tempOffset + 2] : null;

            instructions.Add((tempAddr, opcode, op1, op2));

            // Stop at JMP to self (while(true);)
            if (opcode == 0x4C && op1.HasValue && op2.HasValue)
            {
                ushort target = (ushort)(op1.Value | (op2.Value << 8));
                if (target == tempAddr) break;
            }

            tempOffset += size;
            tempAddr += (ushort)size;

            // Stop if we've reached the detected end of main
            // (_mainEnd is always >= $8000 when set, so 0 is a safe sentinel for "not set")
            if (_mainEnd != 0 && tempAddr >= _mainEnd) break;
        }

        // Detect for-loop structures first — array sizing uses loop bounds
        DetectForLoops(instructions);

        // Detect byte[] / ROM table shapes from indexed addressing + loop bounds
        _arrays.Analyze(
            instructions,
            _forLoops.Select(f => (f.LocalAddress, f.InitValue, f.Limit, f.InitIndex, f.BodyStartIndex, f.FooterStartIndex, f.AfterLoopIndex)).ToList(),
            NESConstants.LocalStackBase);

        // Detect local variable usage (non-indexed STA/LDA to $0325+ not claimed by arrays)
        DetectLocalVariables(instructions);

        if (instructions.Count > 0)
            _logger.WriteLine($"Collected {instructions.Count} instructions (${_mainAddress:X4}-${instructions[^1].Address:X4})");

        var statements = ProcessInstructions(instructions, _mainAddress);

        // NES programs must end with an infinite loop; ensure one is present
        if (statements.Count == 0 || statements[^1] != "while (true) ;")
            statements.Add("while (true) ;");

        return statements;
    }

    /// <summary>
    /// Process a list of 6502 instructions and return decompiled C# statements.
    /// Shared by DecompileMain (for the main program) and DecompileBlock (for user functions).
    /// </summary>
    List<string> ProcessInstructions(
        List<(ushort Address, byte Opcode, byte? Op1, byte? Op2)> instructions,
        ushort blockStart)
    {
        var statements = new List<string>();

        // Build lookup tables for for-loop init and footer indices
        var forLoopByInit = new Dictionary<int, ForLoopInfo>();
        var forLoopByFooter = new Dictionary<int, ForLoopInfo>();
        foreach (var fl in _forLoops)
        {
            forLoopByInit[fl.InitIndex] = fl;
            forLoopByFooter[fl.FooterStartIndex] = fl;
        }

        // Now walk through instructions with look-ahead for pattern matching
        var pushedArgs = new Stack<string>();   // Track arg representations pushed via pusha (immediate or local var name)
        var pushedWords = new Stack<ushort>(); // Track values pushed via pushax (16-bit)
        byte? lastImmediateA = null;           // Track last LDA #imm value for consecutive poke detection
        List<string>? decspArgs = null;        // Non-null when collecting args after JSR decsp4
        string? pendingDecspValue = null;      // Value loaded for next STA ($22),Y in decsp4 block
        string? pendingDecspIndexVar = null;   // Variable name from LDX $abs for array index resolution
        var unknownInstructions = new List<(ushort Address, byte Opcode, byte? Op1, byte? Op2)>();
        string? lastLoadedVarName = null;      // Track name of last local variable loaded into A
        ushort? lastLoadedVarAddr = null;       // Track address of last local variable loaded into A
        byte? lastCmpValue = null;             // Track value from last CMP #imm instruction
        byte? lastAndMask = null;              // Track AND #imm mask for subsequent branch conditions
        var pendingCloseBraces = new Stack<ushort>(); // Target addresses where if-blocks end

        for (int i = 0; i < instructions.Count; i++)
        {
            // Check if this is a for-loop init (LDA #imm / STA $local)
            if (forLoopByInit.TryGetValue(i, out var loopInit))
            {
                var varName = _localVariables[loopInit.LocalAddress];
                statements.Add($"for (byte {varName} = {loopInit.InitValue}; {varName} < {loopInit.Limit}; {varName}++)");
                statements.Add("{");
                i = loopInit.BodyStartIndex - 1; // -1 because the for loop will i++
                lastImmediateA = null;
                lastLoadedVarName = null; lastLoadedVarAddr = null;
                lastCmpValue = null;
                continue;
            }

            // Check if this is a for-loop footer (LDA $local / CLC / ADC / STA / CMP / BCC)
            if (forLoopByFooter.TryGetValue(i, out var loopFooter))
            {
                statements.Add("}");
                i = loopFooter.AfterLoopIndex - 1; // -1 because the for loop will i++
                lastImmediateA = null;
                lastLoadedVarName = null; lastLoadedVarAddr = null;
                lastCmpValue = null;
                continue;
            }

            var (instrAddr, opcode, op1, op2) = instructions[i];

            // Emit closing braces for any if-blocks that end at this address
            while (pendingCloseBraces.Count > 0 && pendingCloseBraces.Peek() == instrAddr)
            {
                pendingCloseBraces.Pop();
                statements.Add("}");
            }

            // === decsp4 stack args collection ===
            // When inside a decsp4 block, intercept instructions that build stack arguments
            // Pattern: JSR decsp4 / (LDA val / [LDY #3|DEY] / STA ($22),Y) × 4 / LDA val / JSR <sub>
            if (decspArgs != null)
            {
                // STA ($22),Y → collect the pending value as a stack arg
                if (opcode == 0x91 && op1 == 0x22 && pendingDecspValue != null)
                {
                    decspArgs.Add(pendingDecspValue);
                    pendingDecspValue = null;
                    continue;
                }

                // DEY or LDY → skip (stack index management)
                if (opcode == 0x88 || opcode == 0xA0)
                    continue;

                // LDX $abs (0xAE) → track the index variable for subsequent LDA $abs,X array access
                if (opcode == 0xAE && op1.HasValue && op2.HasValue)
                {
                    ushort ldxAddr = (ushort)(op1.Value | (op2.Value << 8));
                    pendingDecspIndexVar = _localVariables.TryGetValue(ldxAddr, out var xn) ? xn : $"var_{ldxAddr:X4}";
                    continue;
                }

                // LDA $abs,X (0xBD) → indexed array access (value for STA ($22),Y or A arg)
                if (opcode == 0xBD && op1.HasValue && op2.HasValue)
                {
                    ushort baseAddr = (ushort)(op1.Value | (op2.Value << 8));
                    string indexName = pendingDecspIndexVar ?? "i";
                    string valStr = $"{_arrays.ArrayName(baseAddr)}[{indexName}]";
                    pendingDecspIndexVar = null;
                    if (i + 1 < instructions.Count && TryMatchDecspCall(instructions[i + 1], out var callName2))
                    {
                        var stmt = DecompileCallN(callName2, valStr, decspArgs);
                        if (stmt != null) statements.Add(stmt);
                        decspArgs = null;
                        pendingDecspValue = null;
                        pendingDecspIndexVar = null;
                        lastImmediateA = null;
                        i++;
                        continue;
                    }
                    pendingDecspValue = valStr;
                    continue;
                }

                // STA $zp (0x85) → skip (zero-page store, e.g. result storage after JSR)
                if (opcode == 0x85)
                    continue;

                // BEQ/BNE/BCC/BCS forward branches → skip (conditional within decsp4 block)
                if (opcode is 0xF0 or 0xD0 or 0x90 or 0xB0)
                    continue;

                // LDA #imm → value for STA ($22),Y or final A argument
                if (opcode == 0xA9 && op1.HasValue)
                {
                    if (i + 1 < instructions.Count && TryMatchDecspCall(instructions[i + 1], out var callName))
                    {
                        var stmt = DecompileCallN(callName, $"0x{op1.Value:X2}", decspArgs);
                        if (stmt != null) statements.Add(stmt);
                        decspArgs = null;
                        pendingDecspValue = null;
                        pendingDecspIndexVar = null;
                        lastImmediateA = null;
                        i++;
                        continue;
                    }
                    pendingDecspValue = op1.Value.ToString();
                    continue;
                }

                // LDA $abs → value from local variable or absolute address
                if (opcode == 0xAD && op1.HasValue && op2.HasValue)
                {
                    ushort addr = (ushort)(op1.Value | (op2.Value << 8));
                    string valStr = _localVariables.TryGetValue(addr, out var vn) ? vn : $"var_{addr:X4}";
                    if (i + 1 < instructions.Count && TryMatchDecspCall(instructions[i + 1], out var callName))
                    {
                        var stmt = DecompileCallN(callName, valStr, decspArgs);
                        if (stmt != null) statements.Add(stmt);
                        decspArgs = null;
                        pendingDecspValue = null;
                        pendingDecspIndexVar = null;
                        lastImmediateA = null;
                        i++;
                        continue;
                    }
                    pendingDecspValue = valStr;
                    continue;
                }

                // LDA $zp → value from zero-page variable
                if (opcode == 0xA5 && op1.HasValue)
                {
                    string valStr = $"var_{op1.Value:X4}";
                    if (i + 1 < instructions.Count && TryMatchDecspCall(instructions[i + 1], out var callName))
                    {
                        var stmt = DecompileCallN(callName, valStr, decspArgs);
                        if (stmt != null) statements.Add(stmt);
                        decspArgs = null;
                        pendingDecspValue = null;
                        pendingDecspIndexVar = null;
                        lastImmediateA = null;
                        i++;
                        continue;
                    }
                    pendingDecspValue = valStr;
                    continue;
                }

                // Unexpected instruction in decsp4 block → bail out
                decspArgs = null;
                pendingDecspValue = null;
                pendingDecspIndexVar = null;
            }

            // RTS = end of function (user-defined functions end here)
            if (opcode == 0x60)
                break;

            // Pattern: JMP to self = while (true) ;
            if (opcode == 0x4C && op1.HasValue && op2.HasValue)
            {
                ushort target = (ushort)(op1.Value | (op2.Value << 8));
                if (target == instrAddr)
                {
                    FlushUnknownInstructions(unknownInstructions, statements);
                    statements.Add("while (true) ;");
                    break;
                }
                // Last instruction is a backward JMP within the block = while (true) { body } game loop
                if (i == instructions.Count - 1 && target >= blockStart && target < instrAddr)
                {
                    FlushUnknownInstructions(unknownInstructions, statements);
                    statements.Add("while (true) ;");
                    break;
                }
            }

            // Pattern: LDA #imm / JSR pusha → push 8-bit arg for a multi-arg call
            if (opcode == 0xA9 && op1.HasValue && i + 1 < instructions.Count)
            {
                var next = instructions[i + 1];
                if (next.Opcode == 0x20 && IsSubroutine(next, "pusha"))
                {
                    pushedArgs.Push(op1.Value.ToString());
                    lastImmediateA = null; // JSR modifies A
                    lastLoadedVarName = null; lastLoadedVarAddr = null;
                    lastCmpValue = null;
                    lastAndMask = null;
                    i++; // skip the JSR pusha
                    continue;
                }
            }

            // Pattern: LDA $local / JSR pusha → push local variable name for a multi-arg call
            if (opcode == 0xAD && op1.HasValue && op2.HasValue && i + 1 < instructions.Count)
            {
                ushort addr = (ushort)(op1.Value | (op2.Value << 8));
                if (_localVariables.TryGetValue(addr, out var varName))
                {
                    var next = instructions[i + 1];
                    if (next.Opcode == 0x20 && IsSubroutine(next, "pusha"))
                    {
                        pushedArgs.Push(varName);
                        lastImmediateA = null; // JSR modifies A
                        i++; // skip the JSR pusha
                        continue;
                    }
                }
            }

            // Pattern: LDA #lo / LDX #hi / JSR pushax → push 16-bit pointer
            // Pattern: LDA #lo / LDX #hi / JSR <fastcall subroutine> → call with pointer in A:X
            if (opcode == 0xA9 && op1.HasValue && i + 2 < instructions.Count)
            {
                var nextLdx = instructions[i + 1];
                var nextJsr = instructions[i + 2];
                if (nextLdx.Opcode == 0xA2 && nextLdx.Op1.HasValue
                    && nextJsr.Opcode == 0x20 && nextJsr.Op1.HasValue && nextJsr.Op2.HasValue)
                {
                    ushort jsrTarget = (ushort)(nextJsr.Op1.Value | (nextJsr.Op2.Value << 8));
                    if (_symbolTable.TryGetValue(jsrTarget, out var targetName))
                    {
                        if (targetName == "pushax")
                        {
                            ushort value16 = (ushort)(op1.Value | (nextLdx.Op1.Value << 8));
                            pushedWords.Push(value16);
                            lastImmediateA = null; // JSR modifies A
                            lastLoadedVarName = null; lastLoadedVarAddr = null;
                            lastCmpValue = null;
                            lastAndMask = null;
                            i += 2; // skip LDX and JSR pushax
                            continue;
                        }
                        if (targetName != "pusha")
                        {
                            // Fastcall: pointer in A:X (LDA=lo, LDX=hi)
                            // Don't pop stacks — fastcall targets receive args in registers, not on cc65 stack
                            byte aVal = op1.Value;
                            byte xVal = nextLdx.Op1.Value;
                            var stmts = DecompileCallXA(targetName, xVal, aVal, null, null);
                            FlushUnknownInstructions(unknownInstructions, statements);
                            statements.AddRange(stmts);
                            lastImmediateA = null; // JSR modifies A
                            lastLoadedVarName = null; lastLoadedVarAddr = null;
                            lastCmpValue = null;
                            lastAndMask = null;
                            i += 2;
                            continue;
                        }
                    }
                }
            }

            // Pattern: LDX #hi / LDA #lo / JSR <subroutine> → call with 16-bit arg in X:A
            if (opcode == 0xA2 && op1.HasValue && i + 2 < instructions.Count)
            {
                var nextLda = instructions[i + 1];
                var nextJsr = instructions[i + 2];
                if (nextLda.Opcode == 0xA9 && nextLda.Op1.HasValue
                    && nextJsr.Opcode == 0x20 && nextJsr.Op1.HasValue && nextJsr.Op2.HasValue)
                {
                    ushort jsrTarget = (ushort)(nextJsr.Op1.Value | (nextJsr.Op2.Value << 8));
                    if (_symbolTable.TryGetValue(jsrTarget, out var name) && name != "pusha" && name != "pushax")
                    {
                        byte xVal = op1.Value;
                        byte aVal = nextLda.Op1.Value;
                        ushort? pw = pushedWords.Count > 0 ? pushedWords.Pop() : null;
                        string? pbStr = pushedArgs.Count > 0 ? pushedArgs.Pop() : null;
                        byte? pb = pbStr != null && byte.TryParse(pbStr, out var pbVal) ? pbVal : (byte?)null;
                        var stmts = DecompileCallXA(name, xVal, aVal, pw, pb);
                        FlushUnknownInstructions(unknownInstructions, statements);
                        statements.AddRange(stmts);
                        lastImmediateA = null; // JSR modifies A
                        lastLoadedVarName = null; lastLoadedVarAddr = null;
                        lastCmpValue = null;
                        lastAndMask = null;
                        i += 2; // skip LDA and JSR
                        continue;
                    }
                }
            }

            // Pattern: LDA #imm / JSR <returning subroutine> / STA $local → var = call(arg)
            if (opcode == 0xA9 && op1.HasValue && i + 2 < instructions.Count)
            {
                var jsrInstr = instructions[i + 1];
                var staInstr = instructions[i + 2];
                if (jsrInstr.Opcode == 0x20 && jsrInstr.Op1.HasValue && jsrInstr.Op2.HasValue
                    && staInstr.Opcode == 0x8D && staInstr.Op1.HasValue && staInstr.Op2.HasValue)
                {
                    ushort jsrTarget = (ushort)(jsrInstr.Op1.Value | (jsrInstr.Op2.Value << 8));
                    if (_symbolTable.TryGetValue(jsrTarget, out var name) && IsReturningFunction(name))
                    {
                        ushort stAddr = (ushort)(staInstr.Op1.Value | (staInstr.Op2.Value << 8));
                        if (_localVariables.TryGetValue(stAddr, out var varName))
                        {
                            var callExpr = FormatCallExpression(name, op1.Value);
                            statements.Add($"{varName} = {callExpr};");
                            if (name == "pad_poll")
                                _padVariables.Add(stAddr);
                            lastImmediateA = null;
                            lastLoadedVarName = null; lastLoadedVarAddr = null;
                            lastCmpValue = null;
                            lastAndMask = null;
                            i += 2; // skip JSR and STA
                            continue;
                        }
                    }
                }
            }

            // Pattern: LDA #imm / JSR <subroutine> → call with immediate arg in A
            if (opcode == 0xA9 && op1.HasValue && i + 1 < instructions.Count)
            {
                var next = instructions[i + 1];
                if (next.Opcode == 0x20 && next.Op1.HasValue && next.Op2.HasValue)
                {
                    ushort jsrTarget = (ushort)(next.Op1.Value | (next.Op2.Value << 8));
                    if (_symbolTable.TryGetValue(jsrTarget, out var name))
                    {
                        var allPushed = DrainPushedArgs(pushedArgs);
                        if (allPushed.Count <= 1 && allPushed.All(a => byte.TryParse(a, out _)))
                        {
                            // Simple case: 0-1 immediate pushed args — use existing byte-based handler
                            byte? pb = allPushed.Count > 0 ? byte.Parse(allPushed[0]) : null;
                            var stmt = DecompileCall(name, op1.Value, pb);
                            if (stmt != null)
                            {
                                FlushUnknownInstructions(unknownInstructions, statements);
                                statements.Add(stmt);
                            }
                        }
                        else
                        {
                            // Multi-arg or variable args — use N-arg handler
                            var stmt = DecompileCallN(name, $"0x{op1.Value:X2}", allPushed);
                            if (stmt != null)
                            {
                                FlushUnknownInstructions(unknownInstructions, statements);
                                statements.Add(stmt);
                            }
                        }
                        lastImmediateA = null; // JSR modifies A
                        lastLoadedVarName = null; lastLoadedVarAddr = null;
                        lastCmpValue = null;
                        lastAndMask = null;
                        i++; // skip the JSR
                        continue;
                    }
                }
            }

            // Pattern: LDA #imm / STA $local → local variable assignment
            if (opcode == 0xA9 && op1.HasValue && i + 1 < instructions.Count)
            {
                var next = instructions[i + 1];
                if (next.Opcode == 0x8D && next.Op1.HasValue && next.Op2.HasValue)
                {
                    ushort addr = (ushort)(next.Op1.Value | (next.Op2.Value << 8));
                    if (_localVariables.TryGetValue(addr, out var varName))
                    {
                        FlushUnknownInstructions(unknownInstructions, statements);
                        statements.Add($"{varName} = 0x{op1.Value:X2};");
                        lastImmediateA = op1.Value;
                        lastLoadedVarName = null; lastLoadedVarAddr = null;
                        lastCmpValue = null;
                        lastAndMask = null;
                        i++; // skip the STA
                        continue;
                    }
                }
            }

            // Pattern: STA $local (consecutive — A still holds last immediate value)
            if (opcode == 0x8D && op1.HasValue && op2.HasValue && lastImmediateA.HasValue)
            {
                ushort addr = (ushort)(op1.Value | (op2.Value << 8));
                if (_localVariables.TryGetValue(addr, out var varName))
                {
                    FlushUnknownInstructions(unknownInstructions, statements);
                    statements.Add($"{varName} = 0x{lastImmediateA.Value:X2};");
                    continue;
                }
            }

            // Pattern: LDA $local → load local variable into A (track for use in subsequent calls/branches)
            if (opcode == 0xAD && op1.HasValue && op2.HasValue)
            {
                ushort addr = (ushort)(op1.Value | (op2.Value << 8));
                if (_localVariables.TryGetValue(addr, out var localVarName))
                {
                    // Check if followed by JSR <subroutine> (not pusha/pushax) → call with local var in A
                    if (i + 1 < instructions.Count)
                    {
                        var next = instructions[i + 1];
                        if (next.Opcode == 0x20 && next.Op1.HasValue && next.Op2.HasValue)
                        {
                            ushort jsrTarget = (ushort)(next.Op1.Value | (next.Op2.Value << 8));
                            if (_symbolTable.TryGetValue(jsrTarget, out var callName)
                                && callName != "pusha" && callName != "pushax")
                            {
                                var allPushed = DrainPushedArgs(pushedArgs);
                                var stmt = DecompileCallN(callName, localVarName, allPushed);
                                if (stmt != null) statements.Add(stmt);
                                lastImmediateA = null;
                                i++; // skip the JSR
                                continue;
                            }
                        }
                    }
                    // Don't emit a statement — the value in A will be consumed by the next JSR, STA, or branch
                    lastLoadedVarName = localVarName;
                    lastLoadedVarAddr = addr;
                    lastImmediateA = null;
                    lastCmpValue = null;
                    lastAndMask = null; // New load resets AND tracking
                    continue;
                }
            }

            // Pattern: LDA #imm / STA $abs → poke(addr, val)
            if (opcode == 0xA9 && op1.HasValue && i + 1 < instructions.Count)
            {
                var next = instructions[i + 1];
                if (next.Opcode == 0x8D && next.Op1.HasValue && next.Op2.HasValue)
                {
                    ushort addr = (ushort)(next.Op1.Value | (next.Op2.Value << 8));
                    if (!_symbolTable.ContainsKey(addr))
                    {
                        FlushUnknownInstructions(unknownInstructions, statements);
                        TrackPpuAddrWrite(addr, op1.Value);
                        statements.Add(FormatPoke(addr, op1.Value));
                        lastImmediateA = op1.Value;
                        i++; // skip the STA
                        continue;
                    }
                }
            }

            // Pattern: STA $abs (consecutive poke — A still holds last immediate value)
            if (opcode == 0x8D && op1.HasValue && op2.HasValue && lastImmediateA.HasValue)
            {
                ushort addr = (ushort)(op1.Value | (op2.Value << 8));
                if (!_symbolTable.ContainsKey(addr))
                {
                    FlushUnknownInstructions(unknownInstructions, statements);
                    TrackPpuAddrWrite(addr, lastImmediateA.Value);
                    statements.Add(FormatPoke(addr, lastImmediateA.Value));
                    continue;
                }
            }

            // Pattern: LDA $abs → peek(addr) for non-symbol-table absolute addresses
            if (opcode == 0xAD && op1.HasValue && op2.HasValue)
            {
                ushort addr = (ushort)(op1.Value | (op2.Value << 8));
                if (!_symbolTable.ContainsKey(addr))
                {
                    FlushUnknownInstructions(unknownInstructions, statements);
                    statements.Add(FormatPeek(addr));
                    lastImmediateA = null; // A now holds peek result, not an immediate
                    lastLoadedVarName = null; lastLoadedVarAddr = null;
                    lastCmpValue = null;
                    lastAndMask = null;
                    continue;
                }
            }

            // Pattern: AND #imm → mask accumulator, track for subsequent branch condition
            // Only consume when followed by a branch (BEQ/BNE), otherwise fall through to unknown
            if (opcode == 0x29 && op1.HasValue && lastLoadedVarName != null && i + 1 < instructions.Count)
            {
                byte nextOp = instructions[i + 1].Opcode;
                if (nextOp is 0xF0 or 0xD0) // BEQ or BNE
                {
                    lastAndMask = op1.Value;
                    // Don't clear lastLoadedVarName — AND result still references the variable
                    continue;
                }
            }

            // Pattern: CMP #imm → track comparison value for subsequent branch
            if (opcode == 0xC9 && op1.HasValue)
            {
                lastCmpValue = op1.Value;
                lastAndMask = null; // CMP sets flags — branch is based on CMP, not prior AND
                continue;
            }

            // Pattern: forward BEQ/BNE/BCS/BCC → if block
            if (opcode is 0xF0 or 0xD0 or 0xB0 or 0x90 && op1.HasValue && lastLoadedVarName != null)
            {
                sbyte branchOffset = (sbyte)op1.Value;
                ushort target = (ushort)(instrAddr + 2 + branchOffset);

                if (branchOffset > 0) // Forward branch = skip body when condition is met
                {
                    string condition;
                    if (lastAndMask.HasValue)
                    {
                        // After AND #mask: flags reflect A & mask
                        bool isPadVar = lastLoadedVarAddr.HasValue && _padVariables.Contains(lastLoadedVarAddr.Value);
                        string maskStr = isPadVar ? FormatPadMask(lastAndMask.Value) : $"0x{lastAndMask.Value:X2}";
                        condition = opcode switch
                        {
                            0xF0 => $"({lastLoadedVarName} & {maskStr}) != 0",  // BEQ skip → body runs when AND result nonzero
                            0xD0 => $"({lastLoadedVarName} & {maskStr}) == 0",  // BNE skip → body runs when AND result zero
                            _ => "/* unknown */"
                        };
                    }
                    else if (lastCmpValue.HasValue)
                    {
                        // After CMP #N: flags reflect A - N
                        bool isPadVar = lastLoadedVarAddr.HasValue && _padVariables.Contains(lastLoadedVarAddr.Value);
                        string cmpStr = isPadVar && NesLibEnumFormatter.IsExactPadFlags(lastCmpValue.Value)
                            ? NesLibEnumFormatter.FormatPad(lastCmpValue.Value)
                            : lastCmpValue.Value <= 9
                                ? lastCmpValue.Value.ToString()
                                : $"0x{lastCmpValue.Value:X2}";
                        condition = opcode switch
                        {
                            0xF0 => $"{lastLoadedVarName} != {cmpStr}",  // BEQ skip → body runs when not equal
                            0xD0 => $"{lastLoadedVarName} == {cmpStr}",  // BNE skip → body runs when equal
                            0xB0 => $"{lastLoadedVarName} < {cmpStr}",   // BCS skip → body runs when < N
                            0x90 => $"{lastLoadedVarName} >= {cmpStr}",  // BCC skip → body runs when >= N
                            _ => "/* unknown */"
                        };
                    }
                    else
                    {
                        // After LDA: flags reflect loaded value
                        condition = opcode switch
                        {
                            0xF0 => $"{lastLoadedVarName} != 0",  // BEQ skip → body runs when not zero
                            0xD0 => $"{lastLoadedVarName} == 0",  // BNE skip → body runs when zero
                            _ => "/* unknown */"
                        };
                    }

                    statements.Add($"if ({condition})");
                    statements.Add("{");
                    pendingCloseBraces.Push(target);

                    lastLoadedVarName = null; lastLoadedVarAddr = null;
                    lastCmpValue = null;
                    lastAndMask = null;
                    lastImmediateA = null;
                    continue;
                }
            }

            // Pattern: plain JSR (no preceding LDA/LDX)
            if (opcode == 0x20 && op1.HasValue && op2.HasValue)
            {
                ushort jsrTarget = (ushort)(op1.Value | (op2.Value << 8));
                if (_symbolTable.TryGetValue(jsrTarget, out var name))
                {
                    // JSR decsp4 → start collecting stack args for multi-arg call (e.g. oam_spr)
                    if (name == "decsp4")
                    {
                        decspArgs = new List<string>(4);
                        pendingDecspValue = null;
                        lastImmediateA = null;
                        continue;
                    }

                    var allPushed = DrainPushedArgs(pushedArgs);
                    if (allPushed.Count == 0)
                    {
                        var stmt = DecompileCall(name, null, null);
                        if (stmt != null)
                        {
                            FlushUnknownInstructions(unknownInstructions, statements);
                            statements.Add(stmt);
                        }
                    }
                    else
                    {
                        var stmt = DecompileCallN(name, null, allPushed);
                        if (stmt != null)
                        {
                            FlushUnknownInstructions(unknownInstructions, statements);
                            statements.Add(stmt);
                        }
                    }
                }
                lastImmediateA = null; // JSR may modify A
                lastLoadedVarName = null; lastLoadedVarAddr = null;
                lastCmpValue = null;
                lastAndMask = null;
                continue;
            }

            // Unrecognized instruction — accumulate for comment emission
            unknownInstructions.Add((instrAddr, opcode, op1, op2));
            lastImmediateA = null;
            lastLoadedVarName = null; lastLoadedVarAddr = null;
            lastCmpValue = null;
            lastAndMask = null;
        }

        // Close any remaining open if-blocks (may happen when branch target is past the end)
        while (pendingCloseBraces.Count > 0)
        {
            var addr = pendingCloseBraces.Pop();
            _logger.WriteLine($"  Warning: unclosed if-block at ${addr:X4} — target may be outside block");
            statements.Add("}");
        }

        // Flush any remaining unknown instructions
        FlushUnknownInstructions(unknownInstructions, statements);

        return statements;
    }

    bool IsSubroutine((ushort Address, byte Opcode, byte? Op1, byte? Op2) instr, string name)
    {
        if (instr.Opcode != 0x20 || !instr.Op1.HasValue || !instr.Op2.HasValue) return false;
        ushort target = (ushort)(instr.Op1.Value | (instr.Op2.Value << 8));
        return _symbolTable.TryGetValue(target, out var n) && n == name;
    }

    /// <summary>
    /// Returns true if the named subroutine returns a meaningful value in A
    /// that callers typically store to a local variable.
    /// </summary>
    static bool IsReturningFunction(string name) =>
        name is "pad_poll" or "rand8" or "rand";

    /// <summary>
    /// Format a call expression (without trailing semicolon) for a returning function.
    /// </summary>
    static string FormatCallExpression(string name, byte? aValue) => name switch
    {
        "pad_poll" => $"pad_poll({aValue?.ToString() ?? "0"})",
        "rand8" or "rand" => "rand8()",
        _ => $"{name}({(aValue.HasValue ? $"0x{aValue:X2}" : "")})"
    };

    /// <summary>
    /// Format an AND mask value, using PAD enum names when possible.
    /// </summary>
    static string FormatPadMask(byte mask) => NesLibEnumFormatter.FormatPad(mask);

    /// <summary>
    /// Check if an instruction is a JSR to a known subroutine (not pusha/pushax/decsp4).
    /// Used within decsp4 block to detect the final function call.
    /// </summary>
    bool TryMatchDecspCall((ushort Address, byte Opcode, byte? Op1, byte? Op2) instr, out string callName)
    {
        callName = "";
        if (instr.Opcode != 0x20 || !instr.Op1.HasValue || !instr.Op2.HasValue) return false;
        ushort target = (ushort)(instr.Op1.Value | (instr.Op2.Value << 8));
        if (!_symbolTable.TryGetValue(target, out var name)) return false;
        if (name is "pusha" or "pushax" or "decsp4") return false;
        callName = name;
        return true;
    }

    /// <summary>
    /// Detects local variables by finding non-indexed STA/LDA instructions targeting
    /// addresses in the $0325+ range that aren't claimed by array declarations.
    /// </summary>
    void DetectLocalVariables(List<(ushort Address, byte Opcode, byte? Op1, byte? Op2)> instructions)
    {
        const ushort MaxLocalAddress = 0x0800;

        // Build set of addresses claimed by arrays
        var arrayAddresses = new HashSet<ushort>();
        foreach (var arr in _arrays.RamArrays)
        {
            for (int j = 0; j < arr.Size; j++)
                arrayAddresses.Add((ushort)(arr.Base + j));
            string bound = arr.LoopBound is int b ? $" (loop bound {b})" : "";
            _logger.WriteLine($"  Detected array {arr.Name} at ${arr.Base:X4}, size {arr.Size}{bound}");
        }

        // Find non-indexed accesses to local variable area
        var localAddresses = new SortedSet<ushort>();
        foreach (var (_, opcode, op1, op2) in instructions)
        {
            if (!op1.HasValue || !op2.HasValue) continue;
            if (GetInstructionSize(opcode) != 3) continue;
            if (IsIndexedOpcode(opcode)) continue;

            // STA absolute (0x8D) or LDA absolute (0xAD)
            if (opcode is not (0x8D or 0xAD)) continue;

            ushort operandAddr = (ushort)(op1.Value | (op2.Value << 8));
            if (operandAddr < NESConstants.LocalStackBase || operandAddr >= MaxLocalAddress) continue;
            if (arrayAddresses.Contains(operandAddr)) continue;

            localAddresses.Add(operandAddr);
        }

        // Assign names: var_0325, var_0326, etc.
        foreach (var addr in localAddresses)
        {
            var name = $"var_{addr:X4}";
            _localVariables[addr] = name;
            _logger.WriteLine($"  Detected local variable '{name}' at ${addr:X4}");
        }
    }

    /// <summary>
    /// Detect for-loop structures by scanning for the condition/branch footer pattern
    /// and matching them to their initialization (LDA #imm / STA $local / JMP forward).
    ///
    /// Pattern 1 (INC): INC $local / LDA $local / CMP #$NN / BCC backward
    /// Pattern 2 (ADC): LDA $local / CLC / ADC #$01 / STA $local / CMP #$NN / BCC backward
    /// </summary>
    void DetectForLoops(List<(ushort Address, byte Opcode, byte? Op1, byte? Op2)> instructions)
    {
        // Build address→index lookup for resolving branch targets
        var addrToIndex = new Dictionary<ushort, int>();
        for (int k = 0; k < instructions.Count; k++)
            addrToIndex[instructions[k].Address] = k;

        // Track claimed init indices to prevent overlapping matches
        var claimedInits = new HashSet<int>();

        for (int j = 0; j < instructions.Count; j++)
        {
            // Try to match a for-loop footer starting at index j
            ushort localAddr;
            byte limit;
            int footerStart, afterLoopIndex;
            ushort bccTarget;
            int conditionCheckIndex; // index of the LDA $local in the condition check

            if (TryMatchIncFooter(instructions, j, out localAddr, out limit, out bccTarget, out afterLoopIndex))
            {
                footerStart = j;
                conditionCheckIndex = j + 1; // LDA is after INC
            }
            else if (TryMatchAdcFooter(instructions, j, out localAddr, out limit, out bccTarget, out afterLoopIndex))
            {
                footerStart = j;
                conditionCheckIndex = j; // LDA is at start of ADC footer
            }
            else
            {
                continue;
            }

            if (!_localVariables.ContainsKey(localAddr)) continue;

            // Find the body start index from the BCC target address
            if (!addrToIndex.TryGetValue(bccTarget, out int bodyStartIndex)) continue;

            // Find the initialization: LDA #imm / STA $local / JMP forward
            // The JMP targets the condition check (LDA $local in the footer)
            int initIndex = -1;
            byte initValue = 0;
            if (bodyStartIndex >= 3)
            {
                var ldaInit = instructions[bodyStartIndex - 3];
                var staInit = instructions[bodyStartIndex - 2];
                var jmpInit = instructions[bodyStartIndex - 1];

                if (ldaInit.Opcode == 0xA9 // LDA immediate
                    && staInit.Opcode == 0x8D // STA absolute
                    && jmpInit.Opcode == 0x4C) // JMP absolute
                {
                    ushort staInitAddr = (ushort)(staInit.Op1!.Value | (staInit.Op2!.Value << 8));
                    ushort jmpTarget = (ushort)(jmpInit.Op1!.Value | (jmpInit.Op2!.Value << 8));
                    // Verify STA writes to the loop counter and JMP targets the condition check (LDA $local)
                    if (staInitAddr == localAddr && jmpTarget == instructions[conditionCheckIndex].Address)
                    {
                        initIndex = bodyStartIndex - 3;
                        initValue = ldaInit.Op1!.Value;
                    }
                }
            }
            // Also try without JMP: LDA #imm / STA $local (direct fall-through to body)
            if (initIndex < 0 && bodyStartIndex >= 2)
            {
                var ldaInit = instructions[bodyStartIndex - 2];
                var staInit = instructions[bodyStartIndex - 1];

                if (ldaInit.Opcode == 0xA9 // LDA immediate
                    && staInit.Opcode == 0x8D) // STA absolute
                {
                    ushort staInitAddr = (ushort)(staInit.Op1!.Value | (staInit.Op2!.Value << 8));
                    if (staInitAddr == localAddr)
                    {
                        byte candidateInit = ldaInit.Op1!.Value;
                        // For fall-through init, only treat this as a for-loop initializer when
                        // the initial value is strictly less than the loop limit, so that the
                        // decompiled `for (...; var < limit; ...)` preserves semantics.
                        if (candidateInit < limit)
                        {
                            initIndex = bodyStartIndex - 2;
                            initValue = candidateInit;
                        }
                    }
                }
            }
            if (initIndex < 0) continue;
            if (claimedInits.Contains(initIndex)) continue;

            _forLoops.Add(new ForLoopInfo(localAddr, initValue, limit, initIndex, bodyStartIndex, footerStart, afterLoopIndex));
            _forLoopLocals.Add(localAddr);
            claimedInits.Add(initIndex);
            _logger.WriteLine($"  Detected for loop: {_localVariables[localAddr]} = {initValue} to {limit} (init@{initIndex}, body@{bodyStartIndex}, footer@{footerStart})");
        }
    }

    /// <summary>
    /// Try to match the INC-based footer pattern: INC $local / LDA $local / CMP #NN / BCC backward
    /// </summary>
    static bool TryMatchIncFooter(
        List<(ushort Address, byte Opcode, byte? Op1, byte? Op2)> instructions, int j,
        out ushort localAddr, out byte limit, out ushort bccTarget, out int afterLoopIndex)
    {
        localAddr = 0; limit = 0; bccTarget = 0; afterLoopIndex = 0;
        if (j + 3 >= instructions.Count) return false;

        if (instructions[j].Opcode != 0xEE) return false;     // INC absolute
        if (instructions[j + 1].Opcode != 0xAD) return false;  // LDA absolute
        if (instructions[j + 2].Opcode != 0xC9) return false;  // CMP immediate
        if (instructions[j + 3].Opcode != 0x90) return false;  // BCC

        ushort incAddr = (ushort)(instructions[j].Op1!.Value | (instructions[j].Op2!.Value << 8));
        ushort ldaAddr = (ushort)(instructions[j + 1].Op1!.Value | (instructions[j + 1].Op2!.Value << 8));
        if (incAddr != ldaAddr) return false;

        localAddr = incAddr;
        limit = instructions[j + 2].Op1!.Value;

        // Resolve BCC target — must be a backward branch
        // BCC is 2 bytes (opcode + relative offset), so target = address + 2 + signed_offset
        ushort bccInstrAddr = instructions[j + 3].Address;
        sbyte bccOffset = (sbyte)instructions[j + 3].Op1!.Value;
        bccTarget = (ushort)(bccInstrAddr + 2 + bccOffset);
        if (bccTarget >= bccInstrAddr) return false;

        afterLoopIndex = j + 4;
        return true;
    }

    /// <summary>
    /// Try to match the ADC-based footer pattern: LDA $local / CLC / ADC #$01 / STA $local / CMP #NN / BCC backward
    /// </summary>
    static bool TryMatchAdcFooter(
        List<(ushort Address, byte Opcode, byte? Op1, byte? Op2)> instructions, int j,
        out ushort localAddr, out byte limit, out ushort bccTarget, out int afterLoopIndex)
    {
        localAddr = 0; limit = 0; bccTarget = 0; afterLoopIndex = 0;
        if (j + 5 >= instructions.Count) return false;

        if (instructions[j].Opcode != 0xAD) return false;      // LDA absolute
        if (instructions[j + 1].Opcode != 0x18) return false;   // CLC
        if (instructions[j + 2].Opcode != 0x69 || instructions[j + 2].Op1 != 0x01) return false; // ADC #$01
        if (instructions[j + 3].Opcode != 0x8D) return false;   // STA absolute
        if (instructions[j + 4].Opcode != 0xC9) return false;   // CMP immediate
        if (instructions[j + 5].Opcode != 0x90) return false;   // BCC

        ushort ldaAddr = (ushort)(instructions[j].Op1!.Value | (instructions[j].Op2!.Value << 8));
        ushort staAddr = (ushort)(instructions[j + 3].Op1!.Value | (instructions[j + 3].Op2!.Value << 8));
        if (ldaAddr != staAddr) return false;

        localAddr = ldaAddr;
        limit = instructions[j + 4].Op1!.Value;

        // Resolve BCC target — must be a backward branch
        // BCC is 2 bytes (opcode + relative offset), so target = address + 2 + signed_offset
        ushort bccInstrAddr = instructions[j + 5].Address;
        sbyte bccOffset = (sbyte)instructions[j + 5].Op1!.Value;
        bccTarget = (ushort)(bccInstrAddr + 2 + bccOffset);
        if (bccTarget >= bccInstrAddr) return false;

        afterLoopIndex = j + 6;
        return true;
    }

    /// <summary>
    /// Returns true if the opcode uses Absolute,X or Absolute,Y addressing mode.
    /// </summary>
    static bool IsIndexedOpcode(byte opcode) => opcode is
        // Absolute,X: ORA, ASL, AND, ROL, EOR, LSR, ADC, ROR, STA, LDY, LDA, CMP, DEC, SBC, INC
        0x1D or 0x1E or 0x3D or 0x3E or 0x5D or 0x5E
        or 0x7D or 0x7E or 0x9D or 0xBC or 0xBD
        or 0xDD or 0xDE or 0xFD or 0xFE
        // Absolute,Y: ORA, AND, EOR, ADC, STA, LDA, LDX, CMP, SBC
        or 0x19 or 0x39 or 0x59 or 0x79 or 0x99
        or 0xB9 or 0xBE or 0xD9 or 0xF9;

    /// <summary>
    /// Decompile a call where A contains a value (the last argument),
    /// and optionally a pushed value was the first argument.
    /// </summary>
    string? DecompileCall(string name, byte? aValue, byte? pushedArg = null)
    {
        // Track single-byte vram_adr calls (transpiler omits LDX #$00 for zero addresses)
        if (name == "vram_adr" && aValue.HasValue)
        {
            _lastVramAdrTarget = aValue.Value;
            return NesLibCallFormatter.FormatVramAdr(aValue.Value, 0);
        }

        if (NesLibCallFormatter.IsInternalRuntime(name))
            return null;

        string? formatted = NesLibCallFormatter.FormatCall(name, aValue, pushedArg);
        if (formatted != null)
            return formatted;

        if (_userFunctionNames.Contains(name))
            return FormatUserFunctionCall(name, aValue, pushedArg);

        // Known NESLib name but unconventional args — still emit a call, not a comment
        if (IsLikelyNesLibName(name))
        {
            if (pushedArg.HasValue && aValue.HasValue)
                return $"{name}({NesLibCallFormatter.FormatByte(pushedArg.Value)}, {NesLibCallFormatter.FormatByte(aValue.Value)});";
            if (aValue.HasValue)
                return $"{name}({NesLibCallFormatter.FormatByte(aValue.Value)});";
            return $"{name}();";
        }

        return $"// {name}({(aValue.HasValue ? $"0x{aValue:X2}" : "")});";
    }

    /// <summary>
    /// Drain all pushed arguments from the stack into a list, in push order (first-pushed = first element).
    /// </summary>
    static List<string> DrainPushedArgs(Stack<string> pushedArgs)
    {
        if (pushedArgs.Count == 0) return new List<string>();
        var args = pushedArgs.ToList();
        pushedArgs.Clear();
        args.Reverse(); // Stack pops in LIFO order; reverse to get push order
        return args;
    }

    /// <summary>
    /// Decompile a call with N pushed arguments and optionally a value in A.
    /// Used for multi-arg calls like oam_spr(x, y, tile, attr, oam_off).
    /// Pushed args are in parameter order (first = first parameter).
    /// aArg is the last parameter (loaded into A before the call).
    /// </summary>
    static string? DecompileCallN(string name, string? aArg, List<string> pushedArgs)
    {
        if (NesLibCallFormatter.IsInternalRuntime(name))
            return null;

        string? formatted = NesLibCallFormatter.FormatCallN(name, aArg, pushedArgs);
        if (formatted != null)
            return formatted;

        var all = pushedArgs.Concat(aArg != null ? new[] { aArg } : Array.Empty<string>());
        if (IsLikelyNesLibName(name))
            return $"{name}({string.Join(", ", all.Select(NesLibCallFormatter.FormatOamArg))});";

        return $"// {name}({string.Join(", ", all)});";
    }

    static bool IsLikelyNesLibName(string name) =>
        name is not null
        && !name.StartsWith("func_", StringComparison.Ordinal)
        && !name.StartsWith("_", StringComparison.Ordinal)
        && name.All(c => char.IsLetterOrDigit(c) || c == '_');

    /// <summary>
    /// Decompile a call where X:A contain a 16-bit value (hi:lo).
    /// Used for vram_adr(NTADR_A(x,y)), vram_write(data), pal_bg/pal_spr, vram_fill, etc.
    /// Returns a list of statements (may include byte array declarations before the call).
    /// </summary>
    List<string> DecompileCallXA(string name, byte xValue, byte aValue, ushort? pushedPtr, byte? pushedByte = null)
    {
        ushort value16 = (ushort)((xValue << 8) | aValue);

        // Track vram_adr targets for CHR RAM tile extraction
        if (name == "vram_adr")
            _lastVramAdrTarget = value16;
        else if (name == "vram_write")
            TrackChrRamUpload(value16, pushedPtr);

        List<string> FormatPointerPayload(string api, ushort lenOrAddr, ushort? ptr)
        {
            if (api is "pal_bg" or "pal_spr" or "pal_all")
            {
                int size = api == "pal_all" ? 32 : 16;
                return FormatPaletteCall(api, $"palette{_dataCounter++}", lenOrAddr, size);
            }
            if (api == "vram_write")
                return FormatVramWrite(lenOrAddr, ptr);
            if (api == "vram_read" && ptr.HasValue)
            {
                string varName = $"data{_dataCounter++}";
                return new List<string>
                {
                    $"byte[] {varName} = new byte[{lenOrAddr}];",
                    $"vram_read({varName}, {lenOrAddr});",
                };
            }
            if (api is "vram_unrle" or "start_music" or "set_music_pulse_table" or "set_music_triangle_table")
            {
                if (ptr.HasValue || api != "vram_unrle")
                {
                    ushort dataPtr = ptr ?? lenOrAddr;
                    int offset = dataPtr - 0x8000;
                    if (offset >= 0 && offset < _rom.PrgRom.Length)
                    {
                        string varName = $"data{_dataCounter++}";
                        int len = Math.Min(64, _rom.PrgRom.Length - offset);
                        return new List<string>
                        {
                            FormatByteArrayDeclaration(offset, len, varName),
                            $"{api}({varName});",
                        };
                    }
                }
                return new List<string> { $"{api}(/* 0x{lenOrAddr:X4} */);" };
            }
            return new List<string> { $"{api}(/* 0x{lenOrAddr:X4} */);" };
        }

        var formatted = NesLibCallFormatter.FormatCallXA(
            name, xValue, aValue, pushedPtr, pushedByte, FormatPointerPayload);
        if (formatted != null)
            return formatted;

        if (_userFunctionNames.Contains(name))
            return new List<string> { FormatUserFunctionCall(name, aValue, pushedByte) };

        if (IsLikelyNesLibName(name))
            return new List<string> { $"{name}(0x{value16:X4});" };

        return new List<string> { $"// {name}(0x{value16:X4});" };
    }

    /// <summary>
    /// Format vram_write call. If a data pointer was pushed, try to read the data
    /// from the ROM. For printable ASCII, emit a string literal; otherwise emit a byte array.
    /// </summary>
    List<string> FormatVramWrite(ushort length, ushort? dataPointer)
    {
        if (dataPointer.HasValue && length > 0)
        {
            int offset = dataPointer.Value - 0x8000;
            if (offset >= 0 && offset + length <= _rom.PrgRom.Length)
            {
                // Try string first (all printable ASCII)
                bool allPrintable = true;
                for (int i = 0; i < length; i++)
                {
                    byte b = _rom.PrgRom[offset + i];
                    if (b < 0x20 || b >= 0x7F) { allPrintable = false; break; }
                }
                _arrays.NotePointerUsage((ushort)(0x8000 + offset), length, "vram_write");
                if (allPrintable)
                {
                    var chars = new char[length];
                    for (int i = 0; i < length; i++)
                        chars[i] = (char)_rom.PrgRom[offset + i];
                    string escaped = new string(chars).Replace("\\", "\\\\").Replace("\"", "\\\"");
                    return new List<string> { $"vram_write(\"{escaped}\");" };
                }

                // Non-ASCII: emit byte array variable + call
                string varName = $"data{_dataCounter++}";
                return new List<string>
                {
                    FormatByteArrayDeclaration(offset, length, varName),
                    $"vram_write({varName});"
                };
            }
        }
        return new List<string> { $"vram_write(/* {length} bytes */);" };
    }

    /// <summary>
    /// Format a vram_adr call, trying to reconstruct NTADR_A macro usage.
    /// </summary>
    static string FormatVramAdr(byte lo, byte hi)
    {
        ushort addr = (ushort)((hi << 8) | lo);
        // Try to decompose as NTADR_A(x, y) = 0x2000 + y*32 + x
        if (addr >= 0x2000 && addr < 0x2400)
        {
            int offset = addr - 0x2000;
            int y = offset / 32;
            int x = offset % 32;
            return $"vram_adr(NTADR_A({x}, {y}));";
        }
        return $"vram_adr(0x{addr:X4});";
    }

    /// <summary>
    /// Track vram_write calls that upload tile data to CHR RAM pattern tables ($0000-$1FFF).
    /// Called before FormatVramWrite to record data for .s file extraction.
    /// </summary>
    void TrackChrRamUpload(ushort length, ushort? dataPointer)
    {
        if (_rom.ChrBanks > 0 || !_lastVramAdrTarget.HasValue || !dataPointer.HasValue || length == 0)
            return;

        ushort ppuAddr = _lastVramAdrTarget.Value;
        _lastVramAdrTarget = null;

        // Only track writes to CHR pattern tables ($0000-$1FFF)
        if (ppuAddr >= 0x2000)
            return;

        int offset = dataPointer.Value - 0x8000;
        // NROM-128 (16KB PRG): $C000-$FFFF mirrors $8000-$BFFF
        if (_rom.PrgRom.Length == 0x4000)
            offset %= 0x4000;
        if (offset >= 0 && offset + length <= _rom.PrgRom.Length)
        {
            var data = new byte[length];
            Array.Copy(_rom.PrgRom, offset, data, 0, length);
            _chrRamTileData.Add((ppuAddr, data));
            _logger.WriteLine($"CHR RAM upload: {length} bytes to PPU ${ppuAddr:X4} from PRG ${dataPointer.Value:X4}");
        }
    }

    /// <summary>
    /// Returns tile data extracted from vram_write calls to CHR RAM pattern tables.
    /// Each entry contains the PPU target address and the raw tile data.
    /// </summary>
    public IReadOnlyList<(ushort PpuAddress, byte[] Data)> GetChrRamTileData() => _chrRamTileData;

    /// <summary>
    /// Track poke() writes to PPU_ADDR ($2006) for CHR RAM detection.
    /// Two consecutive writes set the full 16-bit PPU address (high byte first).
    /// </summary>
    void TrackPpuAddrWrite(ushort addr, byte value)
    {
        if (addr != NESLib.PPU_ADDR)
        {
            _ppuAddrHigh = null;
            return;
        }

        if (!_ppuAddrHigh.HasValue)
        {
            _ppuAddrHigh = value; // First write = high byte
        }
        else
        {
            _lastVramAdrTarget = (ushort)((_ppuAddrHigh.Value << 8) | value);
            _ppuAddrHigh = null;
        }
    }

    /// <summary>
    /// Format a palette call (pal_bg, pal_spr, pal_all) by extracting palette data from ROM.
    /// </summary>
    List<string> FormatPaletteCall(string funcName, string varName, ushort pointer, int size)
    {
        _arrays.NotePointerUsage(pointer, size, funcName);
        int offset = MapPrgOffset(pointer);
        if (offset >= 0 && offset + size <= _rom.PrgRom.Length)
        {
            return new List<string>
            {
                FormatByteArrayDeclaration(offset, size, varName, preferNesColor: true),
                $"{funcName}({varName});"
            };
        }
        return new List<string> { $"// {funcName}(/* data at ${pointer:X4} */);" };
    }

    int MapPrgOffset(ushort cpuAddress)
    {
        int offset = cpuAddress - 0x8000;
        if (_rom.PrgRom.Length == 0x4000)
            offset %= 0x4000;
        return offset;
    }

    /// <summary>
    /// Format a byte/ushort array declaration from ROM data.
    /// Palette-looking data uses NESColor names; music tables can emit ushort[].
    /// </summary>
    string FormatByteArrayDeclaration(int romOffset, int length, string varName, bool preferNesColor = false, bool preferUshort = false)
    {
        if (preferUshort && length >= 2 && (length % 2) == 0)
        {
            int words = length / 2;
            var sb = new StringBuilder();
            sb.Append($"ushort[] {varName} = new ushort[] {{ ");
            for (int i = 0; i < words; i++)
            {
                if (i > 0) sb.Append(", ");
                ushort w = (ushort)(_rom.PrgRom[romOffset + i * 2] | (_rom.PrgRom[romOffset + i * 2 + 1] << 8));
                sb.Append(w <= 9 ? w.ToString() : $"0x{w:X4}");
            }
            sb.Append(" };");
            return sb.ToString();
        }

        bool useColors = preferNesColor || LooksLikePalette(romOffset, length);
        var text = new StringBuilder();
        if (length <= 16)
        {
            text.Append($"byte[] {varName} = new byte[] {{ ");
            for (int i = 0; i < length; i++)
            {
                if (i > 0) text.Append(", ");
                byte b = _rom.PrgRom[romOffset + i];
                text.Append(useColors ? NesLibEnumFormatter.FormatNesColor(b) : FormatHexByte(b));
            }
            text.Append(" };");
        }
        else
        {
            text.AppendLine($"byte[] {varName} = new byte[]");
            text.Append('{');
            for (int i = 0; i < length; i++)
            {
                if (i % 16 == 0)
                {
                    if (i > 0) text.Append(',');
                    text.AppendLine();
                    text.Append("    ");
                }
                else
                {
                    text.Append(", ");
                }
                byte b = _rom.PrgRom[romOffset + i];
                text.Append(useColors ? NesLibEnumFormatter.FormatNesColor(b) : FormatHexByte(b));
            }
            text.AppendLine();
            text.Append("};");
        }
        return text.ToString();
    }

    static string FormatHexByte(byte b) => $"0x{b:X2}";

    bool LooksLikePalette(int romOffset, int length)
    {
        if (length is not (16 or 32))
            return false;
        if (romOffset < 0 || romOffset + length > _rom.PrgRom.Length)
            return false;
        for (int i = 0; i < length; i++)
        {
            byte b = _rom.PrgRom[romOffset + i];
            if (b > 0x3D)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Map of known NES hardware register addresses to their NESLib constant names.
    /// </summary>
    static readonly Dictionary<ushort, string> KnownAddresses = new()
    {
        // PPU registers
        { NESLib.PPU_CTRL, nameof(NESLib.PPU_CTRL) },
        { NESLib.PPU_MASK, nameof(NESLib.PPU_MASK) },
        { NESLib.PPU_STATUS, nameof(NESLib.PPU_STATUS) },
        { NESLib.PPU_SCROLL, nameof(NESLib.PPU_SCROLL) },
        { NESLib.PPU_ADDR, nameof(NESLib.PPU_ADDR) },
        { NESLib.PPU_DATA, nameof(NESLib.PPU_DATA) },
        // APU registers
        { NESLib.APU_PULSE1_CTRL, nameof(NESLib.APU_PULSE1_CTRL) },
        { NESLib.APU_PULSE1_SWEEP, nameof(NESLib.APU_PULSE1_SWEEP) },
        { NESLib.APU_PULSE1_TIMER_LO, nameof(NESLib.APU_PULSE1_TIMER_LO) },
        { NESLib.APU_PULSE1_TIMER_HI, nameof(NESLib.APU_PULSE1_TIMER_HI) },
        { NESLib.APU_PULSE2_CTRL, nameof(NESLib.APU_PULSE2_CTRL) },
        { NESLib.APU_PULSE2_SWEEP, nameof(NESLib.APU_PULSE2_SWEEP) },
        { NESLib.APU_PULSE2_TIMER_LO, nameof(NESLib.APU_PULSE2_TIMER_LO) },
        { NESLib.APU_PULSE2_TIMER_HI, nameof(NESLib.APU_PULSE2_TIMER_HI) },
        { NESLib.APU_TRIANGLE_CTRL, nameof(NESLib.APU_TRIANGLE_CTRL) },
        { NESLib.APU_TRIANGLE_TIMER_LO, nameof(NESLib.APU_TRIANGLE_TIMER_LO) },
        { NESLib.APU_TRIANGLE_TIMER_HI, nameof(NESLib.APU_TRIANGLE_TIMER_HI) },
        { NESLib.APU_NOISE_CTRL, nameof(NESLib.APU_NOISE_CTRL) },
        { NESLib.APU_NOISE_PERIOD, nameof(NESLib.APU_NOISE_PERIOD) },
        { NESLib.APU_NOISE_LENGTH, nameof(NESLib.APU_NOISE_LENGTH) },
        { NESLib.APU_STATUS, nameof(NESLib.APU_STATUS) },
    };

    /// <summary>
    /// Format an absolute address as a NESLib constant name or hex literal.
    /// </summary>
    static string FormatAddress(ushort addr)
    {
        if (KnownAddresses.TryGetValue(addr, out var name))
            return name;
        return $"0x{addr:X4}";
    }

    /// <summary>
    /// Format a poke() statement with a named or hex address and typed value when known.
    /// </summary>
    static string FormatPoke(ushort addr, byte value)
    {
        string formattedValue = addr switch
        {
            _ when addr == NESLib.PPU_MASK => NesLibEnumFormatter.FormatMask(value),
            _ when addr == NESLib.PPU_CTRL => NesLibEnumFormatter.FormatPpuCtrl(value),
            _ when addr == NESLib.APU_STATUS => NesLibEnumFormatter.FormatApuStatus(value),
            _ when addr == NESLib.APU_FRAME => NesLibEnumFormatter.FormatApuFrame(value),
            _ when (addr & 0xFF00) == 0x3F00 => NesLibEnumFormatter.FormatNesColor(value),
            _ => value <= 9 ? value.ToString() : $"0x{value:X2}",
        };
        // Typed enums need a byte cast at the poke() call site.
        if (addr is NESLib.PPU_MASK or NESLib.PPU_CTRL or NESLib.APU_STATUS or NESLib.APU_FRAME
            || (addr & 0xFF00) == 0x3F00)
        {
            if (formattedValue is not "0" && !formattedValue.StartsWith("NESColor.", StringComparison.Ordinal))
                formattedValue = $"(byte)({formattedValue})";
        }
        return $"poke({FormatAddress(addr)}, {formattedValue});";
    }

    /// <summary>
    /// Format a peek() statement with a named or hex address.
    /// </summary>
    static string FormatPeek(ushort addr) =>
        $"peek({FormatAddress(addr)});";

    /// <summary>
    /// Format a call to a user-defined function.
    /// Parameter inference is not yet implemented, so all calls are emitted
    /// as zero-argument to match the generated function declarations.
    /// </summary>
    static string FormatUserFunctionCall(string name, byte? aValue, byte? pushedArg)
    {
        return $"{name}();";
    }
    /// <summary>
    /// Generate the final C# source code from decompiled statements.
    /// </summary>
    string GenerateCSharp(List<string> statements, List<(string Name, List<string> Statements)> userFunctions)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// Decompiled from .nes ROM by dotnes decompiler");
        sb.AppendLine();

        // Emit RAM byte[] array declarations (sized from gaps + for-loop bounds + max index)
        if (_arrays.RamArrays.Count > 0)
        {
            foreach (var arr in _arrays.RamArrays)
                sb.AppendLine($"byte[] {arr.Name} = new byte[{arr.Size}];");
            sb.AppendLine();
        }

        // Emit local variable declarations (excluding for-loop counter variables)
        var nonLoopLocals = _localVariables
            .Where(kvp => !_forLoopLocals.Contains(kvp.Key))
            .OrderBy(kvp => kvp.Key)
            .ToList();
        if (nonLoopLocals.Count > 0)
        {
            foreach (var (addr, name) in nonLoopLocals)
            {
                string type = _padVariables.Contains(addr) ? "PAD" : "byte";
                sb.AppendLine($"{type} {name} = 0;");
            }
            sb.AppendLine();
        }

        int indent = 0;
        foreach (var statement in statements)
        {
            if (statement == "}")
                indent = Math.Max(0, indent - 1);

            string prefix = indent > 0 ? new string(' ', indent * 4) : "";
            sb.AppendLine(prefix + statement);

            if (statement == "{")
                indent++;
        }

        // Emit user-defined functions
        foreach (var (funcName, funcStatements) in userFunctions)
        {
            sb.AppendLine();
            sb.AppendLine($"static void {funcName}()");
            sb.AppendLine("{");

            indent = 1;
            foreach (var statement in funcStatements)
            {
                if (statement == "}")
                    indent = Math.Max(1, indent - 1);

                string prefix = new string(' ', indent * 4);
                sb.AppendLine(prefix + statement);

                if (statement == "{")
                    indent++;
            }

            sb.AppendLine("}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generate a .csproj file for the decompiled project.
    /// </summary>
    public string GenerateCsproj(string projectName)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
        sb.AppendLine();
        sb.AppendLine("  <PropertyGroup>");
        sb.AppendLine("    <OutputType>Exe</OutputType>");
        sb.AppendLine("    <TargetFramework>net10.0</TargetFramework>");
        sb.AppendLine("    <ImplicitUsings>enable</ImplicitUsings>");
        sb.AppendLine("    <Nullable>enable</Nullable>");

        ProjectEmitter.AppendRomProperties(sb, _rom, packMode: PackMode.Transpile);

        sb.AppendLine("  </PropertyGroup>");
        sb.AppendLine();
        sb.AppendLine("  <ItemGroup>");
        sb.AppendLine("    <Using Include=\"NES\" />");
        sb.AppendLine("    <PackageReference Include=\"dotnes\" Version=\"*\" />");
        sb.AppendLine("  </ItemGroup>");
        sb.AppendLine();
        sb.AppendLine("</Project>");

        return sb.ToString();
    }

    /// <summary>
    /// Get the size of a 6502 instruction by its opcode byte.
    /// </summary>
    static int GetInstructionSize(byte opcode)
    {
        // Implied/Accumulator instructions (1 byte)
        if (opcode is 0x00 or 0x08 or 0x0A or 0x18 or 0x28 or 0x2A or 0x38
            or 0x40 or 0x48 or 0x4A or 0x58 or 0x60 or 0x68 or 0x6A
            or 0x78 or 0x88 or 0x8A or 0x98 or 0x9A or 0xA8 or 0xAA
            or 0xB8 or 0xBA or 0xC8 or 0xCA or 0xD8 or 0xE8 or 0xEA
            or 0xF8)
            return 1;

        // Absolute + Absolute,X + Absolute,Y + Indirect (3 bytes)
        if (opcode is 0x0D or 0x0E or 0x19 or 0x1D or 0x1E or 0x20
            or 0x2C or 0x2D or 0x2E or 0x39 or 0x3D or 0x3E
            or 0x4C or 0x4D or 0x4E or 0x59 or 0x5D or 0x5E
            or 0x6C or 0x6D or 0x6E or 0x79 or 0x7D or 0x7E
            or 0x8C or 0x8D or 0x8E or 0x99 or 0x9D
            or 0xAC or 0xAD or 0xAE or 0xB9 or 0xBC or 0xBD or 0xBE
            or 0xCC or 0xCD or 0xCE or 0xD9 or 0xDD or 0xDE
            or 0xEC or 0xED or 0xEE or 0xF9 or 0xFD or 0xFE)
            return 3;

        // All other valid opcodes are 2 bytes (immediate, zero page, relative, indexed indirect, indirect indexed)
        return 2;
    }

    /// <summary>
    /// Flush accumulated unknown instructions as comment lines in the output.
    /// Splits into separate blocks when addresses are not contiguous (i.e., a recognized-
    /// but-not-emitted instruction was between two unknowns).
    /// </summary>
    static void FlushUnknownInstructions(
        List<(ushort Address, byte Opcode, byte? Op1, byte? Op2)> unknowns,
        List<string> statements)
    {
        if (unknowns.Count == 0)
            return;

        int groupStart = 0;
        for (int i = 1; i <= unknowns.Count; i++)
        {
            // Split when the next unknown isn't contiguous with the previous one
            if (i < unknowns.Count)
            {
                var prev = unknowns[i - 1];
                ushort expectedNext = (ushort)(prev.Address + GetInstructionSize(prev.Opcode));
                if (unknowns[i].Address != expectedNext)
                {
                    EmitUnknownBlock(unknowns, groupStart, i, statements);
                    groupStart = i;
                }
            }
        }

        EmitUnknownBlock(unknowns, groupStart, unknowns.Count, statements);
        unknowns.Clear();
    }

    static void EmitUnknownBlock(
        List<(ushort Address, byte Opcode, byte? Op1, byte? Op2)> unknowns,
        int start, int end, List<string> statements)
    {
        ushort startAddr = unknowns[start].Address;
        var last = unknowns[end - 1];
        ushort endAddr = (ushort)(last.Address + GetInstructionSize(last.Opcode) - 1);

        if (startAddr == endAddr)
            statements.Add($"// Unknown 6502 assembly at ${startAddr:X4}:");
        else
            statements.Add($"// Unknown 6502 assembly at ${startAddr:X4}-${endAddr:X4}:");

        for (int i = start; i < end; i++)
        {
            var (addr, opcode, op1, op2) = unknowns[i];
            statements.Add($"//   {FormatDisassembly(addr, opcode, op1, op2)}");
        }
    }

    /// <summary>
    /// Format a single 6502 instruction as a disassembly string (e.g., "LDA #$01", "STA $2005").
    /// </summary>
    static string FormatDisassembly(ushort address, byte opcode, byte? op1, byte? op2)
    {
        string mnemonic = GetMnemonic(opcode);
        int size = GetInstructionSize(opcode);

        if (size == 1)
            return mnemonic;

        if (size == 2 && op1.HasValue)
        {
            // Determine addressing mode from opcode
            return opcode switch
            {
                // Immediate: #$xx
                0x09 or 0x29 or 0x49 or 0x69 or 0xA0 or 0xA2 or 0xA9
                    or 0xC0 or 0xC9 or 0xE0 or 0xE9
                    => $"{mnemonic} #${op1.Value:X2}",

                // Relative: branch instructions
                0x10 or 0x30 or 0x50 or 0x70 or 0x90 or 0xB0 or 0xD0 or 0xF0
                    => $"{mnemonic} ${(ushort)(address + 2 + (sbyte)op1.Value):X4}",

                // Zero Page,X
                0x15 or 0x16 or 0x35 or 0x36 or 0x55 or 0x56 or 0x75 or 0x76
                    or 0x94 or 0x95 or 0xB4 or 0xB5 or 0xD5 or 0xD6 or 0xF5 or 0xF6
                    => $"{mnemonic} ${op1.Value:X2},X",

                // Zero Page,Y
                0x96 or 0xB6
                    => $"{mnemonic} ${op1.Value:X2},Y",

                // Indexed Indirect (X)
                0x01 or 0x21 or 0x41 or 0x61 or 0x81 or 0xA1 or 0xC1 or 0xE1
                    => $"{mnemonic} (${op1.Value:X2},X)",

                // Indirect Indexed (Y)
                0x11 or 0x31 or 0x51 or 0x71 or 0x91 or 0xB1 or 0xD1 or 0xF1
                    => $"{mnemonic} (${op1.Value:X2}),Y",

                // Default: Zero Page
                _ => $"{mnemonic} ${op1.Value:X2}",
            };
        }

        if (size == 3 && op1.HasValue && op2.HasValue)
        {
            ushort addr = (ushort)(op1.Value | (op2.Value << 8));

            return opcode switch
            {
                // Indirect (JMP only)
                0x6C => $"{mnemonic} (${addr:X4})",

                // Absolute,X
                0x1D or 0x1E or 0x3D or 0x3E or 0x5D or 0x5E or 0x7D or 0x7E
                    or 0x9D or 0xBC or 0xBD or 0xDD or 0xDE or 0xFD or 0xFE
                    => $"{mnemonic} ${addr:X4},X",

                // Absolute,Y
                0x19 or 0x39 or 0x59 or 0x79 or 0x99 or 0xB9 or 0xBE or 0xD9 or 0xF9
                    => $"{mnemonic} ${addr:X4},Y",

                // Default: Absolute
                _ => $"{mnemonic} ${addr:X4}",
            };
        }

        return mnemonic;
    }

    /// <summary>
    /// Get the mnemonic name for a 6502 opcode byte.
    /// </summary>
    static string GetMnemonic(byte opcode) => opcode switch
    {
        0x00 => "BRK", 0x01 => "ORA", 0x05 => "ORA", 0x06 => "ASL", 0x08 => "PHP", 0x09 => "ORA", 0x0A => "ASL",
        0x0D => "ORA", 0x0E => "ASL",
        0x10 => "BPL", 0x11 => "ORA", 0x15 => "ORA", 0x16 => "ASL", 0x18 => "CLC", 0x19 => "ORA", 0x1D => "ORA",
        0x1E => "ASL",
        0x20 => "JSR", 0x21 => "AND", 0x24 => "BIT", 0x25 => "AND", 0x26 => "ROL", 0x28 => "PLP", 0x29 => "AND",
        0x2A => "ROL", 0x2C => "BIT", 0x2D => "AND", 0x2E => "ROL",
        0x30 => "BMI", 0x31 => "AND", 0x35 => "AND", 0x36 => "ROL", 0x38 => "SEC", 0x39 => "AND", 0x3D => "AND",
        0x3E => "ROL",
        0x40 => "RTI", 0x41 => "EOR", 0x45 => "EOR", 0x46 => "LSR", 0x48 => "PHA", 0x49 => "EOR", 0x4A => "LSR",
        0x4C => "JMP", 0x4D => "EOR", 0x4E => "LSR",
        0x50 => "BVC", 0x51 => "EOR", 0x55 => "EOR", 0x56 => "LSR", 0x58 => "CLI", 0x59 => "EOR", 0x5D => "EOR",
        0x5E => "LSR",
        0x60 => "RTS", 0x61 => "ADC", 0x65 => "ADC", 0x66 => "ROR", 0x68 => "PLA", 0x69 => "ADC", 0x6A => "ROR",
        0x6C => "JMP", 0x6D => "ADC", 0x6E => "ROR",
        0x70 => "BVS", 0x71 => "ADC", 0x75 => "ADC", 0x76 => "ROR", 0x78 => "SEI", 0x79 => "ADC", 0x7D => "ADC",
        0x7E => "ROR",
        0x81 => "STA", 0x84 => "STY", 0x85 => "STA", 0x86 => "STX", 0x88 => "DEY", 0x8A => "TXA", 0x8C => "STY",
        0x8D => "STA", 0x8E => "STX",
        0x90 => "BCC", 0x91 => "STA", 0x94 => "STY", 0x95 => "STA", 0x96 => "STX", 0x98 => "TYA", 0x99 => "STA",
        0x9A => "TXS", 0x9D => "STA",
        0xA0 => "LDY", 0xA1 => "LDA", 0xA2 => "LDX", 0xA4 => "LDY", 0xA5 => "LDA", 0xA6 => "LDX", 0xA8 => "TAY",
        0xA9 => "LDA", 0xAA => "TAX", 0xAC => "LDY", 0xAD => "LDA", 0xAE => "LDX",
        0xB0 => "BCS", 0xB1 => "LDA", 0xB4 => "LDY", 0xB5 => "LDA", 0xB6 => "LDX", 0xB8 => "CLV", 0xB9 => "LDA",
        0xBA => "TSX", 0xBC => "LDY", 0xBD => "LDA", 0xBE => "LDX",
        0xC0 => "CPY", 0xC1 => "CMP", 0xC4 => "CPY", 0xC5 => "CMP", 0xC6 => "DEC", 0xC8 => "INY", 0xC9 => "CMP",
        0xCA => "DEX", 0xCC => "CPY", 0xCD => "CMP", 0xCE => "DEC",
        0xD0 => "BNE", 0xD1 => "CMP", 0xD5 => "CMP", 0xD6 => "DEC", 0xD8 => "CLD", 0xD9 => "CMP", 0xDD => "CMP",
        0xDE => "DEC",
        0xE0 => "CPX", 0xE1 => "SBC", 0xE4 => "CPX", 0xE5 => "SBC", 0xE6 => "INC", 0xE8 => "INX", 0xE9 => "SBC",
        0xEA => "NOP", 0xEC => "CPX", 0xED => "SBC", 0xEE => "INC",
        0xF0 => "BEQ", 0xF1 => "SBC", 0xF5 => "SBC", 0xF6 => "INC", 0xF8 => "SED", 0xF9 => "SBC", 0xFD => "SBC",
        0xFE => "INC",
        _ => $"???({opcode:X2})",
    };
}
