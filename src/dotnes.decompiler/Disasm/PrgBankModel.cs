namespace dotnes.Disasm;

/// <summary>
/// Maps CPU addresses to PRG file offsets for common iNES mappers.
/// Unknown/obscure mappers fall back to a last-32KB window view (RawPrg still exact).
/// </summary>
sealed class PrgBankModel
{
    readonly NESRomReader _rom;

    public PrgBankModel(NESRomReader rom) => _rom = rom;

    /// <summary>iNES 16KB PRG bank count.</summary>
    public int PrgBanks => Math.Max(1, _rom.PrgBanks);

    /// <summary>True when $8000-$FFFF is one switchable 32KB window.</summary>
    public bool HasSwitchable32KWindow =>
        _rom.Mapper is 7 or 8 or 11 or 15 or 34 or 35 or 36 or 39 or 41 or 51 or 53 or 54 or 57 or 58 or 59 or 60 or 61
            or 62 or 63 or 66 or 77 or 79 or 107 or 113 or 129 or 144 or 162 or 166 or 167 or 169 or 174 or 175
            or 176 or 177 or 178 or 179 or 187 or 200 or 201 or 202 or 203 or 204 or 208 or 212 or 213
            or 216 or 221 or 225 or 226 or 227 or 228 or 229 or 230 or 231 or 233 or 235 or 236 or 237 or 239
            or 241 or 242 or 244 or 255 or 261 or 266 or 271 or 274 or 283 or 285 or 288 or 289 or 290 or 299 or 310 or 314 or 319 or 329 or 332 or 334 or 335 or 337 or 342
            or 346 or 350 or 354 or 375 or 378 or 379 or 380 or 389 or 390 or 402 or 403 or 414 or 416 or 429 or 433 or 438 or 449 or 452 or 453 or 454 or 458 or 470 or 471 or 514 or 518 or 519 or 550 or 551 or 558 or 561 or 562;
    // Note: mapper 160 ≡ 90 (JY ASIC) → 16K. mapper 161 ≡ MMC1 → 16K. mapper 181 ≈ 185 (no PRG bank).

    /// <summary>True when $8000-$9FFF is a switchable 8KB window (MMC2); $A000-$FFFF fixed.</summary>
    public bool HasSwitchable8KWindow =>
        _rom.Mapper is 9;

    /// <summary>True when $8000-$BFFF is a switchable 16KB window.</summary>
    public bool HasSwitchable16KWindow =>
        _rom.Mapper is 2 or 6 or 29 or 30 or 31 or 40 or 42 or 43 or 50 or 56 or 70 or 71 or 78 or 81 or 84 or 89
            or 91 or 92 or 93 or 94 or 96 or 97 or 98 or 111 or 117 or 142 or 152 or 153 or 154 or 156 or 157
            or 159 or 163 or 164 or 168 or 180 or 183 or 188 or 190 or 222 or 224 or 232
            or 1 or 105 or 155 or 161
            or 4 or 12 or 14 or 37 or 44 or 45 or 47 or 49 or 52 or 64 or 74 or 76 or 88 or 90 or 95 or 100
            or 114 or 115 or 116 or 118 or 119 or 121 or 123 or 126 or 131 or 151 or 158 or 160 or 165 or 182 or 189 or 191 or 192 or 193 or 194
            or 195 or 196 or 197 or 198 or 199 or 205 or 206 or 207 or 209 or 211 or 214 or 215 or 217 or 219
            or 238 or 245 or 247 or 248 or 249 or 250 or 251 or 254 or 260 or 262 or 264 or 265 or 267 or 268 or 269 or 270 or 281 or 282 or 286 or 287
            or 291 or 292 or 293 or 294 or 295 or 296 or 297 or 300 or 301 or 302 or 307 or 309 or 312 or 315 or 320 or 321 or 322 or 323 or 324
            or 327 or 331 or 333 or 336 or 338 or 339
            or 340 or 341 or 344 or 345 or 348 or 349 or 351 or 353 or 356 or 357 or 358 or 359 or 361 or 364 or 365 or 366 or 367 or 369
            or 368 or 370 or 371 or 372 or 373 or 376 or 377 or 381 or 382 or 383 or 384 or 385 or 386 or 387 or 388 or 391 or 392 or 393 or 394 or 395 or 396 or 397 or 398
            or 401 or 404 or 405 or 407 or 408 or 409 or 410 or 412 or 417 or 419 or 422
            or 430 or 431 or 432 or 434 or 435 or 437 or 439
            or 441 or 443 or 444 or 445 or 447 or 448 or 450 or 451 or 455 or 456 or 457 or 461 or 467
            or 460 or 472 or 512 or 513 or 515 or 516 or 517 or 521 or 522 or 523 or 525 or 526 or 527 or 528 or 529 or 530 or 534 or 536 or 537 or 538 or 540 or 541
            or 542 or 544 or 547 or 548 or 552
            or 10 or 5 or 69
            or 16 or 17 or 18
            or 19 or 210
            or 21 or 22 or 23 or 24 or 25 or 26 or 27
            or 28 or 32 or 33 or 38 or 46 or 48 or 65 or 67 or 68 or 72 or 80 or 82 or 83 or 103 or 106 or 112
            or 73 or 75 or 85
            or 109 or 110 or 132 or 133 or 134 or 135 or 136 or 137 or 138 or 139 or 141 or 143 or 145 or 146 or 147 or 148
            or 149 or 150 or 171 or 172 or 173 or 220 or 223 or 234 or 240 or 243 or 246 or 252 or 253
            or 256 or 263 or 298 or 303 or 308 or 313 or 325 or 362 or 363 or 406 or 411;

    /// <summary>True when any write to $8000-$FFFF selects the PRG bank (bus-conflict style).</summary>
    public bool BusConflictBankSwitch =>
        _rom.Mapper is 2 or 6 or 7 or 8 or 11 or 15 or 29 or 30 or 34 or 35 or 36 or 39 or 40 or 42 or 43 or 54
            or 56 or 57 or 58 or 59 or 60 or 66 or 70 or 71 or 72 or 77 or 78 or 81 or 83 or 84 or 89 or 91 or 92
            or 93 or 94 or 96 or 97 or 98 or 107 or 112 or 117 or 129 or 142 or 144 or 152 or 154 or 166 or 167 or 169
            or 171 or 174 or 175 or 176 or 177 or 178 or 179 or 180 or 190 or 203 or 204 or 213
            or 222 or 224 or 226 or 228 or 229 or 230 or 231 or 232 or 233 or 235 or 236 or 237 or 239 or 241
            or 242 or 244 or 255 or 261 or 265 or 271 or 283 or 285 or 288 or 289 or 290 or 293 or 297 or 299 or 300 or 301 or 310 or 314 or 324 or 329
            or 331 or 335 or 337 or 338 or 340 or 341 or 346 or 349
            or 350 or 354 or 375 or 378 or 379 or 380 or 381 or 382 or 385 or 389 or 390 or 402 or 409 or 414 or 416 or 433 or 435 or 438
            or 434 or 439 or 449 or 452 or 453 or 454 or 470 or 471 or 514 or 519 or 550 or 551;
    // Note: 168 Racermate — no bus conflicts. 161 ≡ MMC1 (serial). 181 ≈ 185 (no PRG bank).

    /// <summary>True when absolute stores to the MMC2/MMC4 PRG register select the switchable bank.</summary>
    public bool HasMmc2StylePrgRegister =>
        _rom.Mapper is 9 or 10;

    /// <summary>True when absolute stores to $4100-$5FFF select a NINA/Sachen-style bank.</summary>
    public bool HasNinaBankRegister =>
        _rom.Mapper is 79 or 109 or 110 or 113 or 132 or 133 or 135 or 136 or 137 or 138 or 139 or 141 or 143
            or 145 or 146 or 147 or 148 or 150 or 172 or 173 or 243 or 256 or 262 or 294 or 369 or 403 or 405 or 407 or 408 or 419 or 423 or 424 or 425 or 426 or 427 or 436 or 455 or 456 or 468 or 469 or 561 or 562;

    /// <summary>
    /// True when $8000-$BFFF is fixed to bank 0 and $C000-$FFFF is the switchable 16KB window
    /// (Crazy Climber / inverted UxROM, mapper 180).
    /// </summary>
    public bool HasFixedFirst16KWindow =>
        _rom.Mapper is 180;

    /// <summary>True when absolute stores to VRC-style PRG0 near $8000 select the switchable bank.</summary>
    public bool HasVrcStylePrg0Register =>
        _rom.Mapper is 21 or 22 or 23 or 25 or 27 or 75 or 85 or 151 or 183 or 298 or 308 or 527 or 530;

    /// <summary>Size of one switchable PRG window unit.</summary>
    public int BankSize =>
        HasSwitchable32KWindow ? 0x8000
        : HasSwitchable8KWindow ? 0x2000
        : 0x4000;

    /// <summary>Number of switchable window banks (units of <see cref="BankSize"/>).</summary>
    public int SwitchableBankCount
    {
        get
        {
            int prgLen = Math.Max(_rom.PrgRom.Length, BankSize);
            if (HasSwitchable32KWindow)
                return Math.Max(1, prgLen / 0x8000);
            if (HasSwitchable8KWindow)
                return Math.Max(1, prgLen / 0x2000);
            return Math.Max(1, PrgBanks);
        }
    }

    /// <summary>Last switchable-bank index (also used as fixed-window site id for 16K mappers).</summary>
    public int FixedBank => SwitchableBankCount - 1;

    public bool IsFixedCpuAddress(ushort cpuAddress) =>
        HasSwitchable32KWindow ? false
        : HasSwitchable8KWindow ? cpuAddress >= 0xA000
        : HasFixedFirst16KWindow ? cpuAddress < 0xC000
        : cpuAddress >= 0xC000;

    public bool IsSwitchableCpuAddress(ushort cpuAddress) =>
        cpuAddress >= 0x8000 && !IsFixedCpuAddress(cpuAddress);

    /// <summary>Site bank id stored with a decoded instruction at <paramref name="cpuAddress"/>.</summary>
    public int SiteBank(ushort cpuAddress, int switchableBank)
    {
        if (HasSwitchable32KWindow)
            return switchableBank;

        if (HasSwitchable8KWindow && cpuAddress >= 0xA000)
        {
            int off = cpuAddress - 0xA000;
            int fixedStart = Math.Max(0, SwitchableBankCount - 3);
            return fixedStart + off / 0x2000;
        }

        if (HasFixedFirst16KWindow)
            return cpuAddress < 0xC000 ? 0 : switchableBank;

        if (cpuAddress >= 0xC000)
            return FixedBank;

        return switchableBank;
    }

    public int MaskBank(int value)
    {
        int count = SwitchableBankCount;
        int mask = count > 0 ? count - 1 : 0;
        return value & mask;
    }

    /// <summary>
    /// Extract the PRG bank index from a bus-conflict style write value.
    /// Packing follows NESDev bit layouts (Color Dreams / Holy Diver use low PRG bits; GxROM high).
    /// </summary>
    public int ExtractPrgBankFromBusWrite(byte value) =>
        _rom.Mapper switch
        {
            // Color Dreams: CCCC LLPP — PRG in bits 0–1
            11 => MaskBank(value & 0x03),
            // GxROM: xxPP xxCC — PRG in bits 4–5
            66 => MaskBank(value >> 4),
            // Holy Diver: CCCC MPPP — PRG in bits 0–2
            78 => MaskBank(value & 0x07),
            // UN1ROM: xxxP PPxx — PRG in bits 2–4
            94 => MaskBank(value >> 2),
            // RET-CUFROM: ...P PPCC — PRG in bits 2–4
            29 => MaskBank(value >> 2),
            // Racermate: PPxx CCCC — PRG in bits 6–7
            168 => MaskBank(value >> 6),
            _ => MaskBank(value),
        };

    /// <summary>True when <paramref name="storeAddr"/> is the MMC2/MMC4 PRG bank register.</summary>
    public bool IsMmc2StylePrgRegisterWrite(ushort storeAddr) =>
        HasMmc2StylePrgRegister && storeAddr >= 0xA000 && storeAddr <= 0xAFFF;

    /// <summary>True when <paramref name="storeAddr"/> is the NINA-003/006 / 113 bank-select register.</summary>
    public bool IsNinaBankRegisterWrite(ushort storeAddr) =>
        HasNinaBankRegister && storeAddr >= 0x4100 && storeAddr <= 0x5FFF;

    /// <summary>Extract PRG bank from a NINA packed write (bits 7–3).</summary>
    public int ExtractPrgBankFromNinaWrite(byte value) => MaskBank(value >> 3);

    /// <summary>True when <paramref name="storeAddr"/> is a VRC-style PRG0 register ($8000–$8003 / $8000).</summary>
    public bool IsVrcStylePrg0RegisterWrite(ushort storeAddr) =>
        HasVrcStylePrg0Register && storeAddr >= 0x8000 && storeAddr <= 0x8003;

    public bool TryMap(ushort cpuAddress, int switchableBank, out int prgOffset)
    {
        prgOffset = -1;
        if (cpuAddress < 0x8000 || _rom.PrgRom.Length == 0)
            return false;

        int banks = PrgBanks;
        int prgLen = _rom.PrgRom.Length;

        // 16KB NROM: mirrored at $8000 and $C000
        if (prgLen == 0x4000)
        {
            prgOffset = (cpuAddress - 0x8000) % 0x4000;
            return true;
        }

        // Single 32KB / NROM-256: linear $8000-$FFFF → whole image
        if (banks <= 2 && !HasSwitchable16KWindow && !HasSwitchable32KWindow && !HasSwitchable8KWindow)
        {
            int fromBase = cpuAddress - 0x8000;
            if (fromBase < 0 || fromBase >= Math.Min(prgLen, 0x8000))
                return false;
            prgOffset = prgLen <= 0x8000 ? fromBase : prgLen - 0x8000 + fromBase;
            return prgOffset >= 0 && prgOffset < prgLen;
        }

        int bank = switchableBank;
        if (bank < 0 || bank >= SwitchableBankCount)
            bank = FixedBank;

        // AxROM / BNROM / GxROM: entire $8000-$FFFF is one 32KB bank
        if (HasSwitchable32KWindow)
        {
            prgOffset = bank * 0x8000 + (cpuAddress - 0x8000);
            return prgOffset >= 0 && prgOffset < prgLen;
        }

        // MMC2: 8KB switchable at $8000; fixed last 24KB at $A000-$FFFF
        if (HasSwitchable8KWindow)
        {
            if (cpuAddress >= 0xA000)
            {
                prgOffset = prgLen - 0x6000 + (cpuAddress - 0xA000);
                return prgOffset >= 0 && prgOffset < prgLen;
            }

            prgOffset = bank * 0x2000 + (cpuAddress - 0x8000);
            return prgOffset >= 0 && prgOffset < prgLen;
        }

        // Crazy Climber (180): fixed bank 0 at $8000-$BFFF; switchable at $C000-$FFFF
        if (HasFixedFirst16KWindow)
        {
            if (cpuAddress < 0xC000)
            {
                prgOffset = cpuAddress - 0x8000;
                return prgOffset >= 0 && prgOffset < prgLen;
            }

            prgOffset = bank * BankSize + (cpuAddress - 0xC000);
            return prgOffset >= 0 && prgOffset < prgLen;
        }

        if (cpuAddress >= 0xC000)
        {
            // Fixed last 16KB for UxROM / typical MMC1 16K mode / MMC3 fixed $C000 / MMC4
            prgOffset = FixedBank * BankSize + (cpuAddress - 0xC000);
            return prgOffset >= 0 && prgOffset < prgLen;
        }

        // $8000-$BFFF
        if (HasSwitchable16KWindow)
        {
            prgOffset = bank * BankSize + (cpuAddress - 0x8000);
            return prgOffset >= 0 && prgOffset < prgLen;
        }

        // Fallback: last 32KB window
        int window = Math.Min(prgLen, 0x8000);
        int fromEnd = cpuAddress - 0x8000;
        prgOffset = prgLen - window + fromEnd;
        return prgOffset >= 0 && prgOffset < prgLen;
    }

    /// <summary>
    /// Banks to explore when jumping into a switchable window without a known switch.
    /// Full static recovery: every switchable bank unit.
    /// </summary>
    public IEnumerable<int> BanksForUncertainSwitchableTarget()
    {
        if (!HasSwitchable16KWindow && !HasSwitchable32KWindow && !HasSwitchable8KWindow)
        {
            yield return 0;
            yield break;
        }

        for (int b = 0; b < SwitchableBankCount; b++)
            yield return b;
    }
}
