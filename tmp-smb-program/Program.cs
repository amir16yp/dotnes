// Decompiled game logic (optimized soft-CPU recovery — default).
// Same control-flow / register semantics; structured for reading.
// Not a byte-faithful PRG encoding. Pass --exact for literal recovery.
// Compilable soft-CPU C# (neslib). SoftCpu lowers this to 6502; prg.bin fills data gaps.
// Mapper=0, PRG banks=2, CHR banks=1,
// Mirroring=Vertical, Battery=False
// Vectors: NMI=$8082 RESET=$8000 IRQ=$FFF0
// Recovered 6049 instruction sites across 275 routines.
// Recovered 7 PPU palette VRAM-buffer table(s) from PRG.
// Recovered 1 VRAM buffer pointer table(s) that select palettes.
// Recovered 10 nametable text VRAM-buffer stream(s) (tile font).
// Recovered 29 coherent RAM buffer(s) (OAM / VRAM update / generic).
// Memory accesses (peek / poke counts in recovered code):
//   PPU_CTRL                      peek×0  poke×5
//   PPU_MASK                      peek×0  poke×3
//   PPU_STATUS                    peek×6  poke×0
//   PPU_OAM_ADDR                  peek×0  poke×1
//   PPU_SCROLL                    peek×0  poke×4
//   PPU_ADDR                      peek×0  poke×8
//   PPU_DATA                      peek×0  poke×1
//   APU_PULSE1_CTRL               peek×0  poke×7
//   APU_PULSE2_CTRL               peek×0  poke×5
//   APU_NOISE_CTRL                peek×0  poke×3
//   PPU_OAM_DMA                   peek×0  poke×1
//   APU_STATUS                    peek×0  poke×14
//   APU_FRAME                     peek×0  poke×1
//   $14                           peek×0  poke×1
//   $1B                           peek×0  poke×1
//   $0138                         peek×0  poke×1
//   $0450                         peek×0  poke×1
//   $0456                         peek×0  poke×1
//   $05B4                         peek×0  poke×1
//   $06B7                         peek×0  poke×1
//   $0795                         peek×0  poke×1
//   $4DC9                         peek×1  poke×0
//   $6991                         peek×1  poke×0
//   $838B                         peek×1  poke×0
//   $CFB3                         peek×1  poke×0
//   $EEB5                         peek×1  poke×0
//   (ptr_04_lo | ptr_04_hi <<...  peek×2  poke×0
//   (ptr_06_lo | ptr_06_hi <<...  peek×4  poke×5
//   (ptr_E9_lo | ptr_E9_hi <<...  peek×14  poke×0
//   (ptr_F5_lo | ptr_F5_hi <<...  peek×6  poke×0
//   (ushort)0x0400 + x            peek×0  poke×1
//   (ushort)0x0417 + x            peek×0  poke×1
//   (ushort)0x042C + x            peek×0  poke×1
//   0x0400 + x                    peek×1  poke×0
//   0x0416 + x                    peek×1  poke×0
//   0x0417 + x                    peek×1  poke×0
//   0x042C + x                    peek×1  poke×0
//   0x1E1D + x                    peek×1  poke×0
//   0x823F + x                    peek×1  poke×0
//   0x831D + y                    peek×1  poke×0
//   0x833F + x                    peek×1  poke×0
//   0x8354 + x                    peek×1  poke×0
//   0x849F + x                    peek×1  poke×0
//   0x84A0 + x                    peek×1  poke×0
//   0x84B7 + y                    peek×2  poke×0
//   0x85C3 + x                    peek×1  poke×0
//   0x85CF + y                    peek×1  poke×0
//   0x85D7 + y                    peek×1  poke×0
//   0x89C3 + y                    peek×1  poke×0
//   0x89C9 + y                    peek×1  poke×0
//   0x89D1 + y                    peek×1  poke×0
//   0x8A39 + x                    peek×1  poke×0
//   0x8A3A + x                    peek×1  poke×0
//   0x8A3B + x                    peek×1  poke×0
//   0x8A3C + x                    peek×1  poke×0
//   0x8EF4 + y                    peek×1  poke×0
//   0x8EF5 + y                    peek×1  poke×0
//   0x8F00 + x                    peek×1  poke×0
//   0x90E7 + y                    peek×1  poke×0
//   0x9BDF + y                    peek×1  poke×0
//   0x9BF8 + y                    peek×1  poke×0
//   0x9CB4 + y                    peek×1  poke×0
//   0x9CBC + x                    peek×1  poke×0
//   0x9CBC + y                    peek×1  poke×0
//   0xB036 + y                    peek×1  poke×0
//   0xB424 + y                    peek×1  poke×0
//   0xB42B + y                    peek×1  poke×0
//   0xB432 + y                    peek×1  poke×0
//   0xB439 + y                    peek×1  poke×0
//   0xB440 + y                    peek×1  poke×0
//   0xB443 + y                    peek×1  poke×0
//   0xB447 + y                    peek×1  poke×0
//   0xB58C + y                    peek×1  poke×0
//   0xB687 + y                    peek×1  poke×0
//   0xB74D + y                    peek×1  poke×0
//   0xB84B + y                    peek×1  poke×0
//   0xB9BA + y                    peek×2  poke×0
//   0xBA31 + y                    peek×1  poke×0
//   0xBA92 + x                    peek×1  poke×0
//   0xBAD2 + y                    peek×1  poke×0
//   0xBB38 + x                    peek×1  poke×0
//   0xBBFC + y                    peek×1  poke×0
//   0xBDD2 + x                    peek×1  poke×0
//   0xBDD5 + x                    peek×1  poke×0
//   0xBDE8 + y                    peek×1  poke×0
//   0xBF9F + y                    peek×1  poke×0
//   0xC06B + y                    peek×1  poke×0
//   0xC076 + y                    peek×1  poke×0
//   0xC081 + y                    peek×1  poke×0
//   0xD736 + y                    peek×1  poke×0
//   0xD84F + y                    peek×1  poke×0
//   0xD851 + y                    peek×2  poke×0
//   0xD892 + y                    peek×1  poke×0
//   0xD965 + y                    peek×2  poke×0
//   0xD9D2 + y                    peek×2  poke×0
//   0xDC62 + x                    peek×1  poke×0
//   0xDE03 + y                    peek×1  poke×0
//   0xDE29 + x                    peek×1  poke×0
//   0xDF8B + x                    peek×1  poke×0
//   0xDF96 + x                    peek×1  poke×0
//   0xDFBF + y                    peek×1  poke×0
//   0xE3AD + y                    peek×1  poke×0
//   0xE3B0 + y                    peek×1  poke×0
//   0xE4D0 + x                    peek×1  poke×0
//   0xE4D4 + x                    peek×1  poke×0
//   0xE4D8 + x                    peek×1  poke×0
//   0xE541 + x                    peek×1  poke×0
//   0xE542 + x                    peek×1  poke×0
//   0xE682 + x                    peek×1  poke×0
//   0xE73E + x                    peek×1  poke×0
//   0xE73F + x                    peek×1  poke×0
//   0xE840 + y                    peek×1  poke×0
//   0xE85B + y                    peek×1  poke×0
//   0xE876 + y                    peek×1  poke×0
//   0xE878 + x                    peek×1  poke×0
//   0xEBCD + x                    peek×1  poke×0
//   0xEBCE + x                    peek×1  poke×0
//   0xED06 + x                    peek×1  poke×0
//   0xEEE7 + x                    peek×1  poke×0
//   0xF09C + x                    peek×1  poke×0
//   0xF09C + y                    peek×1  poke×0
//   0xF1E3 + x                    peek×1  poke×0
//   0xF22B + x                    peek×1  poke×0
//   0xF237 + y                    peek×1  poke×0
//   0xF3B0 + y                    peek×1  poke×0
//   0xF4D3 + y                    peek×2  poke×0
//   0xF4D9 + y                    peek×2  poke×0
//   0xF4F8 + y                    peek×2  poke×0
//   0xF90C + y                    peek×2  poke×0
//   0xF90D + y                    peek×2  poke×0
//   0xF90E + y                    peek×2  poke×0
//   0xF90F + y                    peek×2  poke×0
//   0xF910 + y                    peek×2  poke×0
//   0xF911 + y                    peek×2  poke×0
//   0xF912 + y                    peek×2  poke×0
//   0xFF66 + y                    peek×1  poke×0
//   0xFF96 + y                    peek×1  poke×0
//   0xFF9A + y                    peek×1  poke×0
//   0xFFA2 + y                    peek×1  poke×0
//   0xFFC9 + y                    peek×1  poke×0
//   0xFFEA + y                    peek×1  poke×0
//   APU_DMC_RAW                   peek×0  poke×3
//   APU_NOISE_LENGTH              peek×0  poke×2
//   APU_NOISE_PERIOD              peek×0  poke×2
//   APU_PULSE1_SWEEP              peek×0  poke×5
//   APU_PULSE1_TIMER_HI + x       peek×0  poke×3
//   APU_PULSE1_TIMER_LO           peek×0  poke×3
//   APU_PULSE1_TIMER_LO + x       peek×0  poke×3
//   APU_PULSE2_SWEEP              peek×0  poke×4
//   APU_PULSE2_TIMER_LO           peek×0  poke×1
//   APU_TRIANGLE_CTRL             peek×0  poke×3
//   ram_0416                      peek×0  poke×1
//   ram_0416 + x                  peek×0  poke×1
//   rom_AE88                      peek×2  poke×0

#nullable disable
#pragma warning disable CS8321 // unused local function (keep all discovered routines)
#pragma warning disable CS0162 // unreachable code from literal branches
#pragma warning disable CS0164 // unreferenced labels from partial CFG
// Coherent RAM buffers (SoftCpu maps name → base via // $…. ; name[x|y] → abs,X/Y).
byte[] buf_000F = new byte[7]; // $000F
byte[] buf_0016 = new byte[8]; // $0016
byte[] obj = new byte[13]; // $001E
byte[] obj_003A = new byte[12]; // $003A
byte[] obj_0046 = new byte[18]; // $0046
byte[] obj_0058 = new byte[13]; // $0058
byte[] buf_006D = new byte[2]; // $006D
byte[] obj_006E = new byte[22]; // $006E
byte[] buf_0086 = new byte[2]; // $0086
byte[] obj_0087 = new byte[22]; // $0087
byte[] obj_009F = new byte[23]; // $009F
byte[] obj_00B6 = new byte[22]; // $00B6
byte[] buf_00CE = new byte[2]; // $00CE
byte[] obj_00CF = new byte[13]; // $00CF
byte[] obj_00E4 = new byte[16]; // $00E4
byte[] buf_0110 = new byte[36]; // $0110
byte[] buf_0134 = new byte[16]; // $0134
byte[] oam = new byte[256]; // $0200
byte[] vram_buf = new byte[250]; // $0300
byte[] buf_0433 = new byte[12]; // $0433
byte[] buf_046B = new byte[13]; // $046B
byte[] buf_047D = new byte[16]; // $047D
byte[] buf_0491 = new byte[16]; // $0491
byte[] buf_04AC = new byte[8]; // $04AC
byte[] buf_06BE = new byte[54]; // $06BE
byte[] game = new byte[143]; // $06FC
byte[] buf_0796 = new byte[16]; // $0796
byte[] buf_07A8 = new byte[16]; // $07A8
byte[] score = new byte[7]; // $07D7
byte[] oam_y = new byte[256]; // $0200 (OAM Y)
byte[] oam_tile = new byte[256]; // $0201 (OAM tile)
byte[] oam_attr = new byte[256]; // $0202 (OAM attr)
byte[] oam_x = new byte[256]; // $0203 (OAM X)

// ROM tables recovered from indexed peeks (SoftCpu maps name → $addr).
byte[] rom_EE07 = new byte[] { 0x20, 0x28, 0xC8, 0x18, 0x00, 0x40, 0x50, 0x58, 0x80, 0x88, 0xB8, 0x78, 0x60, 0xA0, 0xB0, 0xB8, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F, 0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x08, 0x09, 0x28, 0x29, 0x2A, 0x2B, 0x2C, 0x2D }; // $EE07
byte[] rom_FF00 = new byte[] { 0x00, 0x88, 0x00, 0x2F, 0x00, 0x00, 0x02, 0xA6, 0x02, 0x80, 0x02, 0x5C, 0x02, 0x3A, 0x02, 0x1A, 0x01, 0xDF, 0x01, 0xC4, 0x01, 0xAB, 0x01, 0x93, 0x01, 0x7C, 0x01, 0x67, 0x01, 0x53, 0x01, 0x40, 0x01, 0x2E, 0x01, 0x1D, 0x01, 0x0D, 0x00, 0xFE, 0x00, 0xEF, 0x00, 0xE2, 0x00, 0xD5, 0x00, 0xC9, 0x00, 0xBE, 0x00, 0xB3, 0x00, 0xA9, 0x00, 0xA0, 0x00, 0x97, 0x00, 0x8E, 0x00, 0x86, 0x00, 0x77 }; // $FF00

// Hot RAM as variables (SoftCpu maps each name → address via // $…. ).
byte vram_ptr_lo = 0; // $00
byte vram_ptr_hi = 0; // $01
byte zp_02 = 0; // $02
byte zp_03 = 0; // $03
byte ptr_04_lo = 0; // $04
byte ptr_04_hi = 0; // $05
byte ptr_06_lo = 0; // $06
byte ptr_06_hi = 0; // $07
byte zp_08 = 0; // $08
byte zp_09 = 0; // $09
byte zp_0A = 0; // $0A
byte zp_0B = 0; // $0B
byte zp_0C = 0; // $0C
byte zp_0D = 0; // $0D
byte zp_0E = 0; // $0E
byte zp_1D = 0; // $1D
byte VRAM_INC_32 = 0; // $32
byte zp_33 = 0; // $33
byte zp_39 = 0; // $39
byte zp_45 = 0; // $45
byte zp_57 = 0; // $57
byte buf_006D_00 = 0; // $6D
byte buf_0086_00 = 0; // $86
byte obj_009F_00 = 0; // $9F
byte obj_009F_16 = 0; // $B5
byte f_C882_C9 = 0; // $C9
byte buf_00CE_00 = 0; // $CE
byte ptr_E9_lo = 0; // $E9
byte ptr_E9_hi = 0; // $EA
byte zp_EB = 0; // $EB
byte zp_EC = 0; // $EC
byte zp_ED = 0; // $ED
byte zp_EF = 0; // $EF
byte zp_F0 = 0; // $F0
byte zp_F1 = 0; // $F1
byte zp_F2 = 0; // $F2
byte zp_F3 = 0; // $F3
byte zp_F4 = 0; // $F4
byte ptr_F5_lo = 0; // $F5
byte ptr_F5_hi = 0; // $F6
byte zp_F7 = 0; // $F7
byte zp_F8 = 0; // $F8
byte zp_F9 = 0; // $F9
byte zp_FA = 0; // $FA
byte zp_FB = 0; // $FB
byte zp_FC = 0; // $FC
byte zp_FD = 0; // $FD
byte zp_FE = 0; // $FE
byte zp_FF = 0; // $FF
byte ram_0109 = 0; // $0109
byte ram_010D = 0; // $010D
byte f_B855_010E = 0; // $010E
byte ram_010F = 0; // $010F
byte ram_0139 = 0; // $0139
byte ram_0340 = 0; // $0340
byte f_E87D_0363 = 0; // $0363
byte ram_036A = 0; // $036A
byte f_C882_03A0 = 0; // $03A0
byte ram_03A1 = 0; // $03A1
byte ram_03AE = 0; // $03AE
byte ram_03AF = 0; // $03AF
byte ram_03B1 = 0; // $03B1
byte ram_03B3 = 0; // $03B3
byte ram_03BA = 0; // $03BA
byte ram_03BC = 0; // $03BC
byte ram_03C4 = 0; // $03C4
byte ram_03D1 = 0; // $03D1
byte ram_03D4 = 0; // $03D4
byte ram_03D6 = 0; // $03D6
byte ram_03EE = 0; // $03EE
byte f_8A61_03F0 = 0; // $03F0
byte buf_0433_00 = 0; // $0433
byte buf_047D_00 = 0; // $047D
byte f_D73E_0483 = 0; // $0483
byte ram_0484 = 0; // $0484
byte ram_0490 = 0; // $0490
byte ram_0499 = 0; // $0499
byte f_92C8_06A0 = 0; // $06A0
byte f_BCED_06BC = 0; // $06BC
byte ram_06CB = 0; // $06CB
byte ram_06CC = 0; // $06CC
byte f_C0CC_06CD = 0; // $06CD
byte f_B624_06CE = 0; // $06CE
byte f_C71B_06D3 = 0; // $06D3
byte f_89E1_06D4 = 0; // $06D4
byte ram_06D5 = 0; // $06D5
byte f_C0CC_06D9 = 0; // $06D9
byte f_C0CC_06DA = 0; // $06DA
byte ram_06DE = 0; // $06DE
byte f_81C6_06E0 = 0; // $06E0
byte buf_06BE_26 = 0; // $06E4
byte game_00 = 0; // $06FC
byte ram_06FD = 0; // $06FD
byte f_AF93_06FF = 0; // $06FF
byte ram_0700 = 0; // $0700
byte f_B55E_0701 = 0; // $0701
byte f_B55E_0702 = 0; // $0702
byte ram_0703 = 0; // $0703
byte ram_0704 = 0; // $0704
byte ram_0705 = 0; // $0705
byte ram_0709 = 0; // $0709
byte ram_070B = 0; // $070B
byte ram_070C = 0; // $070C
byte ram_070D = 0; // $070D
byte ram_070E = 0; // $070E
byte ram_070F = 0; // $070F
byte ram_0710 = 0; // $0710
byte ram_0711 = 0; // $0711
byte ram_0712 = 0; // $0712
byte ram_0714 = 0; // $0714
byte ram_0716 = 0; // $0716
byte f_836B_0717 = 0; // $0717
byte f_836B_0718 = 0; // $0718
byte ram_071A = 0; // $071A
byte ram_071B = 0; // $071B
byte ram_071C = 0; // $071C
byte ram_071D = 0; // $071D
byte ram_071F = 0; // $071F
byte ram_0722 = 0; // $0722
byte ram_0723 = 0; // $0723
byte ram_0725 = 0; // $0725
byte ram_0726 = 0; // $0726
byte f_C08C_072A = 0; // $072A
byte ram_0739 = 0; // $0739
byte ram_073A = 0; // $073A
byte ram_073B = 0; // $073B
byte ram_073D = 0; // $073D
byte scroll = 0; // $073F
byte ram_0740 = 0; // $0740
byte ram_0743 = 0; // $0743
byte f_C0CC_0745 = 0; // $0745
byte ram_0747 = 0; // $0747
byte ram_0748 = 0; // $0748
byte ram_074E = 0; // $074E
byte ram_0750 = 0; // $0750
byte ram_0751 = 0; // $0751
byte ram_0752 = 0; // $0752
byte ram_0753 = 0; // $0753
byte ram_0754 = 0; // $0754
byte ram_0755 = 0; // $0755
byte ram_0756 = 0; // $0756
byte ram_0757 = 0; // $0757
byte ram_0758 = 0; // $0758
byte ram_0759 = 0; // $0759
byte game_5E = 0; // $075A
byte ram_075D = 0; // $075D
byte f_BBFE_075E = 0; // $075E
byte ram_075F = 0; // $075F
byte ram_0760 = 0; // $0760
byte f_82D8_0764 = 0; // $0764
byte f_B04A_0769 = 0; // $0769
byte ram_076A = 0; // $076A
byte f_8247_076B = 0; // $076B
byte ram_0770 = 0; // $0770
byte ram_0772 = 0; // $0772
byte ram_0773 = 0; // $0773
byte ram_0774 = 0; // $0774
byte ram_0775 = 0; // $0775
byte ram_0776 = 0; // $0776
byte ram_0777 = 0; // $0777
byte ppu_ctrl_shadow = 0; // $0778
byte ppu_mask_shadow = 0; // $0779
byte ram_077A = 0; // $077A
byte ram_077F = 0; // $077F
byte f_8247_0780 = 0; // $0780
byte ram_0781 = 0; // $0781
byte ram_0782 = 0; // $0782
byte f_B51C_0783 = 0; // $0783
byte ram_0784 = 0; // $0784
byte ram_0785 = 0; // $0785
byte f_B74F_0787 = 0; // $0787
byte ram_078F = 0; // $078F
byte ram_0791 = 0; // $0791
byte f_B6F9_0792 = 0; // $0792
byte f_BCED_079D = 0; // $079D
byte ram_079E = 0; // $079E
byte ram_079F = 0; // $079F
byte ram_07A0 = 0; // $07A0
byte ram_07A2 = 0; // $07A2
byte ram_07A7 = 0; // $07A7
byte ram_07B0 = 0; // $07B0
byte ram_07B1 = 0; // $07B1
byte f_F2D0_07B2 = 0; // $07B2
byte f_F73A_07B3 = 0; // $07B3
byte ram_07B4 = 0; // $07B4
byte f_F73A_07B5 = 0; // $07B5
byte ram_07B6 = 0; // $07B6
byte f_F73A_07B7 = 0; // $07B7
byte f_F73A_07B8 = 0; // $07B8
byte ram_07B9 = 0; // $07B9
byte ram_07BA = 0; // $07BA
byte ram_07BB = 0; // $07BB
byte ram_07BD = 0; // $07BD
byte ram_07BE = 0; // $07BE
byte f_F667_07BF = 0; // $07BF
byte ram_07C0 = 0; // $07C0
byte ram_07C1 = 0; // $07C1
byte ram_07C4 = 0; // $07C4
byte ram_07C5 = 0; // $07C5
byte f_F2D0_07C6 = 0; // $07C6
byte ram_07C7 = 0; // $07C7
byte ram_07CA = 0; // $07CA
byte f_B74F_07F8 = 0; // $07F8
byte f_B74F_07F9 = 0; // $07F9
byte f_B74F_07FA = 0; // $07FA
byte ram_07FC = 0; // $07FC
byte ram_07FD = 0; // $07FD
byte reset_07FF = 0; // $07FF
byte f_C26C_0EC3 = 0; // $0EC3
byte BG_TABLE_1000 = 0; // $1000
byte f_C26C_1EC3 = 0; // $1EC3

// Address constants (indexed bases / ROM / mapper — keep peek/poke).
const ushort ram_0416 = 0x0416;
const ushort rom_AE88 = 0xAE88;

// PPU palette VRAM-buffer records recovered from PRG ($3F,$00,$len + colors).
(ushort Base, byte[] Data)[] palette =
{
    (0x8CA4, new byte[] { 0x3F, 0x00, 0x20, NESColor.Black, NESColor.Rose, NESColor.Blue, NESColor.LightRose, NESColor.Black, NESColor.PaleGreen, NESColor.Green, NESColor.Black, NESColor.Black, NESColor.White, NESColor.Blue, NESColor.Black, NESColor.Black, NESColor.LightOrange, NESColor.Blue, NESColor.Black, NESColor.LightBlue, NESColor.Red, NESColor.LightOrange, NESColor.Gold, NESColor.Black, NESColor.Gray, NESColor.White, NESColor.LightOrange, NESColor.Black, NESColor.Red, NESColor.White, NESColor.LightOrange, NESColor.Black, NESColor.Black, NESColor.White, NESColor.Gray }),
    (0x8CC8, new byte[] { 0x3F, 0x00, 0x20, NESColor.Black, NESColor.LightLime, NESColor.Green, NESColor.Black, NESColor.Black, NESColor.PaleRed, NESColor.Orange, NESColor.Black, NESColor.Black, NESColor.White, NESColor.LightAzure, NESColor.Black, NESColor.Black, NESColor.LightOrange, NESColor.Orange, NESColor.Black, NESColor.Black, NESColor.Red, NESColor.LightOrange, NESColor.Gold, NESColor.Black, NESColor.Green, NESColor.White, NESColor.LightOrange, NESColor.Black, NESColor.Red, NESColor.White, NESColor.LightOrange, NESColor.Black, NESColor.Black, NESColor.PaleRed, NESColor.Orange }),
    (0x8CEC, new byte[] { 0x3F, 0x00, 0x20, NESColor.Black, NESColor.LightLime, NESColor.Green, NESColor.DarkLime, NESColor.Black, NESColor.PaleCyan, NESColor.Cyan, NESColor.Black, NESColor.Black, NESColor.White, NESColor.LightAzure, NESColor.Cyan, NESColor.Black, NESColor.LightOrange, NESColor.Orange, NESColor.Cyan, NESColor.Black, NESColor.Red, NESColor.LightOrange, NESColor.Gold, NESColor.Black, NESColor.Cyan, NESColor.PaleRed, NESColor.Orange, NESColor.Black, NESColor.Red, NESColor.White, NESColor.LightOrange, NESColor.Black, NESColor.DarkCyan, NESColor.PaleCyan, NESColor.Cyan }),
    (0x8D10, new byte[] { 0x3F, 0x00, 0x20, NESColor.Black, NESColor.White, NESColor.Gray, NESColor.DarkGray, NESColor.Black, NESColor.White, NESColor.Gray, NESColor.DarkGray, NESColor.Black, NESColor.White, NESColor.Red, NESColor.DarkGray, NESColor.Black, NESColor.LightOrange, NESColor.Orange, NESColor.DarkGray, NESColor.Black, NESColor.Red, NESColor.LightOrange, NESColor.Gold, NESColor.Black, NESColor.Cyan, NESColor.PaleRed, NESColor.Orange, NESColor.Black, NESColor.Red, NESColor.White, NESColor.LightOrange, NESColor.Black, NESColor.DarkGray, NESColor.White, NESColor.Gray }),
    (0x8D34, new byte[] { 0x3F, 0x00, 0x04, NESColor.LightBlue, NESColor.White, NESColor.DarkGray, NESColor.Gray }),
    (0x8D3C, new byte[] { 0x3F, 0x00, 0x04, NESColor.Black, NESColor.White, NESColor.DarkGray, NESColor.Gray }),
    (0x8D44, new byte[] { 0x3F, 0x00, 0x04, NESColor.LightBlue, NESColor.LightOrange, NESColor.Red, NESColor.Black })
};

// Nametable VRAM text buffers (ppuHi,ppuLo,len,tiles…,00).
// Text string only when a tile font decodes to English; otherwise Data is the source of truth.
(ushort Base, byte[] Data, string Text)[] vramText =
{
    (0x8752, new byte[] { 0x20, 0x43, 0x05, 0x16, 0x0A, 0x1B, 0x12, 0x18, 0x20, 0x52, 0x0B, 0x20, 0x18, 0x1B, 0x15, 0x0D, 0x24, 0x24, 0x1D, 0x12, 0x16, 0x0E, 0x20, 0x68, 0x05, 0x00, 0x24, 0x24, 0x2E, 0x29, 0x23, 0xC0, 0x7F, 0xAA, 0x23, 0xC2, 0x01, 0xEA }, "MARIO | WORLD  TIME"),
    (0x8798, new byte[] { 0x21, 0xCD, 0x05, 0x16, 0x0A, 0x1B, 0x12, 0x18, 0x22, 0x0C, 0x07, 0x1D, 0x12, 0x16, 0x0E, 0x24, 0x1E, 0x19 }, "MARIO | TIME UP"),
    (0x87AB, new byte[] { 0x21, 0xCD, 0x05, 0x16, 0x0A, 0x1B, 0x12, 0x18, 0x22, 0x0B, 0x09, 0x10, 0x0A, 0x16, 0x0E, 0x24, 0x18, 0x1F, 0x0E, 0x1B }, "MARIO | GAME OVER"),
    (0x8D54, new byte[] { 0x25, 0x48, 0x10, 0x1D, 0x11, 0x0A, 0x17, 0x14, 0x24, 0x22, 0x18, 0x1E, 0x24, 0x16, 0x0A, 0x1B, 0x12, 0x18, 0x2B, 0x00 }, "THANK YOU MARIO!"),
    (0x8D68, new byte[] { 0x25, 0x48, 0x10, 0x1D, 0x11, 0x0A, 0x17, 0x14, 0x24, 0x22, 0x18, 0x1E, 0x24, 0x15, 0x1E, 0x12, 0x10, 0x12, 0x2B, 0x00 }, "THANK YOU LUIGI!"),
    (0x8D7C, new byte[] { 0x25, 0xC5, 0x16, 0x0B, 0x1E, 0x1D, 0x24, 0x18, 0x1E, 0x1B, 0x24, 0x19, 0x1B, 0x12, 0x17, 0x0C, 0x0E, 0x1C, 0x1C, 0x24, 0x12, 0x1C, 0x24, 0x12, 0x17, 0x26, 0x05, 0x0F, 0x0A, 0x17, 0x18, 0x1D, 0x11, 0x0E, 0x1B, 0x24, 0x0C, 0x0A, 0x1C, 0x1D, 0x15, 0x0E, 0x2B, 0x00 }, "BUT OUR PRINCESS IS IN | ANOTHER CASTLE!"),
    (0x8DA8, new byte[] { 0x25, 0xA7, 0x13, 0x22, 0x18, 0x1E, 0x1B, 0x24, 0x1A, 0x1E, 0x0E, 0x1C, 0x1D, 0x24, 0x12, 0x1C, 0x24, 0x18, 0x1F, 0x0E, 0x1B, 0xAF, 0x00 }, ""),
    (0x8DBF, new byte[] { 0x25, 0xE3, 0x1B, 0x20, 0x0E, 0x24, 0x19, 0x1B, 0x0E, 0x1C, 0x0E, 0x17, 0x1D, 0x24, 0x22, 0x18, 0x1E, 0x24, 0x0A, 0x24, 0x17, 0x0E, 0x20, 0x24, 0x1A, 0x1E, 0x0E, 0x1C, 0x1D, 0xAF, 0x00 }, ""),
    (0x8DDE, new byte[] { 0x26, 0x4A, 0x0D, 0x19, 0x1E, 0x1C, 0x11, 0x24, 0x0B, 0x1E, 0x1D, 0x1D, 0x18, 0x17, 0x24, 0x0B, 0x00 }, "PUSH BUTTON B"),
    (0x8DEF, new byte[] { 0x26, 0x88, 0x11, 0x1D, 0x18, 0x24, 0x1C, 0x0E, 0x15, 0x0E, 0x0C, 0x1D, 0x24, 0x0A, 0x24, 0x20, 0x18, 0x1B, 0x15, 0x0D, 0x00 }, "TO SELECT A WORLD")
};

// VRAM buffer pointers (lo$805A/hi$806D) — RAM, palette, or text stream.
ushort[] vramBufPtr =
{
    0x0301, // RAM update buffer
    0x8CA4,
    0x8CC8,
    0x8CEC,
    0x8D10,
    0x0300, // RAM update buffer
    0x0341, // RAM update buffer
    0x0341, // RAM update buffer
    0x8D4C,
    0x8D34,
    0x8D3C,
    0x8D44,
    0x8D54, // "THANK YOU MARIO!"
    0x8D68, // "THANK YOU LUIGI!"
    0x8D7C, // "BUT OUR PRINCESS IS IN | ANOTHER CASTLE!"
    0x8DA8, // text stream
    0x8DBF, // text stream
    0x8DDE, // "PUSH BUTTON B"
    0x8DEF // "TO SELECT A WORLD"
};

byte a = 0, x = 0, y = 0;
bool carry = false, zero = false, negative = false;

// Bind vram_ptr_* to a palette/text row once; (vram_ptr_lo),Y then indexes Data.
byte[] _vramData = null;
ushort _vramBase = 0;
void BindVramFromZp()
{
    ushort p = (ushort)(vram_ptr_lo | vram_ptr_hi << 8);
    if (_vramData != null && p >= _vramBase && p < _vramBase + _vramData.Length) return;
    _vramData = null;
    _vramBase = p;
    for (int i = 0; i < palette.Length; i++)
    {
        if (palette[i].Base == p) { _vramData = palette[i].Data; return; }
    }
    for (int i = 0; i < vramText.Length; i++)
    {
        if (vramText[i].Base == p) { _vramData = vramText[i].Data; return; }
    }
}
byte PeekVramBufY()
{
    ushort addr = (ushort)((vram_ptr_lo | vram_ptr_hi << 8) + y);
    if (_vramData == null || addr < _vramBase || addr >= _vramBase + _vramData.Length)
        BindVramFromZp();
    if (_vramData != null)
    {
        int i = addr - _vramBase;
        if ((uint)i < (uint)_vramData.Length) return _vramData[i];
    }
    return peek(addr);
}

reset();
while (true) ;

// bank=0 $8000
void reset()
{
    sei();
    // CLD
    ppu_ctrl(PPUCTRL.BG_TABLE_1000);
    x = 0xFF;
    txs();
    waitvsync();
    waitvsync();
    y = 0xFE;
    x = 5;
    L_8018: ;
    a = score[x];
    // if (a >= 0x0A) goto L_802B // label not in this routine
    x--;
    if (x < 0x80) goto L_8018;
    a = reset_07FF;
    if (a == 0xA5) { y = 0xD6; }
    func_00_90CC();
    apu_dmc_raw(a);
    game[0x74] = a;
    reset_07FF = 0xA5;
    ram_07A7 = 0xA5;
    apu_status_write(APUSTATUS.PULSE1 | APUSTATUS.PULSE2 | APUSTATUS.TRIANGLE | APUSTATUS.NOISE);
    ppu_mask_write(MASK.EDGE_BG | MASK.EDGE_SPR);
    ClearOam_8220();
    func_00_8E19();
    game[0x78]++;
    a = ppu_ctrl_shadow;
    a |= (byte)PPUCTRL.NMI;
    func_00_8EED();
    while (true) ;
}

// bank=0 $8082
void nmi()
{
    a = ppu_ctrl_shadow;
    a &= (byte)~PPUCTRL.NMI;
    ppu_ctrl_shadow = a;
    a &= (byte)~(PPUCTRL.NAMETABLE_X | PPUCTRL.NMI);
    ppu_ctrl((PPUCTRL)a);
    a = ppu_mask_shadow;
    a &= (byte)(MASK.EDGE_BG | MASK.EDGE_SPR | MASK.TINT_RED | MASK.TINT_GREEN | MASK.TINT_BLUE);
    y = game[0x78];
    if (y == 0)
    {
        a = ppu_mask_shadow;
        a |= (byte)(MASK.EDGE_BG | MASK.EDGE_SPR | MASK.BG | MASK.SPR);
    }
    ppu_mask_shadow = a;
    a &= (byte)~(MASK.BG | MASK.SPR);
    ppu_mask_write((MASK)a);
    x = ppu_status();
    a = 0;
    func_00_8EE6();
    oam_dma(2);
    x = game[0x77];
    {
        ushort _p = vramBufPtr[x];
        vram_ptr_lo = (byte)_p;
        vram_ptr_hi = (byte)(_p >> 8);
    }
    BindVramFromZp();
    func_00_8EDD();
    y = 0;
    x = game[0x77];
    if (x == 6) { y++; }
    // LDX $8080
    a = 0;
    vram_buf[x] = a;
    vram_buf[x + 1] = a;
    game[0x77] = a;
    a = ppu_mask_shadow;
    ppu_mask_write((MASK)a);
    func_F2D0();
    func_00_8E5C();
    func_00_8182();
    func_00_8F97();
    a = game[0x7A];
    if ((a & 1) != 0) goto L_811B;
    a = game[0x4B];
    if (a != 0)
    {
        game[0x4B]--;
        if (zero)
        {
            }
            x = 0x14;
            game[0x83]--;
            // if (!negative) goto L_810E // label not in this routine
            game[0x83] = 0x14;
            x = 0x23;
            do
            {
            a = game[x + 132];
            if (a != 0)
            {
            // DEC $0780
            }
            x--;
            }
            while (!negative);
        }
    zp_09++;
    L_811B: ;
    x = 0;
    a = ram_07A7;
    a &= 2;
    vram_ptr_lo = a;
    a = buf_07A8[0];
    a &= 2;
    // EOR $00
    carry = false;
    if (!zero) { carry = true; }
    for (y = 7; y != 0; y--)
    {
        // ROR $07A7
        x++;
    }
    a = game[0x26];
    if (a != 0)
    {
        while (sprite0_hit()) ;
        a = game[0x7A];
        carry = (a & 1) != 0; a >>= 1;
        if (!carry)
        {
        ClearOam_8223();
        func_00_81C6();
        }
        while (!sprite0_hit()) ;
        for (y = 0x14; y != 0; y--) ;
    }
    a = scroll;
    ppu_scroll(a, a);
    a = game[0x44];
    a = ppu_ctrl_shadow;
    pha();
    ppu_ctrl((PPUCTRL)a);
    a = game[0x7A];
    if ((a & 1) == 0) { func_00_8212(); }
    a = ppu_status();
    a = pla();
    a |= 0x80;
    ppu_ctrl((PPUCTRL)a);
    return; // RTI
}

// bank=1 $FFF0
void irq()
{
    // ORA $1A19
    // ???(1A)
    // ???(1C)
    a = (byte)(a | peek((ushort)(0x1E1D + x)));
    // ASL $821F
    // ???(80)
    // BRK
}

// bank=0 $8182
void func_00_8182()
{
    a = game[0x74];
    if (a != 2)
    {
        if (a != 1) return;
        a = game[0x76];
        if (a != 3) return;
    }
    a = game[0x7B];
    if (a != 0)
    {
        game[0x7B]--;
        return;
    }
    a = game_00;
    a &= (byte)PADMSB.START;
    if (a == 0)
    {
        a = game[0x7A];
        a &= 0x7F;
    }
    else
    {
        a = game[0x7A];
        a &= 0x80;
        if (a != 0) return;
        game[0x7B] = 0x2B;
        a = game[0x7A];
        y = (byte)(a + 1);
        zp_FA = y;
        a ^= 1;
        a |= 0x80;
    }
    game[0x7A] = a;
}

// bank=0 $81C6
void func_00_81C6()
{
    y = game[0x52];
    vram_ptr_lo = 0x28;
    x = 0x0E;
    L_81CF: ;
    a = buf_06BE[x + 38];
    if (a >= vram_ptr_lo)
    {
        y = f_81C6_06E0;
        carry = false;
        // ADC $06E1
        if (carry)
        {
        carry = false;
        a = (byte)(a + vram_ptr_lo + (carry ? 1 : 0));
        }
        buf_06BE[x + 38] = a;
    }
    x--;
    if (x < 0x80) goto L_81CF;
    x = f_81C6_06E0;
    x++;
    if (x == 3) { x = 0; }
    f_81C6_06E0 = 0;
    x = 8;
    y = 2;
    do
    {
        buf_06BE[x + 51] = buf_06BE[y + 43];
        a += 8;
        buf_06BE[x + 52] = a;
        a += 8;
        buf_06BE[x + 53] = a;
        x--;
        x--;
        x--;
        y--;
    }
    while (!negative);
    return;
}

// bank=0 $8212
void func_00_8212()
{
    a = game[0x74];
    func_00_8E04();
    // AND $82
    // ???(DC)
    x = peek((ushort)0x838B);
    carry = false;
    // ???(92)
}

// bank=0 $8220
void ClearOam_8220()
{
    y = 0;
    a = 0xF8;
    do
    {
        oam_y[y] = a;
        y++; y++; y++; y++;
    }
    while (y != 0);
    return;
}

// bank=0 $8223
void ClearOam_8223()
{
    y = 4;
    a = 0xF8;
    do
    {
        oam_y[y] = a;
        y++; y++; y++; y++;
    }
    while (!zero);
    return;
}

// bank=0 $8E04
void func_00_8E04()
{
    a *= 2;
    y = a;
    a = pla();
    ptr_04_lo = a;
    a = pla();
    ptr_04_hi = a;
    y++;
    a = peek((ushort)((ptr_04_lo | ptr_04_hi << 8) + y));
    ptr_06_lo = a;
    y++;
    a = peek((ushort)((ptr_04_lo | ptr_04_hi << 8) + y));
    ptr_06_hi = a;
    // JMP $0006
}

// bank=0 $8E19
void func_00_8E19()
{
    a = ppu_status();
    a = ppu_ctrl_shadow;
    a |= (byte)PPUCTRL.BG_TABLE_1000;
    a &= (byte)(PPUCTRL.BG_TABLE_1000 | PPUCTRL.SPR_SIZE_8X16 | PPUCTRL.MASTER_SLAVE | PPUCTRL.NMI);
    func_00_8EED();
    a = 0x24;
    func_00_8E2D();
    a = 0x20;
}

// bank=0 $8E2D
void func_00_8E2D()
{
    vram_adr((ushort)(a << 8));
    vram_fill(0x24, 960);
    a = x;
    vram_buf[0] = 4;
    vram_buf[0x01] = 4;
    vram_fill(4, 0x40);
    scroll = 4;
    game[0x44] = 4;
    func_00_8EE6(); return;
}

// bank=0 $8E5C
void func_00_8E5C()
{
    func_00_8E6A();
    x++;
}

// bank=0 $8E6A
void func_00_8E6A()
{
    a = (byte)pad_poll_msb(x);
    game[x] = a;
    pha();
    a &= (byte)(PADMSB.START | PADMSB.SELECT);
    a = (byte)(a & game[x + 78]);
    if (a != 0)
    {
        a = pla();
        a &= (byte)~(PADMSB.START | PADMSB.SELECT);
        game[x] = a;
        return;
    }
    a = pla();
    game[x + 78] = a;
    return;
}

// bank=0 $8EDD
void func_00_8EDD()
{
    L_8E92: ;
    poke(PPU_ADDR, a);
    y++;
    a = PeekVramBufY();
    poke(PPU_ADDR, a);
    y++;
    a = PeekVramBufY();
    a *= 2;
    pha();
    a = ppu_ctrl_shadow;
    a |= (byte)PPUCTRL.VRAM_INC_32;
    if (!carry) { a &= 0xFB; }
    func_00_8EED();
    a = pla();
    a *= 2;
    if (carry)
    {
        a |= 2;
        y++;
    }
    a >>= 2;
    x = a;
    L_8EB6: ;
    if (!carry) { y++; }
    a = PeekVramBufY();
    vram_put(a);
    x--;
    if (x != 0) goto L_8EB6;
    carry = true;
    a = y;
    a = (byte)(a + vram_ptr_lo + (carry ? 1 : 0));
    vram_ptr_lo = a;
    a = 0;
    a = (byte)(a + vram_ptr_hi + (carry ? 1 : 0));
    vram_ptr_hi = a;
    vram_adr(0x3F00);
    vram_adr(0x0000);
    x = ppu_status();
    y = 0;
    a = PeekVramBufY();
    if (a != 0) goto L_8E92;
}

// bank=0 $8EE6
void func_00_8EE6()
{
    ppu_scroll(a, a);
    return;
}

// bank=0 $8EED
void func_00_8EED()
{
    ppu_ctrl((PPUCTRL)a);
    ppu_ctrl_shadow = a;
    return;
}

// bank=0 $8F97
void func_00_8F97()
{
    x = 5;
    func_00_8F9E();
    x = 0x0B;
}

// bank=0 $8F9E
void func_00_8F9E()
{
    y = 5;
    carry = true;
    do
    {
        a = score[x + 6];
        // SBC $07D7
        x--;
        y--;
    }
    while (!negative);
    if (!carry) return;
    x++;
    y++;
    do
    {
        score[y] = score[x + 6];
        x++;
        y++;
    }
    while (y < 6);
}

// bank=0 $90CC
void func_00_90CC()
{
    x = 7;
    ptr_06_lo = 0;
    L_90D2: ;
    ptr_06_hi = 7;
    L_90D4: ;
    if (x == 1)
    {
        if (y < 0x60)
        {
            }
            poke((ushort)((ptr_06_lo | ptr_06_hi << 8) + y), a);
        }
    y--;
    if (y != 0xFF) goto L_90D4;
    x--;
    if (x < 0x80) goto L_90D2;
    return;
}

// bank=1 $F2D0
void func_F2D0()
{
    a = game[0x74];
    if (a == 0)
    {
        apu_status_write((APUSTATUS)a);
        return;
    }
    apu_frame(APUFRAME.IRQ_INHIBIT | APUFRAME.MODE_5STEP | (APUFRAME)0x3F);
    apu_status_write(APUSTATUS.PULSE1 | APUSTATUS.PULSE2 | APUSTATUS.TRIANGLE | APUSTATUS.NOISE);
    a = f_F2D0_07C6;
    if (a == 0)
    {
        a = zp_FA;
        if (a != 1) goto L_F34B;
    }
    a = f_F2D0_07B2;
    if (a != 0) goto L_F316;
    a = zp_FA;
    if (a == 0) goto L_F35D;
    f_F2D0_07B2 = a;
    f_F2D0_07C6 = a;
    apu_status_write(0);
    zp_F1 = 0;
    zp_F2 = 0;
    zp_F3 = 0;
    apu_status_write(APUSTATUS.PULSE1 | APUSTATUS.PULSE2 | APUSTATUS.TRIANGLE | APUSTATUS.NOISE);
    ram_07BB = 0x2A;
    L_F312: ;
    a = 0x44;
    if (a != 0) goto L_F327;
    L_F316: ;
    a = ram_07BB;
    if (a != 0x24)
    {
        if (a == 0x1E) goto L_F312;
        if (a != 0x18) goto L_F32E;
    }
    a = 0x64;
    L_F327: ;
    x = 0x84;
    y = 0x7F;
    func_F388();
    L_F32E: ;
    ram_07BB--;
    if (ram_07BB != 0) goto L_F35D;
    apu_status_write(0);
    a = f_F2D0_07B2;
    if (a == 2) { f_F2D0_07C6 = 0; }
    f_F2D0_07B2 = 0;
    if (zero) goto L_F35D;
    L_F34B: ;
    func_F41B();
    func_F57C();
    func_F667();
    func_F694();
    zp_FB = 0;
    zp_FC = 0;
    L_F35D: ;
    zp_FF = 0;
    zp_FE = 0;
    zp_FD = 0;
    zp_FA = 0;
    y = ram_07C0;
    a = zp_F4;
    a &= 3;
    if (a != 0)
    {
        ram_07C0++;
        // if (y < 0x30) goto L_F37D // label not in this routine
    }
    a = y;
    if (a != 0) { ram_07C0 = (byte)(ram_07C0 - 1); }
    apu_dmc_raw(y);
    return;
}

// bank=1 $F381
void func_F381()
{
    apu_pulse1_sweep(y);
    apu_pulse1_ctrl(x);
    return;
}

// bank=1 $F388
void func_F388()
{
    func_F381();
}

// bank=1 $F38B
void func_F38B()
{
    x = 0;
    y = a;
    a = rom_FF00[y + 1];
    if (a == 0) return;
    poke((ushort)(APU_PULSE1_TIMER_LO + x), a);
    a = rom_FF00[y];
    a |= 8;
    poke((ushort)(APU_PULSE1_TIMER_HI + x), a);
}

// bank=1 $F39F
void func_F39F()
{
    apu_pulse2_ctrl(x);
    apu_pulse2_sweep(y);
    return;
}

// bank=1 $F3A6
void func_F3A6()
{
    func_F39F();
}

// bank=1 $F3A9
void func_F3A9()
{
    L_F38D: ;
    y = a;
    a = rom_FF00[y + 1];
    if (a != 0)
    {
        poke((ushort)(APU_PULSE1_TIMER_LO + x), a);
        a = rom_FF00[y];
        a |= 8;
        poke((ushort)(APU_PULSE1_TIMER_HI + x), a);
    }
    return;
    x = 4;
    if (x != 0) goto L_F38D;
}

// bank=1 $F3AD
void func_F3AD()
{
    L_F34A: ;
    // ???(12)
    func_F41B();
    func_F57C();
    func_F667();
    func_F694();
    zp_FB = 0;
    zp_FC = 0;
    zp_FF = 0;
    zp_FE = 0;
    zp_FD = 0;
    zp_FA = 0;
    y = ram_07C0;
    a = zp_F4;
    a &= 3;
    if (a != 0)
    {
        ram_07C0++;
        // if (y < 0x30) goto L_F37D // label not in this routine
    }
    a = y;
    if (a != 0) { ram_07C0 = (byte)(ram_07C0 - 1); }
    apu_dmc_raw(y);
    return;
    L_F38D: ;
    y = a;
    a = rom_FF00[y + 1];
    if (a != 0)
    {
        poke((ushort)(APU_PULSE1_TIMER_LO + x), a);
        a = rom_FF00[y];
        a |= 8;
        poke((ushort)(APU_PULSE1_TIMER_HI + x), a);
    }
    return;
    x = 8;
    if (x != 0) goto L_F38D;
    // ???(9F)
    // ???(9B)
    a = y;
    // STX $95
    // STY $92
    if (!carry) goto L_F34A;
    txs();
    // ???(97)
    obj_0087[x + 12] = a;
    // ???(92)
    ram_07BB = 0x40;
    a = 0x62;
    func_F38B();
    x = 0x99;
    if (x == 0)
    {
        a = 0x26;
        if (a == 0) { a = 0x18; }
        x = 0x82;
        y = 0xA7;
        func_F388();
        ram_07BB = 0x28;
        a = ram_07BB;
        if (a == 0x25)
        {
        x = 0x5F;
        y = 0xF6;
        if (y == 0)
        {
            }
            // if (a != 0x20) goto L_F419 // label not in this routine
            x = 0x48;
            }
            y = 0xBC;
        }
    func_F381();
    // if (!zero) goto L_F419 // label not in this routine
    a = 5;
    y = 0x99;
    if (y == 0)
    {
        a = 0x0A;
        y = 0x93;
    }
    x = 0x9E;
    ram_07BB = 0x0A;
    a = 0x0C;
    func_F388();
    a = ram_07BB;
    if (a == 6) { apu_pulse1_sweep(0xBB); }
    if (!zero) { func_F4A2(); return; }
    a = 0x0E;
    y = 0xCB;
    x = 0x9F;
    ram_07BB = 0x0E;
    a = 0x28;
    func_F388();
    if (!zero) { func_F4A2(); return; }
    y = ram_07BB;
    if (y == 8)
    {
        apu_pulse1_timer_lo(0xA0);
        a = 0x9F;
        if (a == 0)
        {
            }
            a = 0x90;
        }
    apu_pulse1_ctrl(0x90);
}

// bank=1 $F41B
void func_F41B()
{
    L_F3BF: ;
    ram_07BB = 0x40;
    a = 0x62;
    func_F38B();
    x = 0x99;
    if (x != 0) goto L_F3F2;
    L_F3CD: ;
    a = 0x26;
    if (a != 0) goto L_F3D3;
    L_F3D1: ;
    a = 0x18;
    L_F3D3: ;
    x = 0x82;
    y = 0xA7;
    func_F388();
    ram_07BB = 0x28;
    L_F3DF: ;
    a = ram_07BB;
    if (a == 0x25)
    {
        x = 0x5F;
        y = 0xF6;
        if (y != 0) goto L_F3F4;
    }
    // if (a != 0x20) goto L_F419 // label not in this routine
    x = 0x48;
    L_F3F2: ;
    y = 0xBC;
    L_F3F4: ;
    func_F381();
    // if (!zero) goto L_F419 // label not in this routine
    L_F3F9: ;
    a = 5;
    y = 0x99;
    if (y != 0) goto L_F403;
    L_F3FF: ;
    a = 0x0A;
    y = 0x93;
    L_F403: ;
    x = 0x9E;
    ram_07BB = 0x0A;
    a = 0x0C;
    func_F388();
    L_F40D: ;
    a = ram_07BB;
    if (a == 6) { apu_pulse1_sweep(0xBB); }
    // if (!zero) goto L_F47B // label not in this routine
    y = zp_FF;
    if (y != 0)
    {
        zp_F1 = y;
        if (negative) goto L_F3CD;
        { byte _m = zp_FF; carry = (_m & 1) != 0; zp_FF = (byte)(_m >> 1); }
        if (carry) goto L_F3D1;
        { byte _m = zp_FF; carry = (_m & 1) != 0; zp_FF = (byte)(_m >> 1); }
        if (carry) goto L_F3FF;
        { byte _m = zp_FF; carry = (_m & 1) != 0; zp_FF = (byte)(_m >> 1); }
        if (carry) goto L_F45B;
        { byte _m = zp_FF; carry = (_m & 1) != 0; zp_FF = (byte)(_m >> 1); }
        if (carry) goto L_F47D;
        { byte _m = zp_FF; carry = (_m & 1) != 0; zp_FF = (byte)(_m >> 1); }
        if (carry) goto L_F4B6;
        { byte _m = zp_FF; carry = (_m & 1) != 0; zp_FF = (byte)(_m >> 1); }
        if (carry) goto L_F3F9;
        { byte _m = zp_FF; carry = (_m & 1) != 0; zp_FF = (byte)(_m >> 1); }
        if (carry) goto L_F3BF;
    }
    a = zp_F1;
    if (a != 0)
    {
        if (negative) goto L_F3DF;
        carry = (a & 1) != 0; a >>= 1;
        if (carry) goto L_F3DF;
        carry = (a & 1) != 0; a >>= 1;
        if (carry) goto L_F40D;
        carry = (a & 1) != 0; a >>= 1;
        if (carry) goto L_F469;
        carry = (a & 1) != 0; a >>= 1;
        if (carry) goto L_F48D;
        carry = (a & 1) != 0; a >>= 1;
        if (carry) goto L_F4BB;
        carry = (a & 1) != 0; a >>= 1;
        if (carry) goto L_F40D;
        carry = (a & 1) != 0; a >>= 1;
        if (carry) { func_F4A2(); return; }
    }
    return;
    L_F45B: ;
    ram_07BB = 0x0E;
    y = 0x9C;
    x = 0x9E;
    a = 0x26;
    func_F388();
    L_F469: ;
    y = ram_07BB;
    a = peek((ushort)(0xF3B0 + y));
    apu_pulse1_ctrl(a);
    if (y == 6) { apu_pulse1_timer_lo(0x9E); }
    if (!zero) { func_F4A2(); return; }
    L_F47D: ;
    a = 0x0E;
    y = 0xCB;
    x = 0x9F;
    ram_07BB = 0x0E;
    a = 0x28;
    func_F388();
    if (!zero) { func_F4A2(); return; }
    L_F48D: ;
    y = ram_07BB;
    if (y == 8)
    {
        apu_pulse1_timer_lo(0xA0);
        a = 0x9F;
        if (a == 0)
        {
            }
            a = 0x90;
        }
    apu_pulse1_ctrl(0x90);
    L_F4B6: ;
    ram_07BB = 0x2F;
    L_F4BB: ;
    a = ram_07BB;
    carry = (a & 1) != 0; a >>= 1;
    // if (carry) goto L_F4D1 // label not in this routine
    carry = (a & 1) != 0; a >>= 1;
    // if (carry) goto L_F4D1 // label not in this routine
    a &= 2;
    if (a != 0)
    {
        y = 0x91;
        x = 0x9A;
        a = 0x44;
        func_F388();
    }
    func_F4A2(); return;
}

// bank=1 $F4A7
void func_F4A7()
{
    zp_F1 = 0;
    apu_status_write(APUSTATUS.PULSE2 | APUSTATUS.TRIANGLE | APUSTATUS.NOISE);
    apu_status_write(APUSTATUS.PULSE1 | APUSTATUS.PULSE2 | APUSTATUS.TRIANGLE | APUSTATUS.NOISE);
    return;
}

// bank=1 $F571
void func_F571()
{
    apu_status_write(APUSTATUS.PULSE1 | APUSTATUS.TRIANGLE | APUSTATUS.NOISE);
    apu_status_write(APUSTATUS.PULSE1 | APUSTATUS.PULSE2 | APUSTATUS.TRIANGLE | APUSTATUS.NOISE);
    return;
}

// bank=1 $F57C
void func_F57C()
{
    L_F518: ;
    a = 0x35;
    x = 0x8D;
    if (x != 0) goto L_F522;
    L_F51E: ;
    a = 6;
    x = 0x98;
    L_F522: ;
    ram_07BD = 6;
    y = 0x7F;
    a = 0x42;
    func_F3A6();
    L_F53A: ;
    ram_07BD = 0x20;
    y = 0x94;
    a = 0x5E;
    if (a != 0) goto L_F550;
    L_F545: ;
    a = ram_07BD;
    if (a != 0x18) { func_F568(); return; }
    y = 0x93;
    a = 0x18;
    L_F550: ;
    if (!zero) goto L_F5D1;
    L_F552: ;
    ram_07BD = 0x36;
    L_F557: ;
    a = ram_07BD;
    carry = (a & 1) != 0; a >>= 1;
    if (carry) { func_F568(); return; }
    y = a;
    a = peek((ushort)(0xF4D9 + y));
    x = 0x5D;
    y = 0x7F;
    L_F565: ;
    func_F3A6();
    a = zp_F2;
    a &= 0x40;
    if (a != 0) goto L_F5E7;
    y = zp_FE;
    if (y != 0)
    {
        zp_F2 = y;
        if (negative) goto L_F5C8;
        { byte _m = zp_FE; carry = (_m & 1) != 0; zp_FE = (byte)(_m >> 1); }
        if (carry) goto L_F518;
        { byte _m = zp_FE; carry = (_m & 1) != 0; zp_FE = (byte)(_m >> 1); }
        if (carry) goto L_F5FC;
        { byte _m = zp_FE; carry = (_m & 1) != 0; zp_FE = (byte)(_m >> 1); }
        if (carry) goto L_F600;
        { byte _m = zp_FE; carry = (_m & 1) != 0; zp_FE = (byte)(_m >> 1); }
        if (carry) goto L_F53A;
        { byte _m = zp_FE; carry = (_m & 1) != 0; zp_FE = (byte)(_m >> 1); }
        if (carry) goto L_F51E;
        { byte _m = zp_FE; carry = (_m & 1) != 0; zp_FE = (byte)(_m >> 1); }
        if (carry) goto L_F552;
        { byte _m = zp_FE; carry = (_m & 1) != 0; zp_FE = (byte)(_m >> 1); }
        if (carry) goto L_F5E2;
    }
    a = zp_F2;
    if (a != 0)
    {
        if (negative) goto L_F5D3;
        carry = (a & 1) != 0; a >>= 1;
        if (carry) goto L_F5C2;
        carry = (a & 1) != 0; a >>= 1;
        if (carry) goto L_F60F;
        carry = (a & 1) != 0; a >>= 1;
        if (carry) goto L_F60F;
        carry = (a & 1) != 0; a >>= 1;
        if (carry) goto L_F545;
        carry = (a & 1) != 0; a >>= 1;
        if (carry) goto L_F5C2;
        carry = (a & 1) != 0; a >>= 1;
        if (carry) goto L_F557;
        carry = (a & 1) != 0; a >>= 1;
        if (carry) goto L_F5E7;
    }
    return;
    L_F5C2: ;
    func_F52C(); return;
    L_F5C5: ;
    func_F568(); return;
    L_F5C8: ;
    ram_07BD = 0x38;
    y = 0xC4;
    a = 0x18;
    L_F5D1: ;
    if (!zero) goto L_F5DE;
    L_F5D3: ;
    a = ram_07BD;
    if (a != 8) { func_F568(); return; }
    y = 0xA4;
    a = 0x5A;
    L_F5DE: ;
    x = 0x9F;
    L_F5E0: ;
    if (!zero) goto L_F565;
    L_F5E2: ;
    ram_07BD = 0x30;
    L_F5E7: ;
    a = ram_07BD;
    x = 3;
    L_F5EC: ;
    carry = (a & 1) != 0; a >>= 1;
    if (carry) goto L_F5C5;
    x--;
    if (x != 0) goto L_F5EC;
    y = a;
    a = peek((ushort)(0xF4D3 + y));
    x = 0x82;
    y = 0x7F;
    if (y != 0) goto L_F5E0;
    L_F5FC: ;
    a = 0x10;
    if (a != 0) goto L_F602;
    L_F600: ;
    a = 0x20;
    L_F602: ;
    ram_07BD = 0x20;
    apu_pulse2_sweep(0x7F);
    ram_07BE = 0;
    L_F60F: ;
    ram_07BE++;
    a = ram_07BE;
    carry = (a & 1) != 0; a >>= 1;
    y = a;
    // CPY $07BD
    if (!zero)
    {
        apu_pulse2_ctrl(0x9D);
        a = peek((ushort)(0xF4F8 + y));
        func_F3A9();
        return;
    }
    func_F56D(); return;
}

// bank=1 $F667
void func_F667()
{
    L_F63B: ;
    f_F667_07BF = 0x20;
    L_F640: ;
    a = f_F667_07BF;
    carry = (a & 1) != 0; a >>= 1;
    if (!carry) goto L_F658;
    y = a;
    // LDX $F62B
    a = peek((ushort)(0xFFEA + y));
    L_F64D: ;
    apu_noise_ctrl(a);
    apu_noise_period(x);
    apu_noise_length(0x18);
    L_F658: ;
    f_F667_07BF--;
    if (f_F667_07BF == 0)
    {
        apu_noise_ctrl(0xF0);
        zp_F3 = 0;
    }
    return;
    y = zp_FD;
    if (y != 0)
    {
        zp_F3 = y;
        { byte _m = zp_FD; carry = (_m & 1) != 0; zp_FD = (byte)(_m >> 1); }
        if (carry) goto L_F63B;
        { byte _m = zp_FD; carry = (_m & 1) != 0; zp_FD = (byte)(_m >> 1); }
        if (!(carry))
        {
            }
            a = zp_F3;
            if (a != 0)
            {
            carry = (a & 1) != 0; a >>= 1;
            if (carry) goto L_F640;
            carry = (a & 1) != 0; a >>= 1;
            if (!(carry))
            {
                }
                return;
                }
                f_F667_07BF = 0x40;
            }
    a = f_F667_07BF;
    carry = (a & 1) != 0; a >>= 1;
    y = a;
    x = 0x0F;
    a = peek((ushort)(0xFFC9 + y));
    if (a != 0) goto L_F64D;
    func_F73A(); return;
}

// bank=1 $F694
void func_F694()
{
    L_F691: ;
    func_F73A(); return;
    a = zp_FC;
    if (a != 0) { func_F6A4(); return; }
    a = zp_FB;
    if (a == 0)
    {
        a = ram_07B1;
        a = (byte)(a | zp_F4);
        if (a != 0) goto L_F691;
        return;
    }
    if (a == 4) { func_F4A7(); }
    ram_07C7 = 0x10;
}

// bank=1 $F8C5
void func_F8C5()
{
    x = a;
    { bool _c = carry; carry = (a & 1) != 0; a = (byte)((a >> 1) | (_c ? 0x80 : 0)); }
    a = x;
    { bool _c = carry; carry = (a & 0x80) != 0; a = (byte)((a << 1) | (_c ? 1 : 0)); }
    { bool _c = carry; carry = (a & 0x80) != 0; a = (byte)((a << 1) | (_c ? 1 : 0)); }
    { bool _c = carry; carry = (a & 0x80) != 0; a = (byte)((a << 1) | (_c ? 1 : 0)); }
}

// bank=1 $F8CB
void func_F8CB()
{
    a &= 7;
    carry = false;
    a = (byte)(a + zp_F0 + (carry ? 1 : 0));
    a = (byte)(a + ram_07C4 + (carry ? 1 : 0));
    y = a;
    a = peek((ushort)(0xFF66 + y));
    return;
}

// bank=1 $F8D8
void func_F8D8()
{
    a = ram_07B1;
    a &= 8;
    if (a != 0)
    {
        a = 4;
        if (a != 0) goto L_F8EF;
    }
    a = zp_F4;
    a &= 0x7D;
    if (a != 0)
    {
        a = 8;
        if (a != 0) goto L_F8EF;
    }
    a = 0x28;
    L_F8EF: ;
    x = 0x82;
    y = 0x7F;
    return;
}

// bank=1 $F8F4
void func_F8F4()
{
    a = ram_07B1;
    a &= 8;
    if (a != 0)
    {
        a = peek((ushort)(0xFF96 + y));
        return;
    }
    a = zp_F4;
    a &= 0x7D;
    if (a != 0)
    {
        a = peek((ushort)(0xFF9A + y));
        return;
    }
    a = peek((ushort)(0xFFA2 + y));
    return;
}

// bank=1 $F4A2
void func_F4A2()
{
    ram_07BB--;
    if (ram_07BB != 0) return;
}

// bank=1 $F52C
void func_F52C()
{
    a = ram_07BD;
    if (a == 0x30) { apu_pulse2_timer_lo(0x54); }
    if (!zero) { func_F568(); return; }
    ram_07BD = 0x20;
    y = 0x94;
    a = 0x5E;
    if (a == 0)
    {
        a = ram_07BD;
        if (a != 0x18) { func_F568(); return; }
        y = 0x93;
        a = 0x18;
    }
    if (!zero) goto L_F5D1;
    ram_07BD = 0x36;
    a = ram_07BD;
    carry = (a & 1) != 0; a >>= 1;
    if (carry) { func_F568(); return; }
    y = a;
    a = peek((ushort)(0xF4D9 + y));
    x = 0x5D;
    y = 0x7F;
    L_F565: ;
    func_F3A6();
    L_F5C5: ;
    func_F568(); return;
    L_F5D1: ;
    if (zero)
    {
        a = ram_07BD;
        if (a != 8) { func_F568(); return; }
        y = 0xA4;
        a = 0x5A;
    }
    x = 0x9F;
    L_F5E0: ;
    if (!zero) goto L_F565;
    ram_07BD = 0x30;
    a = ram_07BD;
    x = 3;
    L_F5EC: ;
    carry = (a & 1) != 0; a >>= 1;
    if (carry) goto L_F5C5;
    x--;
    if (x != 0) goto L_F5EC;
    y = a;
    a = peek((ushort)(0xF4D3 + y));
    x = 0x82;
    y = 0x7F;
    if (y != 0) goto L_F5E0;
    a = 0x10;
    if (a == 0) { a = 0x20; }
    ram_07BD = 0x20;
    apu_pulse2_sweep(0x7F);
    ram_07BE = 0;
    ram_07BE++;
    a = ram_07BE;
    carry = (a & 1) != 0; a >>= 1;
    y = a;
    // CPY $07BD
    if (!zero)
    {
        apu_pulse2_ctrl(0x9D);
        a = peek((ushort)(0xF4F8 + y));
        func_F3A9();
        return;
    }
    func_F56D(); return;
}

// bank=1 $F568
void func_F568()
{
    ram_07BD--;
    if (ram_07BD != 0) return;
}

// bank=1 $F56D
void func_F56D()
{
    zp_F2 = 0;
}

// bank=1 $F6A4
void func_F6A4()
{
    ram_07B1 = a;
    if (a == 1)
    {
        func_F4A7();
        func_F571();
    }
    x = zp_F4;
    ram_07C5 = x;
    ram_07C4 = 0;
    zp_F4 = 0;
    if (a == 0x40)
    {
        ram_07C4 = 8;
        // if (!zero) goto L_F6F1 // label not in this routine
        if (a == 4) { func_F4A7(); }
        ram_07C7 = 0x10;
    }
    do
    {
        y++;
        carry = (a & 1) != 0; a >>= 1;
    }
    while (!carry);
    a = peek((ushort)(0xF90C + y));
    y = a;
    a = peek((ushort)(0xF90D + y));
    zp_F0 = a;
    a = peek((ushort)(0xF90E + y));
    ptr_F5_lo = a;
    a = peek((ushort)(0xF90F + y));
    ptr_F5_hi = a;
    a = peek((ushort)(0xF910 + y));
    zp_F9 = a;
    a = peek((ushort)(0xF911 + y));
    zp_F8 = a;
    a = peek((ushort)(0xF912 + y));
    ram_07B0 = a;
    ram_07C1 = a;
    ram_07B4 = 1;
    ram_07B6 = 1;
    ram_07B9 = 1;
    ram_07BA = 1;
    zp_F7 = 0;
    ram_07CA = 0;
    apu_status_write(APUSTATUS.PULSE1 | APUSTATUS.PULSE2 | APUSTATUS.NOISE);
    apu_status_write(APUSTATUS.PULSE1 | APUSTATUS.PULSE2 | APUSTATUS.TRIANGLE | APUSTATUS.NOISE);
}

// bank=1 $F6D4
void func_F6D4()
{
    L_F6D1: ;
    ram_07C7 = y;
    ram_07B1 = 0;
    zp_F4 = a;
    if (a == 1)
    {
        ram_07C7++;
        y = ram_07C7;
        if (y == 0x32)
        {
            y = 0x11;
            if (y != 0) goto L_F6D1;
            }
            zp_F7 = 8;
            do
            {
            y++;
            carry = (a & 1) != 0; a >>= 1;
            }
            while (!carry);
        }
    a = peek((ushort)(0xF90C + y));
    y = a;
    a = peek((ushort)(0xF90D + y));
    zp_F0 = a;
    a = peek((ushort)(0xF90E + y));
    ptr_F5_lo = a;
    a = peek((ushort)(0xF90F + y));
    ptr_F5_hi = a;
    a = peek((ushort)(0xF910 + y));
    zp_F9 = a;
    a = peek((ushort)(0xF911 + y));
    zp_F8 = a;
    a = peek((ushort)(0xF912 + y));
    ram_07B0 = a;
    ram_07C1 = a;
    ram_07B4 = 1;
    ram_07B6 = 1;
    ram_07B9 = 1;
    ram_07BA = 1;
    zp_F7 = 0;
    ram_07CA = 0;
    apu_status_write(APUSTATUS.PULSE1 | APUSTATUS.PULSE2 | APUSTATUS.NOISE);
    apu_status_write(APUSTATUS.PULSE1 | APUSTATUS.PULSE2 | APUSTATUS.TRIANGLE | APUSTATUS.NOISE);
}

// bank=1 $F73A
void func_F73A()
{
    ram_07B4--;
    if (ram_07B4 != 0) goto L_F79E;
    y = zp_F7;
    zp_F7++;
    a = peek((ushort)((ptr_F5_lo | ptr_F5_hi << 8) + y));
    if (a != 0)
    {
        if (!negative) goto L_F786;
        if (!zero) goto L_F77A;
    }
    a = ram_07B1;
    if (a == 0x40)
    {
        a = ram_07C5;
        // if (a != 0) goto L_F774 // label not in this routine
    }
    a &= 4;
    if (a == 0)
    {
        a = zp_F4;
        a &= 0x5F;
        if (a == 0)
        {
        zp_F4 = 0;
        ram_07B1 = 0;
        apu_triangle_ctrl(0);
        apu_pulse1_ctrl(0x90);
        apu_pulse2_ctrl(0x90);
        return;
        }
        func_F6D4(); return;
    }
    func_F6A4(); return;
    L_F77A: ;
    func_F8CB();
    f_F73A_07B3 = a;
    y = zp_F7;
    zp_F7++;
    a = peek((ushort)((ptr_F5_lo | ptr_F5_hi << 8) + y));
    L_F786: ;
    x = zp_F2;
    if (x == 0)
    {
        func_F3A9();
        if (!zero) { func_F8D8(); }
        f_F73A_07B5 = a;
        func_F39F();
    }
    ram_07B4 = f_F73A_07B3;
    L_F79E: ;
    a = zp_F2;
    // if (a != 0) goto L_F7BC // label not in this routine
    a = ram_07B1;
    a &= 0x91;
    if (a == 0)
    {
        y = f_F73A_07B5;
        if (y != 0) { f_F73A_07B5 = (byte)(f_F73A_07B5 - 1); }
        func_F8F4();
        apu_pulse2_ctrl(a);
        apu_pulse2_sweep(0x7F);
    }
    y = zp_F8;
    if (y == 0) goto L_F81A;
    ram_07B6--;
    if (ram_07B6 != 0) goto L_F7F7;
    L_F7C5: ;
    y = zp_F8;
    zp_F8++;
    a = peek((ushort)((ptr_F5_lo | ptr_F5_hi << 8) + y));
    if (a == 0)
    {
        apu_pulse1_ctrl(0x83);
        apu_pulse1_sweep(0x94);
        ram_07CA = 0x94;
        if (!zero) goto L_F7C5;
    }
    func_F8C5();
    ram_07B6 = a;
    y = zp_F1;
    if (y != 0) goto L_F81A;
    a = x;
    a &= 0x3E;
    func_F38B();
    if (!zero) { func_F8D8(); }
    f_F73A_07B7 = a;
    func_F381();
    L_F7F7: ;
    a = zp_F1;
    if (a != 0) goto L_F81A;
    a = ram_07B1;
    a &= 0x91;
    if (a == 0)
    {
        y = f_F73A_07B7;
        if (y != 0) { f_F73A_07B7 = (byte)(f_F73A_07B7 - 1); }
        func_F8F4();
        apu_pulse1_ctrl(a);
    }
    a = ram_07CA;
    if (a == 0) { a = 0x7F; }
    apu_pulse1_sweep(0x7F);
    L_F81A: ;
    a = zp_F9;
    ram_07B9--;
    if (ram_07B9 != 0) goto L_F86D;
    y = zp_F9;
    zp_F9++;
    a = peek((ushort)((ptr_F5_lo | ptr_F5_hi << 8) + y));
    if (a == 0) goto L_F86A;
    if (negative)
    {
        func_F8CB();
        f_F73A_07B8 = a;
        apu_triangle_ctrl(0x1F);
        y = zp_F9;
        zp_F9++;
        a = peek((ushort)((ptr_F5_lo | ptr_F5_hi << 8) + y));
        if (a == 0) goto L_F86A;
    }
    func_F3AD();
    x = f_F73A_07B8;
    ram_07B9 = x;
    a = ram_07B1;
    a &= 0x6E;
    if (a == 0)
    {
        a = zp_F4;
        a &= 0x0A;
        if (a == 0) goto L_F86D;
    }
    a = x;
    if (a < 0x12)
    {
        a = ram_07B1;
        a &= 8;
        if (a != 0)
        {
        a = 0x0F;
        if (a != 0) goto L_F86A;
        }
        a = 0x1F;
        if (a != 0) goto L_F86A;
    }
    a = 0xFF;
    L_F86A: ;
    apu_triangle_ctrl(0xFF);
    L_F86D: ;
    a = zp_F4;
    a &= 0xF3;
    if (a == 0) return;
    ram_07BA--;
    if (ram_07BA != 0) return;
    L_F878: ;
    y = ram_07B0;
    ram_07B0++;
    a = peek((ushort)((ptr_F5_lo | ptr_F5_hi << 8) + y));
    if (a == 0)
    {
        ram_07B0 = ram_07C1;
        if (!zero) goto L_F878;
    }
    func_F8C5();
    ram_07BA = a;
    a = x;
    a &= 0x3E;
    if (a == 0) goto L_F8B9;
    if (a != 0x30)
    {
        if (a != 0x20)
        {
        a &= 0x10;
        if (a == 0) goto L_F8B9;
        a = 0x1C;
        x = 3;
        y = 0x18;
        if (y != 0) goto L_F8BB;
        }
        a = 0x1C;
        x = 0x0C;
        y = 0x18;
        if (y != 0) goto L_F8BB;
    }
    a = 0x1C;
    x = 3;
    y = 0x58;
    if (y != 0) goto L_F8BB;
    L_F8B9: ;
    a = 0x10;
    L_F8BB: ;
    apu_noise_ctrl(0x10);
    apu_noise_period(3);
    apu_noise_length(0x58);
}

// bank=0 $819D
void func_00_819D()
{
    a = game_00;
    a &= (byte)PADMSB.START;
    if (a == 0)
    {
        a = game[0x7A];
        a &= 0x7F;
    }
    else
    {
        a = game[0x7A];
        a &= 0x80;
        if (a != 0) return;
        game[0x7B] = 0x2B;
        a = game[0x7A];
        y = (byte)(a + 1);
        zp_FA = y;
        a ^= 1;
        a |= 0x80;
    }
    game[0x7A] = a;
}

// bank=0 $8247
void func_00_8247()
{
    a = game_00;
    a = (byte)(a | game[0x01]);
    if (a != 0x10)
    {
        if (a == 0x90)
        {
            }
            func_00_82D8(); return;
        }
    if (a != 0x20)
    {
        x = ram_07A2;
        if (x == 0)
        {
        f_8247_0780 = a;
        func_00_836B();
        if (carry) goto L_82C9;
        func_00_82C0(); return;
        }
        x = ram_07FC;
        if (x == 0) { func_00_82BB(); return; }
        if (a != 0x40) { func_00_82BB(); return; }
        y++;
    }
    a = ram_07A2;
    if (a == 0) goto L_82C9;
    ram_07A2 = 0x18;
    a = f_8247_0780;
    if (a != 0) { func_00_82BB(); return; }
    f_8247_0780 = 0x10;
    if (y != 1)
    {
        a = game[0x7E];
        a ^= 1;
        game[0x7E] = a;
        func_00_8325();
        func_00_82BB(); return;
    }
    x = f_8247_076B;
    x++;
    a = x;
    a &= 7;
    f_8247_076B = a;
    func_00_830E();
    do
    {
        a = peek((ushort)(0x823F + x));
        vram_buf[x] = a;
        x++;
        zero = x == 6; carry = x >= 6;
    }
    while (negative);
    y = game[0x63];
    y++;
    vram_buf[0x04] = y;
    L_82C9: ;
    game[0x74] = 0;
    game[0x76] = 0;
    game[0x26] = 0;
    game[0x78]++;
    return;
}

// bank=0 $8487
void func_00_8487()
{
    a = game_00;
    a = (byte)(a | game[0x01]);
    a &= (byte)PADMSB.B;
    if (a == 0) return;
    ram_07FC = 1;
    game_5E = 0xFF;
    func_00_9248();
}

// bank=0 $923C
void func_00_923C()
{
    a = game_00;
    a &= (byte)PADMSB.START;
    if (a != 0) { func_00_9248(); return; }
    a = ram_07A0;
    if (a != 0) return;
}

// bank=0 $AEED
void func_00_AEED()
{
    game_00 = game[x];
    func_00_B04A();
    a = game[0x76];
    if (a < 3) { return; }
    func_00_B624();
    for (x = 0; x != 6; x++)
    {
        zp_08 = 0;
        func_C047();
        func_00_84C3();
    }
    func_F180();
    func_F12A();
    func_EEE9();
    FlushVramBuf_BED4();
    zp_08 = 1;
    func_00_BE70();
    x--;
    zp_08 = x;
    func_00_BE70();
    func_00_BB96();
    func_00_B9BC();
    func_00_B7B8();
    func_00_B855();
    func_00_B74F();
    FlushVramBuf_89E1();
    a = obj_009F_16;
    zero = a == 2; carry = a >= 2; negative = (byte)(a - 2) >= 0x80;
    // if (!negative) goto L_AF52 // label not in this routine
    a = ram_079F;
    if (a != 0)
    {
        // if (a != 4) goto L_AF52 // label not in this routine
        a = game[0x83];
        if (a == 0) { func_00_90ED(); }
        y = ram_079F;
        a = zp_09;
        if (y < 8)
        {
        a >>= 2;
        }
        carry = (a & 1) != 0; a >>= 1;
        func_00_B288();
        func_00_AF67(); return;
    }
    func_00_B29A();
}

// bank=0 $B104
void func_00_B104()
{
    a = game_00;
    a &= (byte)(PADMSB.B | PADMSB.A);
    zp_0A = a;
}

// bank=0 $B10B
void func_00_B10B()
{
    a = game_00;
    a &= (byte)(PADMSB.RIGHT | PADMSB.LEFT);
    zp_0C = a;
}

// bank=0 $B112
void func_00_B112()
{
    a = game_00;
    a &= (byte)(PADMSB.DOWN | PADMSB.UP);
    zp_0B = a;
    a &= (byte)PADMSB.DOWN;
    // if (a == 0) goto L_B12B // label not in this routine
    a = zp_1D;
    // if (a != 0) goto L_B12B // label not in this routine
    y = zp_0C;
    if (y != 0)
    {
        zp_0C = 0;
        zp_0B = 0;
    }
    func_00_B329();
    y = 1;
    a = game[0x58];
    // if (a != 0) goto L_B13E // label not in this routine
    y = 0;
    a = game[0x18];
    if (a != 0) { y = 2; }
    buf_0491[0x08] = 2;
    a = 1;
    y = obj_0046[0x11];
    if (y != 0)
    {
        if (negative) { carry = (a & 0x80) != 0; a <<= 1; }
        zp_45 = a;
    }
    func_00_AF93();
    func_F180();
    func_F12A();
    x = 0;
    func_E29C();
    func_DC64();
    a = buf_00CE_00;
    // if (a < 0x40) goto L_B179 // label not in this routine
    a = zp_0E;
    // if (a == 5) goto L_B179 // label not in this routine
    // if (a == 7) goto L_B179 // label not in this routine
    if (a >= 4)
    {
        a = vram_buf[0xC4];
        a &= 0xDF;
        vram_buf[0xC4] = a;
    }
    a = obj_009F_16;
    zero = a == 2; carry = a >= 2; negative = (byte)(a - 2) >= 0x80;
    // if (negative) goto L_B1BA // label not in this routine
    game[0x27] = 1;
    ptr_06_hi = 4;
    x = 0;
    y = game[0x5D];
    if (y == 0)
    {
        y = game[0x47];
        if (y != 0) goto L_B1AA;
    }
    x++;
    y = zp_0E;
    if (y == 0x0B) goto L_B1AA;
    y = game[0x16];
    if (y == 0)
    {
        y++;
        zp_FC = y;
        game[0x16] = y;
    }
    ptr_06_hi = 6;
    L_B1AA: ;
    { byte _v = ptr_06_hi; zero = a == _v; carry = a >= _v; negative = (byte)(a - _v) >= 0x80; }
    // if (negative) goto L_B1BA // label not in this routine
    x--;
    if (x < 0x80)
    {
        y = ram_07B1;
        if (y == 0) { zp_0E = 6; }
        return;
    }
    game[0x5C] = 0;
    func_00_B1DD();
    game[0x56]++;
    return;
}

// bank=0 $B59E
void func_00_B59E()
{
    a = game_00;
    a &= (byte)~PADMSB.A;
    if (a == 0) { func_00_B5C5(); return; }
    a &= (byte)(PADMSB.RIGHT | PADMSB.LEFT);
    if (a == zp_45)
    {
        game[0x07] = 0;
        func_00_B5C5(); return;
    }
    a = game[0x04];
    if (a >= 0x0B) { func_00_B5C5(); return; }
    zp_45 = zp_33;
    obj_0046[0x11] = 0;
    game[0x09] = 0;
}

// bank=0 $830E
void func_00_830E()
{
    game[0x63] = a;
    game[0x6A] = a;
    game[0x64] = 0;
    game[0x6B] = 0;
    return;
}

// bank=0 $8325
void func_00_8325()
{
    y = 7;
    do
    {
        a = peek((ushort)(0x831D + y));
        vram_buf[y] = a;
        y--;
    }
    while (!negative);
    a = game[0x7E];
    if (a == 0) return;
    vram_buf[0x04] = 0x24;
    vram_buf[0x06] = 0xCE;
}

// bank=0 $836B
void func_00_836B()
{
    x = f_836B_0717;
    a = f_836B_0718;
    if (a == 0)
    {
        x++;
        f_836B_0717++;
        carry = true;
        a = peek((ushort)(0x8354 + x));
        f_836B_0718 = a;
        if (zero) return;
    }
    a = peek((ushort)(0x833F + x));
    game_00 = a;
    f_836B_0718--;
    carry = false;
}

// bank=0 $84C3
void func_00_84C3()
{
    do
    {
        return;
        a = buf_0110[x];
    }
    while (zero);
    if (a >= 0x0B)
    {
        a = 0x0B;
        buf_0110[x] = a;
    }
    y = a;
    a = buf_0110[x + 28];
    if (a == 0)
    {
        buf_0110[x] = a;
        return;
    }
    // DEC $012C
    if (a == 0x2B)
    {
        if (y == 0x0B)
        {
        game_5E++;
        zp_FE = 0x40;
        }
        a = peek((ushort)(0x84B7 + y));
        a >>= 4;
        x = a;
        a = peek((ushort)(0x84B7 + y));
        a &= 0x0F;
        buf_0134[x] = a;
        func_00_BC27();
    }
    // LDY $06E5
    a = buf_0016[x];
    if (a == 0x12) goto L_852B;
    if (a == 0x0D) goto L_852B;
    // if (a == 5) goto L_8523 // label not in this routine
    if (a == 0x0A) goto L_852B;
    if (a == 0x0B) goto L_852B;
    if (a < 9)
    {
        a = obj[x];
        if (a >= 2) goto L_852B;
    }
    x = vram_buf[0xEE];
    // LDY $06EC
    x = zp_08;
    L_852B: ;
    a = buf_0110[x + 14];
    if (a >= 0x18)
    {
        a = (byte)(a - 1 - (carry ? 0 : 1));
        buf_0110[x + 14] = a;
    }
    a = buf_0110[x + 14];
    a = (byte)(a - 8 - (carry ? 0 : 1));
    ClearOam_E5C1();
    oam_x[y] = buf_0110[x + 7];
    a += 8;
    oam_x[y + 4] = a;
    a = 2;
    oam_attr[y] = a;
    oam_attr[y + 4] = a;
    a = buf_0110[x];
    a *= 2;
    x = a;
    a = peek((ushort)(0x849F + x));
    oam_tile[y] = a;
    a = peek((ushort)(0x84A0 + x));
    oam_tile[y + 4] = a;
    x = zp_08;
    return;
}

// bank=0 $85F1
void FlushVramBuf_85F1()
{
    x = vram_buf[0];
    y = 0;
    a = game[0x57];
    if (a != 0) { y = 4; }
    a = game[0x5A];
    if (a == 2) { y = 8; }
    vram_ptr_lo = 3;
    do
    {
        a = peek((ushort)(0x85D7 + y));
        vram_buf[x + 4] = a;
        y++;
        x++;
        vram_ptr_lo--;
    }
    while (!negative);
    x = vram_buf[0];
    y = game[0x48];
    if (y == 0) { y = game[0x52]; }
    a = peek((ushort)(0x85CF + y));
    vram_buf[x + 4] = a;
    a = 0x3F;
    vram_buf[x + 1] = a;
    a = 0x10;
    vram_buf[x + 2] = a;
    a = 4;
    vram_buf[x + 3] = a;
    a = 0;
    vram_buf[x + 8] = a;
    a = x;
    a += 7;
}

// bank=0 $896A
void FlushVramBuf_896A()
{
    a = game[0x25];
    a &= 0x1F;
    carry = true;
    a = (byte)(a - 4 - (carry ? 0 : 1));
    a &= 0x1F;
    vram_ptr_hi = a;
    a = game[0x24];
    if (!carry) { a ^= 4; }
    a &= 4;
    a |= 0x23;
    vram_ptr_lo = a;
    a = vram_ptr_hi;
    a >>= 2;
    a = (byte)(a + 0xC0 + (carry ? 1 : 0));
    vram_ptr_hi = a;
    y = vram_buf[0x40];
    for (x = 0; x < 7; x++)
    {
        vram_buf[y + 65] = vram_ptr_lo;
        a = vram_ptr_hi;
        a += 8;
        vram_buf[y + 66] = a;
        vram_ptr_hi = a;
        vram_buf[y + 68] = vram_buf[x + 249];
        a = 1;
        vram_buf[y + 67] = a;
        carry = (a & 1) != 0; a >>= 1;
        vram_buf[x + 249] = a;
        y++;
        y++;
        y++;
        y++;
    }
    vram_buf[y + 65] = a;
    vram_buf[0x40] = y;
    game[0x77] = 6;
    return;
}

// bank=0 $89E1
void FlushVramBuf_89E1()
{
    a = zp_09;
    a &= 7;
    if (a != 0) return;
    x = vram_buf[0];
    if (x >= 0x31) return;
    for (y = a; y < 8; y++)
    {
        a = peek((ushort)(0x89C9 + y));
        vram_buf[x + 1] = a;
        x++;
    }
    x = vram_buf[0];
    vram_ptr_lo = 3;
    a = game[0x52];
    a *= 4;
    y = a;
    do
    {
        a = peek((ushort)(0x89D1 + y));
        vram_buf[x + 4] = a;
        y++;
        x++;
        vram_ptr_lo--;
    }
    while (!negative);
    x = vram_buf[0];
    y = f_89E1_06D4;
    a = peek((ushort)(0x89C3 + y));
    vram_buf[x + 5] = a;
    a = vram_buf[0];
    a += 7;
    vram_buf[0] = a;
    f_89E1_06D4++;
    a = f_89E1_06D4;
    if (a < 6) return;
    f_89E1_06D4 = 0;
}

// bank=0 $8A4D
void func_00_8A4D()
{
    y = 0x41;
    a = 3;
    x = game[0x52];
    if (x == 0) { a = 4; }
    FlushVramBuf_8A97();
    game[0x77] = 6;
    return;
}

// bank=0 $8A61
void func_00_8A61()
{
    func_00_8A6D();
    f_8A61_03F0++;
    // DEC $03EC
    return;
}

// bank=0 $8A6B
void func_00_8A6B()
{
    a = 0;
}

// bank=0 $8A6D
void func_00_8A6D()
{
    y = 3;
    // if (a == 0) goto L_8A87 // label not in this routine
    y = 0;
    // if (a == 0x58) goto L_8A87 // label not in this routine
    // if (a == 0x51) goto L_8A87 // label not in this routine
    y++;
    // if (a == 0x5D) goto L_8A87 // label not in this routine
    if (a != 0x52) { y++; }
    a = y;
    y = vram_buf[0];
    y++;
    FlushVramBuf_8A97();
    y--;
    a = y;
    a += 0x0A;
    func_00_863F(); return;
}

// bank=0 $8A97
void FlushVramBuf_8A97()
{
    vram_ptr_lo = x;
    vram_ptr_hi = y;
    a *= 4;
    x = a;
    y = 0x20;
    a = ptr_06_lo;
    if (a >= 0xD0) { y = 0x24; }
    zp_03 = 0x24;
    a &= 0x0F;
    a *= 2;
    ptr_04_lo = a;
    ptr_04_hi = 0;
    a = zp_02;
    a += 0x20;
    a *= 2;
    { bool _c = carry; byte _m = ptr_04_hi; carry = (_m & 0x80) != 0; ptr_04_hi = (byte)((_m << 1) | (_c ? 1 : 0)); }
    a *= 2;
    { bool _c = carry; byte _m = ptr_04_hi; carry = (_m & 0x80) != 0; ptr_04_hi = (byte)((_m << 1) | (_c ? 1 : 0)); }
    a = (byte)(a + ptr_04_lo + (carry ? 1 : 0));
    ptr_04_lo = a;
    a = ptr_04_hi;
    a += (byte)(carry ? 1 : 0);
    carry = false;
    a = (byte)(a + zp_03 + (carry ? 1 : 0));
    ptr_04_hi = a;
    y = vram_ptr_hi;
    a = peek((ushort)(0x8A39 + x));
    vram_buf[y + 3] = a;
    a = peek((ushort)(0x8A3A + x));
    vram_buf[y + 4] = a;
    a = peek((ushort)(0x8A3B + x));
    vram_buf[y + 8] = a;
    a = peek((ushort)(0x8A3C + x));
    vram_buf[y + 9] = a;
    vram_buf[y + 1] = ptr_04_lo;
    a += 0x20;
    vram_buf[y + 6] = a;
    vram_buf[y] = ptr_04_hi;
    vram_buf[y + 5] = a;
    a = 2;
    vram_buf[y + 2] = a;
    vram_buf[y + 7] = a;
    a = 0;
    vram_buf[y + 10] = a;
    x = vram_ptr_lo;
    return;
}

// bank=0 $8F06
void func_00_8F06()
{
    vram_ptr_lo = a;
    FlushVramBuf_8F11();
    a = vram_ptr_lo;
    a >>= 4;
}

// bank=0 $8F11
void FlushVramBuf_8F11()
{
    a += 1;
    a &= 0x0F;
    if (a >= 6) return;
    pha();
    a *= 2;
    y = a;
    x = vram_buf[0];
    a = 0x20;
    if (y == 0) { a = 0x22; }
    vram_buf[x + 1] = a;
    a = peek((ushort)(0x8EF4 + y));
    vram_buf[x + 2] = a;
    a = peek((ushort)(0x8EF5 + y));
    vram_buf[x + 3] = a;
    zp_03 = a;
    zp_02 = x;
    a = pla();
    x = a;
    a = peek((ushort)(0x8F00 + x));
    carry = true;
    // SBC $8EF5
    y = a;
    x = zp_02;
    do
    {
        vram_buf[x + 4] = score[y];
        x++;
        y++;
    }
    while (--zp_03 != 0);
    a = 0;
    vram_buf[x + 4] = a;
    x++;
    x++;
    x++;
    vram_buf[0] = x;
}

// bank=0 $8F5F
void func_00_8F5F()
{
    a = game[0x74];
    if (a != 0)
    {
        x = 5;
        a = buf_0134[x];
        carry = false;
        // ADC $07D7
        if (!(negative))
        {
            if (a < 0x0A)
            {
                }
                a = 0;
                x = 6;
                do
                {
                buf_0110[x + 35] = a;
                x--;
                }
                while (!negative);
                return;
                }
                // DEC $0133
                a = 9;
                if (a != 0) { func_00_8F75(); return; }
            }
    carry = true;
    a = (byte)(a - 0x0A - (carry ? 0 : 1));
    // INC $0133
    func_00_8F75(); return;
}

// bank=0 $90ED
void func_00_90ED()
{
    a = game[0x74];
    if (a == 0) return;
    a = game[0x56];
    if (a != 2)
    {
        y = 5;
        a = game[0x14];
        // if (a == 6) goto L_9110 // label not in this routine
        // if (a == 7) goto L_9110 // label not in this routine
    }
    y = game[0x52];
    a = game[0x47];
    if (a != 0) { y = 4; }
    a = peek((ushort)(0x90E7 + y));
    zp_FB = a;
}

// bank=0 $9248
void func_00_9248()
{
    zp_FC = 0x80;
    func_00_9282();
    if (carry)
    {
        ram_07FD = game[0x63];
        a = 0;
        a *= 2;
        game[0x76] = a;
        ram_07A0 = a;
        game[0x74] = a;
        return;
    }
    func_00_9C03();
    game[0x58] = 1;
    game[0x5B]++;
    game[0x4B] = 0;
    game[0x5A] = 0;
    zp_0E = 0;
    game[0x76] = 0;
    game[0x74] = 1;
    return;
}

// bank=0 $9282
void func_00_9282()
{
    carry = true;
    a = game[0x7E];
    if (a == 0) return;
    a = game[0x65];
    if (a >= 0x80) return;
    a = game[0x57];
    a ^= 1;
    game[0x57] = a;
    x = 6;
    do
    {
        a = game[x + 94];
        pha();
        game[x + 94] = game[x + 101];
        a = pla();
        game[x + 101] = a;
        x--;
    }
    while (!negative);
    carry = false;
}

// bank=0 $92B0
void func_00_92B0()
{
    y = game[0x23];
    if (y == 0) { game[0x23] = 8; }
    y--;
    a = y;
    func_00_92C8();
    game[0x23]--;
    if (!zero) return;
    FlushVramBuf_896A();
}

// bank=0 $92C8
void func_00_92C8()
{
    func_00_8E04();
    // ???(DB)
    // ???(92)
    x = peek(rom_AE88);
    y--;
    // ???(FC)
    // ???(93)
    // ???(DB)
    // ???(92)
    x = peek(rom_AE88);
    y--;
    // ???(FC)
    // ???(93)
    game[0x2A]++;
    a = game[0x2A];
    a &= 0x0F;
    if (a == 0)
    {
        game[0x2A] = a;
        game[0x29]++;
    }
    f_92C8_06A0++;
    a = f_92C8_06A0;
    a &= 0x1F;
    f_92C8_06A0 = a;
    return;
}

// bank=0 $9716
void func_00_9716()
{
    vram_ptr_lo = a;
    a = 0;
    x = 4;
    do
    {
        // LDY $16
        // CPY $00
        if (zero) { buf_000F[x] = a; }
        x--;
    }
    while (!negative);
    return;
}

// bank=0 $9BE1
void func_00_9BE1()
{
    pha();
    a >>= 4;
    y = a;
    a = peek((ushort)(0x9BDF + y));
    ptr_06_hi = a;
    a = pla();
    a &= 0x0F;
    carry = false;
    // ADC $9BDD
    ptr_06_lo = a;
    return;
}

// bank=0 $9C03
void func_00_9C03()
{
    func_00_9C13();
    game[0x54] = a;
    a &= 0x60;
    a *= 2;
    { bool _c = carry; carry = (a & 0x80) != 0; a = (byte)((a << 1) | (_c ? 1 : 0)); }
    { bool _c = carry; carry = (a & 0x80) != 0; a = (byte)((a << 1) | (_c ? 1 : 0)); }
    { bool _c = carry; carry = (a & 0x80) != 0; a = (byte)((a << 1) | (_c ? 1 : 0)); }
    game[0x52] = a;
    return;
}

// bank=0 $9C13
void func_00_9C13()
{
    y = game[0x63];
    a = peek((ushort)(0x9CB4 + y));
    carry = false;
    a = (byte)(a + game[0x64] + (carry ? 1 : 0));
    y = a;
    a = peek((ushort)(0x9CBC + y));
    return;
}

// bank=0 $AEEA
void func_00_AEEA()
{
    x = game[0x57];
}

// bank=0 $AF93
void func_00_AF93()
{
    a = f_AF93_06FF;
    carry = false;
    a = (byte)(a + vram_buf[0xA1] + (carry ? 1 : 0));
    f_AF93_06FF = a;
    a = game[0x27];
    // if (a != 0) goto L_AFFB // label not in this routine
    a = game[0x59];
    // if (a < 0x50) goto L_AFFB // label not in this routine
    a = game[0x89];
    if (a == 0)
    {
        y = f_AF93_06FF;
        y--;
        // if (negative) goto L_AFFB // label not in this routine
        y++;
        if (y >= 2) { y--; }
        a = game[0x59];
        if (a >= 0x70) { y = f_AF93_06FF; }
        a = y;
        game[0x79] = a;
        carry = false;
        a = (byte)(a + game[0x41] + (carry ? 1 : 0));
        game[0x41] = a;
        a = y;
        carry = false;
        a = (byte)(a + game[0x20] + (carry ? 1 : 0));
        game[0x20] = a;
        scroll = a;
        game[0x1E] += (byte)(carry ? 1 : 0);
        a &= 1;
        vram_ptr_lo = a;
        a = ppu_ctrl_shadow;
        a &= (byte)~PPUCTRL.NAMETABLE_X;
        a = (byte)(a | vram_ptr_lo);
        ppu_ctrl_shadow = a;
        func_00_B038();
        poke((ushort)0x0795, 8);
        func_00_B000(); return;
    }
    game[0x79] = 0;
}

// bank=0 $B038
void func_00_B038()
{
    ushort _u0 = (ushort)(game[0x20] | (game[0x1E] << 8));
    _u0 += 0xFF;
    game[0x21] = (byte)_u0;
    game[0x1F] = (byte)(_u0 >> 8);
    return;
}

// bank=0 $B04A
void func_00_B04A()
{
    do
    {
        return;
        a = zp_0E;
        func_00_8E04();
        // AND $91
        // ???(C7)
        a = peek((ushort)((ptr_06_lo | ptr_06_hi << 8) + y));
        // ???(B2)
        a = (byte)(a - obj_009F[0x12] - (carry ? 0 : 1));
        y = obj_009F[0x13];
        x--;
        // ???(B2)
        { byte _v = peek((ushort)0x6991); zero = a == _v; carry = a >= _v; negative = (byte)(a - _v) >= 0x80; }
    }
    while (carry);
    if (carry) goto L_B095;
    // ???(B2)
    // EOR $B2
    a = (byte)(a + 0xB2 + (carry ? 1 : 0));
    // ADC $ADB2
    // ???(52)
    // ???(07)
    if (a == 2) goto L_B09B;
    a = 0;
    y = buf_00CE_00;
    if (y < 0x30) { func_00_B0E6(); return; }
    a = game[0x14];
    if (a != 6)
    {
        if (a != 7) goto L_B0D3;
    }
    a = vram_buf[0xC4];
    if (a != 0)
    {
        func_00_B21F();
        buf_06BE[0x20]--;
        if (!zero) return;
    }
    else
    {
        a = 1;
        func_00_B0E6(); return;
    }
    L_B095: ;
    f_B04A_0769++;
    func_00_B315(); return;
    L_B09B: ;
    a = game[0x5C];
    if (a == 0)
    {
        a = 0xFF;
        func_00_B200();
        a = buf_00CE_00;
        if (a < 0x91) goto L_B0D3;
        return;
    }
    a = vram_buf[0x99];
    if (a != 0x60) return;
    a = buf_00CE_00;
    zero = a == 0x99; carry = a >= 0x99; negative = (byte)(a - 0x99) >= 0x80;
    y = 0;
    a = 1;
    if (carry)
    {
        zp_1D = 3;
        y++;
        poke((ushort)0x05B4, 8);
    }
    game[0x1A] = 0;
    func_00_B0E6();
    a = buf_0086_00;
    if (a < 0x48) return;
    L_B0D3: ;
    zp_0E = 8;
    zp_33 = 1;
    carry = (a & 1) != 0; a >>= 1;
    game[0x56] = a;
    game[0x1A] = a;
    game[0x5C] = a;
}

// bank=0 $B0E6
void func_00_B0E6()
{
    game_00 = a;
    a = zp_0E;
    if (a != 0x0B)
    {
        a = game[0x52];
        if (a != 0) { func_00_B104(); return; }
        y = obj_009F_16;
        y--;
        if (y == 0)
        {
        a = buf_00CE_00;
        if (a < 0xD0) { func_00_B104(); return; }
        }
        game_00 = 0;
    }
    func_00_B329();
    y = 1;
    a = game[0x58];
    // if (a != 0) goto L_B13E // label not in this routine
    y = 0;
    a = game[0x18];
    if (a != 0) { y = 2; }
    buf_0491[0x08] = 2;
    a = 1;
    y = obj_0046[0x11];
    if (y != 0)
    {
        if (negative) { carry = (a & 0x80) != 0; a <<= 1; }
        zp_45 = a;
    }
    func_00_AF93();
    func_F180();
    func_F12A();
    x = 0;
    func_E29C();
    func_DC64();
    a = buf_00CE_00;
    // if (a < 0x40) goto L_B179 // label not in this routine
    a = zp_0E;
    // if (a == 5) goto L_B179 // label not in this routine
    // if (a == 7) goto L_B179 // label not in this routine
    if (a >= 4)
    {
        a = vram_buf[0xC4];
        a &= 0xDF;
        vram_buf[0xC4] = a;
    }
    a = obj_009F_16;
    zero = a == 2; carry = a >= 2; negative = (byte)(a - 2) >= 0x80;
    // if (negative) goto L_B1BA // label not in this routine
    game[0x27] = 1;
    ptr_06_hi = 4;
    x = 0;
    y = game[0x5D];
    if (y == 0)
    {
        y = game[0x47];
        if (y != 0) goto L_B1AA;
    }
    x++;
    y = zp_0E;
    if (y == 0x0B) goto L_B1AA;
    y = game[0x16];
    if (y == 0)
    {
        y++;
        zp_FC = y;
        game[0x16] = y;
    }
    ptr_06_hi = 6;
    L_B1AA: ;
    { byte _v = ptr_06_hi; zero = a == _v; carry = a >= _v; negative = (byte)(a - _v) >= 0x80; }
    // if (negative) goto L_B1BA // label not in this routine
    x--;
    if (x < 0x80)
    {
        y = ram_07B1;
        if (y == 0) { zp_0E = 6; }
        return;
    }
    game[0x5C] = 0;
    func_00_B1DD();
    game[0x56]++;
    return;
}

// bank=0 $B1DD
void func_00_B1DD()
{
    game[0x56] = 2;
    func_00_B213(); return;
}

// bank=0 $B200
void func_00_B200()
{
    carry = false;
    a = (byte)(a + buf_00CE_00 + (carry ? 1 : 0));
    buf_00CE_00 = a;
    return;
}

// bank=0 $B213
void func_00_B213()
{
    game[0x78]++;
    game[0x76] = 0;
    game[0x26] = 0;
    return;
}

// bank=0 $B21F
void func_00_B21F()
{
    obj_0046[0x11] = 8;
    y = 1;
    a = buf_0086_00;
    a &= 0x0F;
    if (a == 0)
    {
        obj_0046[0x11] = a;
        y = a;
    }
    a = y;
    func_00_B0E6();
    return;
}

// bank=0 $B288
void func_00_B288()
{
    a &= 3;
    vram_ptr_lo = a;
    a = vram_buf[0xC4];
    a &= 0xFC;
    a = (byte)(a | vram_ptr_lo);
    vram_buf[0xC4] = a;
    return;
}

// bank=0 $B29A
void func_00_B29A()
{
    a = vram_buf[0xC4];
    a &= 0xFC;
    vram_buf[0xC4] = a;
    return;
}

// bank=0 $B329
void func_00_B329()
{
    a = 0;
    y = game[0x58];
    if (y == 0)
    {
        a = zp_1D;
        if (a == 0)
        {
            a = zp_0B;
            a &= 4;
            }
            game[0x18] = a;
        }
    func_00_B450();
    a = game[0x0F];
    if (a != 0) return;
    a = zp_1D;
    if (a != 3) { game[0x8D] = 0x18; }
    func_00_8E04();
    // ???(5A)
    // ???(B3)
    // ROR $B3
    a = (byte)(a + peek((ushort)0xCFB3) + (carry ? 1 : 0));
    // ???(B3)
}

// bank=0 $B450
void func_00_B450()
{
    a = zp_1D;
    if (a == 3)
    {
        y = 0;
        a = zp_0B;
        a = (byte)(a & ram_0490);
        // if (a == 0) goto L_B465 // label not in this routine
        y++;
        a &= 8;
        if (a == 0) { y++; }
        // LDX $B44D
        buf_0433_00 = x;
        a = 8;
        // LDX $B44A
        obj_009F_00 = x;
        if (!negative) { carry = (a & 1) != 0; a >>= 1; }
        game[0x10] = a;
        return;
    }
    a = game[0x12];
    // if (a != 0) goto L_B488 // label not in this routine
    a = zp_0A;
    a &= 0x80;
    if (a != 0)
    {
        a = (byte)(a & zp_0D);
        if (a != 0)
        {
            }
            func_00_B51C(); return;
        }
    a = zp_1D;
    // if (a == 0) goto L_B4A0 // label not in this routine
    a = game[0x08];
    // if (a == 0) goto L_B488 // label not in this routine
    a = game[0x86];
    if (a == 0)
    {
        a = obj_009F_00;
        // if (!negative) goto L_B4A0 // label not in this routine
        func_00_B51C(); return;
    }
    game[0x86] = 0x20;
    poke(ram_0416, 0);
    buf_0433_00 = 0;
    game[0x0B] = obj_009F_16;
    game[0x0C] = buf_00CE_00;
    zp_1D = 1;
    a = game[0x04];
    // if (a < 9) goto L_B4D2 // label not in this routine
    y++;
    // if (a < 0x10) goto L_B4D2 // label not in this routine
    y++;
    // if (a < 0x19) goto L_B4D2 // label not in this routine
    y++;
    if (a >= 0x1C) { y++; }
    game[0x0A] = 1;
    a = game[0x08];
    // if (a == 0) goto L_B4E4 // label not in this routine
    y = 5;
    a = buf_047D_00;
    if (a != 0) { y++; }
    a = peek((ushort)(0xB424 + y));
    game[0x0D] = a;
    a = peek((ushort)(0xB42B + y));
    game[0x0E] = a;
    a = peek((ushort)(0xB439 + y));
    buf_0433_00 = a;
    a = peek((ushort)(0xB432 + y));
    obj_009F_00 = a;
    a = game[0x08];
    if (a == 0)
    {
        a = 1;
        y = game[0x58];
        if (y != 0) { a = 0x80; }
        zp_FF = 0x80;
    }
    else
    {
        zp_FF = 4;
        a = buf_00CE_00;
        if (a >= 0x14) { func_00_B51C(); return; }
        obj_009F_00 = 0;
        func_00_B51C(); return;
    }
}

// bank=0 $B624
void func_00_B624()
{
    a = game[0x5A];
    if (a < 2) goto L_B66E;
    a = zp_0A;
    a &= 0x40;
    // if (a == 0) goto L_B664 // label not in this routine
    a = (byte)(a & zp_0D);
    // if (a != 0) goto L_B664 // label not in this routine
    a = f_B624_06CE;
    a &= 1;
    x = a;
    a = obj[x + 6];
    // if (a != 0) goto L_B664 // label not in this routine
    y = obj_009F_16;
    y--;
    // if (y != 0) goto L_B664 // label not in this routine
    a = game[0x18];
    // if (a != 0) goto L_B664 // label not in this routine
    a = zp_1D;
    if (a != 3)
    {
        zp_FF = 0x20;
        a = 2;
        obj[x + 6] = a;
        y = game[0x10];
        game[0x15] = y;
        y--;
        game[0x85] = y;
        f_B624_06CE++;
    }
    x = 0;
    func_00_B689();
    x = 1;
    func_00_B689();
    L_B66E: ;
    a = game[0x52];
    if (a != 0) return;
    x = 2;
    do
    {
        zp_08 = 2;
        func_00_B6F9();
        func_F131();
        func_F191();
        func_EDE1();
        x--;
    }
    while (!negative);
}

// bank=0 $B689
void func_00_B689()
{
    zp_08 = x;
    a = obj[x + 6];
    a *= 2;
    if (carry) goto L_B6F3;
    // LDY $24
    if (zero) goto L_B6F2;
    y--;
    if (y != 0)
    {
        a = buf_0086_00;
        a = (byte)(a + 4 + (carry ? 1 : 0));
        obj_0087[x + 6] = a;
        obj_006E[x + 6] = (byte)(buf_006D_00 + (carry ? 1 : 0));
        obj_00CF[x + 6] = buf_00CE_00;
        a = 1;
        obj_00B6[x + 6] = a;
        y = zp_33;
        y--;
        a = peek((ushort)(0xB687 + y));
        obj_0058[x + 6] = a;
        a = 4;
        obj_009F[x + 7] = a;
        a = 7;
        buf_0491[x + 15] = a;
        // DEC $24
    }
    a = x;
    a += 7;
    x = a;
    vram_ptr_lo = 0x50;
    zp_02 = 3;
    a = 0;
    func_00_BFD7();
    func_00_BF0F();
    x = zp_08;
    func_F13B();
    func_F187();
    func_E22D();
    func_E1C8();
    a = vram_buf[0xD2];
    a &= 0xCC;
    if (a != 0)
    {
        a = 0;
        obj[x + 6] = a;
    }
    else
    {
        func_D6D9();
        func_ECDE(); return;
    }
    L_B6F2: ;
    return;
    L_B6F3: ;
    func_F13B();
    ClearOam_ED09(); return;
}

// bank=0 $B6F9
void func_00_B6F9()
{
    a = buf_07A8[x];
    a &= 1;
    ptr_06_hi = a;
    a = obj_00E4[x];
    if (a == 0xF8)
    {
        a = f_B6F9_0792;
        if (a != 0) return;
        y = 0;
        a = zp_33;
        if ((a & 1) != 0) { y = 8; }
        a = y;
        a = (byte)(a + buf_0086_00 + (carry ? 1 : 0));
        obj_0087[x + 21] = a;
        obj_006E[x + 21] = (byte)(buf_006D_00 + (carry ? 1 : 0));
        a = buf_00CE_00;
        a += 8;
        obj_00E4[x] = a;
        a = 1;
        obj_00B6[x + 21] = a;
        y = ptr_06_hi;
        a = peek((ushort)(0xB74D + y));
        f_B6F9_0792 = a;
    }
    y = ptr_06_hi;
    a = peek((ushort)(0x042C + x));
    carry = true;
    // SBC $B74B
    poke((ushort)((ushort)0x042C + x), a);
    a = obj_00E4[x];
    a = (byte)(a - 0 - (carry ? 0 : 1));
    if (a < 0x20) { a = 0xF8; }
    obj_00E4[x] = a;
}

// bank=0 $B74F
void func_00_B74F()
{
    a = game[0x74];
    if (a == 0) return;
    a = zp_0E;
    if (a < 8) return;
    if (a == 0x0B) return;
    a = obj_009F_16;
    if (a >= 2) return;
    a = f_B74F_0787;
    if (a != 0) return;
    a = f_B74F_07F8;
    a = (byte)(a | f_B74F_07F9);
    a = (byte)(a | f_B74F_07FA);
    if (a != 0)
    {
        y = f_B74F_07F8;
        y--;
        // if (y != 0) goto L_B786 // label not in this routine
        a = f_B74F_07F9;
        a = (byte)(a | f_B74F_07FA);
        if (a == 0) { zp_FC = 0x40; }
        f_B74F_0787 = 0x18;
        y = 0x23;
        ram_0139 = 0xFF;
        func_00_8F5F();
        a = 0xA4;
        func_00_8F06(); return;
    }
    game[0x5A] = 0xA4;
    func_D931();
    game[0x5D]++;
}

// bank=0 $B7B8
void func_00_B7B8()
{
    a = game[0x52];
    if (a != 0) goto L_B7F4;
    buf_047D_00 = a;
    a = game[0x4B];
    if (a != 0) goto L_B7F4;
    y = 4;
    L_B7C7: ;
    a = buf_046B[y + 6];
    carry = false;
    // ADC $0477
    zp_02 = a;
    a = buf_046B[y];
    if (a != 0)
    {
        a += (byte)(carry ? 1 : 0);
        vram_ptr_hi = a;
        a = buf_0086_00;
        carry = true;
        // SBC $0471
        a = buf_006D_00;
        // SBC $046B
        // if (negative) goto L_B7F1 // label not in this routine
        a = zp_02;
        carry = true;
        a = (byte)(a - buf_0086_00 - (carry ? 0 : 1));
        a = vram_ptr_hi;
        a = (byte)(a - buf_006D_00 - (carry ? 0 : 1));
        if (a < 0x80) goto L_B7F5;
    }
    y--;
    if (y < 0x80) goto L_B7C7;
    L_B7F4: ;
    return;
    L_B7F5: ;
    a = buf_046B[y + 12];
    carry = (a & 1) != 0; a >>= 1;
    vram_ptr_lo = a;
    a = buf_046B[y + 6];
    carry = false;
    a = (byte)(a + vram_ptr_lo + (carry ? 1 : 0));
    vram_ptr_hi = a;
    vram_ptr_lo = (byte)(buf_046B[y] + (carry ? 1 : 0));
    a = zp_09;
    carry = (a & 1) != 0; a >>= 1;
    if (!carry) goto L_B83B;
    a = vram_ptr_hi;
    carry = true;
    a = (byte)(a - buf_0086_00 - (carry ? 0 : 1));
    a = vram_ptr_lo;
    a = (byte)(a - buf_006D_00 - (carry ? 0 : 1));
    if (a >= 0x80)
    {
        a = buf_0086_00;
        carry = true;
        a = (byte)(a - 1 - (carry ? 0 : 1));
        buf_0086_00 = a;
        a = buf_006D_00;
        a = (byte)(a - 0 - (carry ? 0 : 1));
        func_00_B839(); return;
    }
    a = ram_0490;
    carry = (a & 1) != 0; a >>= 1;
    if (!carry) goto L_B83B;
    a = buf_0086_00;
    a += 1;
    buf_0086_00 = a;
    a = buf_006D_00;
    a += (byte)(carry ? 1 : 0);
    L_B83B: ;
    vram_ptr_lo = 0x10;
    buf_047D_00 = 1;
    zp_02 = 1;
    carry = (a & 1) != 0; a >>= 1;
    x = a;
    func_00_BFD7(); return;
}

// bank=0 $B855
void func_00_B855()
{
    zp_08 = 5;
    a = buf_0016[x];
    if (a != 0x30) return;
    a = zp_0E;
    // if (a != 4) goto L_B896 // label not in this routine
    a = zp_1D;
    if (a == 3)
    {
        a = obj_00CF[x];
        if (a >= 0xAA) goto L_B899;
        a = buf_00CE_00;
        if (a >= 0xA2) goto L_B899;
        a = peek((ushort)(0x0417 + x));
        a = (byte)(a + 0xFF + (carry ? 1 : 0));
        poke((ushort)((ushort)0x0417 + x), a);
        a = obj_00CF[x];
        a = (byte)(a + 1 + (carry ? 1 : 0));
        obj_00CF[x] = a;
        a = f_B855_010E;
        carry = true;
        a = (byte)(a - 0xFF - (carry ? 0 : 1));
        f_B855_010E = a;
        a = ram_010D;
        a = (byte)(a - 1 - (carry ? 0 : 1));
        ram_010D = a;
    }
    func_00_B8AC(); return;
    L_B899: ;
    y = ram_010F;
    a = peek((ushort)(0xB84B + y));
    // LDX $B850
    buf_0134[x] = a;
    func_00_BC27();
    zp_0E = 5;
}

// bank=0 $B9BC
void func_00_B9BC()
{
    a = game[0x52];
    if (a == 0) return;
    zp_08 = 2;
    a = buf_000F[x];
    if (a != 0) { func_00_BA1A(); return; }
    a = buf_07A8[x];
    y = buf_06BE[0x0E];
    a = (byte)(a & peek((ushort)(0xB9BA + y)));
    if (a >= 6) { func_00_BA1A(); return; }
    y = a;
    a = buf_046B[y];
    if (a == 0) { func_00_BA1A(); return; }
    a = buf_047D[y];
    if (a == 0)
    {
        a = game[0x4B];
        if (a != 0) { func_00_BA1A(); return; }
        a = 0x0E;
        buf_047D[y] = a;
        obj_006E[x] = buf_046B[y];
        obj_0087[x] = buf_046B[y + 6];
        a = buf_046B[y + 12];
        carry = true;
        a = (byte)(a - 8 - (carry ? 0 : 1));
        obj_00CF[x] = a;
        a = 1;
        obj_00B6[x] = a;
        buf_000F[x] = a;
        carry = (a & 1) != 0; a >>= 1;
        obj[x] = a;
        a = 9;
        buf_0491[x + 9] = a;
        a = 0x33;
        buf_0016[x] = a;
        func_00_BA2D(); return;
    }
    else
    {
        a = (byte)(a - 0 - (carry ? 0 : 1));
        buf_047D[y] = a;
        func_00_BA1A(); return;
    }
}

// bank=0 $BA33
void func_00_BA33()
{
    a = game[0x4B];
    if (a != 0) goto L_BA76;
    a = obj[x];
    if (a == 0)
    {
        a = vram_buf[0xD1];
        a &= 0x0C;
        if (a == 0x0C) goto L_BA85;
        y = 1;
        func_E143();
        if (!negative) { y++; }
        // STY $46
        y--;
        a = peek((ushort)(0xBA31 + y));
        obj_0058[x] = a;
        a = vram_ptr_lo;
        a = (byte)(a + 0x28 + (carry ? 1 : 0));
        if (a < 0x50) goto L_BA85;
        a = 1;
        obj[x] = a;
        a = 0x0A;
        game[x + 142] = a;
        zp_FE = 8;
    }
    a = obj[x];
    a &= 0x20;
    if (a != 0) { func_00_BF63(); }
    func_00_BF02();
    L_BA76: ;
    func_F1AF();
    func_F152();
    func_E243();
    FlushVramBuf_D853();
    FlushVramBuf_E87D(); return;
    L_BA85: ;
    func_C998();
    return;
}

// bank=0 $BAC3
void func_00_BAC3()
{
    a = game[0x4B];
    if (a != 0) goto L_BB2B;
    a = obj[x + 12];
    a &= 0x7F;
    // LDY $06AE
    if (a != 2)
    {
        if (!(carry))
        {
            a = x;
            a += 0x0D;
            x = a;
            vram_ptr_lo = 0x10;
            vram_ptr_hi = 0x0F;
            zp_02 = 4;
            a = 0;
            func_00_BFD7();
            func_00_BF0F();
            x = zp_08;
            func_00_BB28(); return;
            }
            a = 0xFE;
            obj_009F[x + 13] = a;
            a = obj[y];
            a &= 0xF7;
            obj[y] = a;
            // LDX $46
            x--;
            a = peek((ushort)(0xBA92 + x));
            x = zp_08;
            obj_0058[x + 12] = a;
        }
    // DEC $2A
    ushort _u1 = (ushort)(obj_0087[y] | (obj_006E[y] << 8));
    _u1 += 2;
    obj_0087[x + 12] = (byte)_u1;
    obj_006E[x + 12] = (byte)(_u1 >> 8);
    a = obj_00CF[y];
    carry = true;
    a = (byte)(a - 0x0A - (carry ? 0 : 1));
    obj_00CF[x + 12] = a;
    a = 1;
    obj_00B6[x + 12] = a;
    if (!zero) goto L_BB2B;
    L_BB2B: ;
    func_F19B();
    func_F148();
    func_E236();
    ClearOam_E4DC();
    return;
}

// bank=0 $BB51
void func_00_BB51()
{
    func_00_BB84();
    obj_006E[y + 12] = vram_buf[x + 234];
    a = ptr_06_lo;
    a *= 16;
    a |= 5;
    obj_0087[y + 12] = a;
    a = zp_02;
    a = (byte)(a + 0x20 + (carry ? 1 : 0));
    obj_00CF[y + 12] = a;
    a = 0xFB;
    obj_009F[y + 13] = a;
    a = 1;
    obj_00B6[y + 12] = a;
    obj[y + 12] = a;
    zp_FE = a;
    zp_08 = x;
    func_00_BBFE();
    game[0x4C]++;
    return;
}

// bank=0 $BB84
void func_00_BB84()
{
    y = 8;
    L_BB86: ;
    a = obj[y + 12];
    if (a != 0)
    {
        y--;
        if (y != 5) goto L_BB86;
        y = 8;
    }
    poke((ushort)0x06B7, 8);
    return;
}

// bank=0 $BB96
void func_00_BB96()
{
    zp_08 = 8;
    a = obj[x + 12];
    if (a == 0) { func_00_BBF4(); return; }
    a *= 2;
    if (carry)
    {
        func_00_BAC3();
        func_00_BBF4(); return;
    }
    // LDY $2A
    y--;
    if (y != 0)
    {
        // INC $2A
        a = obj_0087[x + 12];
        carry = false;
        a = (byte)(a + game[0x79] + (carry ? 1 : 0));
        obj_0087[x + 12] = a;
        obj_006E[x + 12] += (byte)(carry ? 1 : 0);
        a = obj[x + 12];
        // if (a != 0x30) goto L_BBE8 // label not in this routine
        a = 0;
        obj[x + 12] = a;
        func_00_BBF4(); return;
    }
    a = x;
    a += 0x0D;
    x = a;
    vram_ptr_lo = 0x50;
    zp_02 = 6;
    carry = (a & 1) != 0; a >>= 1;
    vram_ptr_hi = a;
    a = 0;
    func_00_BFD7();
    x = zp_08;
    a = obj_009F[x + 13];
    if (a == 5)
    {
        // INC $2A
    }
    func_F148();
    func_F19B();
    func_E236();
    func_E686();
}

// bank=0 $BBFE
void func_00_BBFE()
{
    ram_0139 = 1;
    x = game[0x57];
    // LDY $BBF8
    func_00_8F5F();
    f_BBFE_075E++;
    a = f_BBFE_075E;
    if (a == 0x64)
    {
        f_BBFE_075E = 0;
        game_5E++;
        zp_FE = 0x40;
    }
    poke((ushort)0x0138, 2);
}

// bank=0 $BC27
void func_00_BC27()
{
    x = game[0x57];
    // LDY $BBFA
    func_00_8F5F();
    y = game[0x57];
    a = peek((ushort)(0xBBFC + y));
    func_00_8F06();
    y = vram_buf[0];
    a = oam_x[y + 248];
    if (a == 0)
    {
        a = 0x24;
        oam_x[y + 248] = a;
    }
    x = zp_08;
    return;
}

// bank=0 $BCED
void FlushVramBuf_BCED()
{
    pha();
    a = 0x11;
    x = vram_buf[0xEE];
    y = game[0x58];
    if (y == 0) { a = 0x12; }
    obj[x + 8] = a;
    func_00_8A6B();
    x = vram_buf[0xEE];
    vram_buf[x + 228] = zp_02;
    y = a;
    vram_buf[x + 230] = ptr_06_lo;
    a = peek((ushort)((ptr_06_lo | ptr_06_hi << 8) + y));
    func_00_BDF6();
    vram_ptr_lo = a;
    y = game[0x58];
    if (y == 0) { a = y; }
    if (!carry) goto L_BD41;
    y = 0x11;
    // STY $26
    a = 0xC4;
    y = vram_ptr_lo;
    if (y != 0x58)
    {
        if (y != 0x5D) goto L_BD41;
    }
    a = f_BCED_06BC;
    if (a == 0)
    {
        f_BCED_079D = 0x0B;
        f_BCED_06BC++;
    }
    a = f_BCED_079D;
    if (a == 0) { y = 0xC4; }
    a = y;
    L_BD41: ;
    vram_buf[x + 232] = a;
    func_00_BD84();
    y = zp_02;
    a = 0x23;
    poke((ushort)((ptr_06_lo | ptr_06_hi << 8) + y), a);
    game[0x88] = 0x10;
    a = pla();
    ptr_04_hi = a;
    y = 0;
    a = game[0x18];
    if (a == 0)
    {
        a = game[0x58];
        if (a != 0)
        {
            }
            y++;
        }
    a = buf_00CE_00;
    carry = false;
    // ADC $BCEB
    a &= 0xF0;
    obj_00CF[x + 8] = a;
    // LDY $26
    if (y != 0x11)
    {
        func_00_BE02();
        func_00_BD7B(); return;
    }
    func_00_BD9B();
}

// bank=0 $BD84
void func_00_BD84()
{
    ushort _u2 = (ushort)(buf_0086_00 | (buf_006D_00 << 8));
    _u2 += 8;
    obj_0087[x + 8] = (byte)(_u2 & 0xF0);
    obj_006E[x + 8] = (byte)(_u2 >> 8);
    vram_buf[x + 234] = (byte)(_u2 >> 8);
    obj_00B6[x + 8] = obj_009F_16;
    return;
}

// bank=0 $BD9B
void func_00_BD9B()
{
    func_00_BE1F();
    zp_FF = 2;
    a = 0;
    obj_0058[x + 8] = a;
    buf_0433[x + 9] = a;
    obj_009F_00 = a;
    a = 0xFE;
    obj_009F[x + 9] = a;
    a = ptr_04_hi;
    func_00_BDF6();
    if (!carry) return;
    a = y;
    if (a >= 9) { a = (byte)(a - 5 - (carry ? 0 : 1)); }
    func_00_8E04();
    // ???(D2)
    a = peek((ushort)(0xBB38 + x));
    carry = true;
    // ???(BB)
    // CLD
    a = peek((ushort)(0xBDD2 + x));
    // ???(DF)
    a = peek((ushort)(0xBDD5 + x));
    carry = true;
    // ???(BB)
    // CLD
    a = obj_009F[x + 10];
    // BIT $02A9
    // BIT $03A9
    zp_39 = a;
    func_00_BC49(); return;
}

// bank=0 $BDF6
void func_00_BDF6()
{
    y = 0x0D;
    do
    {
        if (a == peek((ushort)(0xBDE8 + y))) return;
        y--;
    }
    while (!negative);
    carry = false;
}

// bank=0 $BE02
void func_00_BE02()
{
    func_00_BE1F();
    a = 1;
    vram_buf[x + 236] = a;
    zp_FD = a;
    func_00_BE41();
    obj_009F_00 = 0xFE;
    ram_0139 = 5;
    func_00_BC27();
    x = vram_buf[0xEE];
    return;
}

// bank=0 $BE1F
void func_00_BE1F()
{
    x = vram_buf[0xEE];
    y = zp_02;
    if (y == 0) return;
    a = y;
    carry = true;
    a = (byte)(a - 0x10 - (carry ? 0 : 1));
    zp_02 = a;
    y = a;
    a = peek((ushort)((ptr_06_lo | ptr_06_hi << 8) + y));
    if (a != 0xC2) return;
    a = 0;
    poke((ushort)((ptr_06_lo | ptr_06_hi << 8) + y), a);
    func_00_8A4D();
    x = vram_buf[0xEE];
    func_00_BB51();
}

// bank=0 $BE41
void func_00_BE41()
{
    vram_buf[x + 241] = obj_0087[x + 8];
    a = 0xF0;
    obj_0058[x + 8] = a;
    obj_0058[x + 10] = a;
    a = 0xFA;
    obj_009F[x + 9] = a;
    a = 0xFC;
    obj_009F[x + 11] = a;
    a = 0;
    buf_0433[x + 9] = a;
    buf_0433[x + 11] = a;
    obj_006E[x + 10] = obj_006E[x + 8];
    obj_0087[x + 10] = obj_0087[x + 8];
    a = obj_00CF[x + 8];
    a += 8;
    obj_00CF[x + 10] = a;
    a = 0xFA;
    obj_009F[x + 9] = a;
    return;
}

// bank=0 $BE70
void func_00_BE70()
{
    a = obj[x + 8];
    if (a == 0) goto L_BED1;
    a &= 0x0F;
    pha();
    y = a;
    a = x;
    a += 9;
    x = a;
    y--;
    if (y == 0) goto L_BEB3;
    func_00_BFA4();
    func_00_BF0F();
    a = x;
    a += 2;
    x = a;
    func_00_BFA4();
    func_00_BF0F();
    x = zp_08;
    func_F159();
    func_F1B6();
    FlushVramBuf_EC53();
    a = pla();
    // LDY $BE
    if (zero) goto L_BED1;
    pha();
    a = 0xF0;
    if (a < obj_00CF[x + 10])
    {
        obj_00CF[x + 10] = a;
    }
    a = obj_00CF[x + 8];
    zero = a == 0xF0; carry = a >= 0xF0; negative = (byte)(a - 0xF0) >= 0x80;
    a = pla();
    if (!carry) goto L_BED1;
    if (carry) goto L_BECF;
    L_BEB3: ;
    func_00_BFA4();
    x = zp_08;
    func_F159();
    func_F1B6();
    FlushVramBuf_EBD1();
    a = obj_00CF[x + 8];
    a &= 0x0F;
    zero = a == 5; carry = a >= 5; negative = (byte)(a - 5) >= 0x80;
    a = pla();
    if (carry) goto L_BED1;
    a = 1;
    vram_buf[x + 236] = a;
    L_BECF: ;
    a = 0;
    L_BED1: ;
    obj[x + 8] = a;
    return;
}

// bank=0 $BED4
void FlushVramBuf_BED4()
{
    x = 1;
    L_BED6: ;
    zp_08 = 1;
    a = vram_buf[0x01];
    // if (a != 0) goto L_BEFE // label not in this routine
    a = vram_buf[x + 236];
    if (a != 0)
    {
        ptr_06_lo = vram_buf[x + 230];
        ptr_06_hi = 5;
        zp_02 = vram_buf[x + 228];
        y = a;
        a = vram_buf[x + 232];
        poke((ushort)((ptr_06_lo | ptr_06_hi << 8) + y), a);
        func_00_8A61();
        a = 0;
        vram_buf[x + 236] = a;
    }
    x--;
    if (x < 0x80) goto L_BED6;
    return;
}

// bank=0 $BF02
void func_00_BF02()
{
    x++;
    func_00_BF0F();
    x = zp_08;
    return;
}

// bank=0 $BF0F
void func_00_BF0F()
{
    a = obj_0046[x + 17];
    a *= 16;
    vram_ptr_hi = a;
    a = obj_0046[x + 17];
    a >>= 4;
    if (a >= 8) { a |= 0xF0; }
    vram_ptr_lo = a;
    y = 0;
    zero = a == 0; carry = a >= 0; negative = (byte)(a - 0) >= 0x80;
    if (negative) { y--; }
    zp_02 = 0;
    a = peek((ushort)(0x0400 + x));
    carry = false;
    a = (byte)(a + vram_ptr_hi + (carry ? 1 : 0));
    poke((ushort)((ushort)0x0400 + x), a);
    a = 0;
    { bool _c = carry; carry = (a & 0x80) != 0; a = (byte)((a << 1) | (_c ? 1 : 0)); }
    pha();
    { bool _c = carry; carry = (a & 1) != 0; a = (byte)((a >> 1) | (_c ? 0x80 : 0)); }
    a = buf_0086[x];
    a = (byte)(a + vram_ptr_lo + (carry ? 1 : 0));
    buf_0086[x] = a;
    a = buf_006D[x];
    a = (byte)(a + zp_02 + (carry ? 1 : 0));
    buf_006D[x] = a;
    a = pla();
    carry = false;
    a = (byte)(a + vram_ptr_lo + (carry ? 1 : 0));
    return;
}

// bank=0 $BF63
void func_00_BF63()
{
    y = 0x3D;
    a = obj[x];
    if (a == 5) { y = 0x20; }
    func_00_BF94(); return;
}

// bank=0 $BFA4
void func_00_BFA4()
{
    y = 1;
    vram_ptr_lo = 0x50;
    a = peek((ushort)(0xBF9F + y));
}

// bank=0 $BFAD
void func_00_BFAD()
{
    zp_02 = a;
    a = 0;
    func_00_BFD7(); return;
}

// bank=0 $BFD7
void func_00_BFD7()
{
    pha();
    a = peek((ushort)(0x0416 + x));
    carry = false;
    // ADC $0433
    poke((ushort)(ram_0416 + x), a);
    y = 0;
    a = obj_009F[x];
    if (negative) { y--; }
    ptr_06_hi = 0;
    // ADC $CE
    buf_00CE[x] = a;
    a = obj_009F[x + 22];
    a = (byte)(a + ptr_06_hi + (carry ? 1 : 0));
    obj_009F[x + 22] = a;
    a = buf_0433[x];
    carry = false;
    a = (byte)(a + vram_ptr_lo + (carry ? 1 : 0));
    buf_0433[x] = a;
    obj_009F[x] += (byte)(carry ? 1 : 0);
    { byte _v = zp_02; zero = a == _v; carry = a >= _v; negative = (byte)(a - _v) >= 0x80; }
    // if (negative) goto L_C018 // label not in this routine
    a = buf_0433[x];
    if (a >= 0x80)
    {
        obj_009F[x] = zp_02;
        a = 0;
        buf_0433[x] = a;
    }
    a = pla();
    if (a == 0) return;
    a = zp_02;
    a ^= 0xFF;
    y = (byte)(a + 1);
    ptr_06_hi = y;
    a = buf_0433[x];
    carry = true;
    a = (byte)(a - vram_ptr_hi - (carry ? 0 : 1));
    buf_0433[x] = a;
    a = obj_009F[x];
    a = (byte)(a - 0 - (carry ? 0 : 1));
    obj_009F[x] = a;
    { byte _v = ptr_06_hi; zero = a == _v; carry = a >= _v; negative = (byte)(a - _v) >= 0x80; }
    if (!negative) return;
    a = buf_0433[x];
    if (a >= 0x80) return;
    obj_009F[x] = ptr_06_hi;
    a = 0xFF;
    buf_0433[x] = a;
}

// bank=1 $C047
void func_C047()
{
    a = buf_000F[x];
    pha();
    if (!((a & 0x80) != 0))
    {
        a = pla();
        if (a == 0)
        {
        a = game[0x23];
        a &= 7;
        if (a == 7) return;
        func_C0CC(); return;
        }
        else
        {
        func_C882(); return;
        }
    }
    a = pla();
    a &= 0x0F;
    y = a;
    a = buf_000F[y];
    if (a != 0) return;
    buf_000F[x] = a;
}

// bank=1 $C08C
void func_C08C()
{
    a = buf_006D_00;
    carry = true;
    a = (byte)(a - 4 - (carry ? 0 : 1));
    buf_006D_00 = a;
    a = game[0x29];
    carry = true;
    a = (byte)(a - 4 - (carry ? 0 : 1));
    game[0x29] = a;
    a = game[0x1E];
    carry = true;
    a = (byte)(a - 4 - (carry ? 0 : 1));
    game[0x1E] = a;
    a = game[0x1F];
    carry = true;
    a = (byte)(a - 4 - (carry ? 0 : 1));
    game[0x1F] = a;
    a = f_C08C_072A;
    carry = true;
    a = (byte)(a - 4 - (carry ? 0 : 1));
    f_C08C_072A = a;
    game[0x3F] = 0;
    game[0x2F] = 0;
    game[0x3D] = 0;
    game[0x3E] = 0;
    a = peek((ushort)(0x9BF8 + y));
    game[0x30] = a;
    return;
}

// bank=1 $C226
void func_C226()
{
    a = 0;
    obj[x] = a;
    func_C26C();
    return;
}

// bank=1 $C26C
void func_C26C()
{
    L_C26C: ;
    L_C24E: ;
    // ???(5B)
    // ???(C2)
    L_C258: ;
    { byte _m = vram_buf[0xD0]; carry = (_m & 0x80) != 0; vram_buf[0xD0] = (byte)(_m << 1); }
    a = buf_0016[x];
    if (a >= 0x15) goto L_C27F;
    y = a;
    a = obj_00CF[x];
    a = (byte)(a + 8 + (carry ? 1 : 0));
    obj_00CF[x] = a;
    L_C278: ;
    // ???(CF)
    a = 1;
    L_C27A: ;
    // ORA $9D
    vram_buf[x + 216] = a;
    // CLD
    // ???(03)
    a = y;
    L_C27F: ;
    func_00_8E04();
    { byte _m = f_C26C_0EC3; carry = (_m & 0x80) != 0; f_C26C_0EC3 = (byte)(_m << 1); }
    // ???(C3)
    { byte _m = f_C26C_1EC3; carry = (_m & 0x80) != 0; f_C26C_1EC3 = (byte)(_m << 1); }
    L_C288: ;
    // ASL $F0C3
    // ???(C3)
    L_C28A: ;
    if (zero) goto L_C24E;
    // ???(C2)
    L_C28C: ;
    plp();
    // ???(C3)
    // SBC $C2
    // ???(42)
    // ???(C3)
    // ???(6B)
    // ???(C3)
    if (zero) goto L_C258;
    // ADC $C3
    // ADC $C3
    // ???(F7)
    // ???(C2)
    // ???(87)
    // ???(C7)
    // CMP $C7
    carry = (a & 1) != 0; a >>= 1;
    // ???(C3)
    a = (byte)(a & peek((ushort)(0x85C3 + x)));
    // ???(C3)
    y = 0xC7;
    if (y == 0) goto L_C26C;
    y = 0xC7;
    y = 0xC7;
    y = 0xC7;
    y = 0xC7;
    // CLV
    // ???(C7)
    if (zero) goto L_C278;
    if (zero) goto L_C27A;
    // ???(5C)
    // CPY $5C
    // CPY $5C
    // CPY $5C
    // CPY $59
    // CPY $F0
    // ???(C2)
    if (zero) goto L_C288;
    if (zero) goto L_C28A;
    if (zero) goto L_C28C;
    // ???(DF)
    // ???(C7)
    // ???(12)
    y++;
    // ???(3F)
    y++;
    // EOR $C8
    // ???(0B)
    y++;
    // ???(03)
    y++;
    // ???(0B)
    y++;
    // ???(4B)
    y++;
    // ???(57)
    y++;
    a ^= 0xC5;
    return;
}

// bank=1 $C363
void func_C363()
{
    a = 0;
    obj_009F[x + 1] = a;
    buf_0433[x + 1] = a;
    return;
}

// bank=1 $C871
void func_C871()
{
    a = obj_0087[x];
    carry = false;
    // ADC $C86B
    obj_0087[x] = a;
    a = obj_006E[x];
    // ADC $C86E
    obj_006E[x] = a;
    return;
}

// bank=1 $C998
void func_C998()
{
    a = 0;
    buf_000F[x] = a;
    buf_0016[x] = a;
    obj[x] = a;
    buf_0110[x] = a;
    buf_0796[x] = a;
    buf_0110[x + 21] = a;
    vram_buf[x + 197] = a;
    game[x + 142] = a;
    return;
}

// bank=1 $D071
void func_D071()
{
    x = 4;
    do
    {
        func_C998();
        x--;
    }
    while (!negative);
    buf_06BE[0x0D] = a;
    x = zp_08;
    return;
}

// bank=1 $D67A
void func_D67A()
{
    a = buf_0016[x];
    if (a == 0x14) return;
    a = game[0x20];
    // LDY $16
    if (y != 5)
    {
        if (y == 0x0D)
        {
            }
            a = (byte)(a + 0x38 + (carry ? 1 : 0));
        }
    a = (byte)(a - 0x48 - (carry ? 0 : 1));
    vram_ptr_hi = a;
    a = game[0x1E];
    a = (byte)(a - 0 - (carry ? 0 : 1));
    vram_ptr_lo = a;
    a = game[0x21];
    a = (byte)(a + 0x48 + (carry ? 1 : 0));
    zp_03 = a;
    zp_02 = (byte)(game[0x1F] + (carry ? 1 : 0));
    a = obj_0087[x];
    { byte _v = vram_ptr_hi; zero = a == _v; carry = a >= _v; negative = (byte)(a - _v) >= 0x80; }
    a = obj_006E[x];
    a = (byte)(a - vram_ptr_lo - (carry ? 0 : 1));
    if (a < 0x80)
    {
        a = obj_0087[x];
        { byte _v = zp_03; zero = a == _v; carry = a >= _v; negative = (byte)(a - _v) >= 0x80; }
        a = obj_006E[x];
        a = (byte)(a - zp_02 - (carry ? 0 : 1));
        if (a >= 0x80) return;
        a = obj[x];
        if (a == 5) return;
        if (y == 0x0D) return;
        if (y == 0x30) return;
        if (y == 0x31) return;
        if (y == 0x32) return;
    }
    func_C998();
}

// bank=1 $D6D9
void func_D6D9()
{
    a = obj[x + 6];
    if (a == 0) goto L_D733;
    a *= 2;
    if (carry) goto L_D733;
    a = zp_09;
    carry = (a & 1) != 0; a >>= 1;
    if (carry) goto L_D733;
    a = x;
    a *= 4;
    a += 0x1C;
    y = a;
    x = 4;
    L_D6EE: ;
    vram_ptr_hi = 4;
    a = y;
    pha();
    a = obj[x];
    a &= 0x20;
    // if (a != 0) goto L_D72C // label not in this routine
    a = buf_000F[x];
    // if (a == 0) goto L_D72C // label not in this routine
    a = buf_0016[x];
    if (a >= 0x24)
    {
        // if (a < 0x2B) goto L_D72C // label not in this routine
    }
    if (a == 6)
    {
        a = obj[x];
        // if (a >= 2) goto L_D72C // label not in this routine
    }
    a = vram_buf[x + 216];
    if (a == 0)
    {
        a = x;
        a *= 4;
        a += 4;
        x = a;
        func_E327();
        x = zp_08;
        // if (!carry) goto L_D72C // label not in this routine
        a = 0x80;
        obj[x + 6] = a;
        x = vram_ptr_hi;
        func_D73E();
    }
    a = pla();
    y = a;
    x = vram_ptr_hi;
    x--;
    if (x < 0x80) goto L_D6EE;
    L_D733: ;
    x = zp_08;
    return;
}

// bank=1 $D73E
void func_D73E()
{
    func_F152();
    x = vram_ptr_hi;
    a = buf_000F[x];
    if (a >= 0x80)
    {
        a &= 0x0F;
        x = a;
        a = buf_0016[x];
        if (a != 0x2D)
        {
            x = vram_ptr_hi;
            }
            a = buf_0016[x];
            if (a == 2) return;
            if (a == 0x2D)
            {
                }
                f_D73E_0483--;
                if (f_D73E_0483 != 0) return;
                func_C363();
                obj_0058[x] = a;
                buf_06BE[0x0D] = a;
                a = 0xFE;
                obj_009F[x + 1] = a;
                y = game[0x63];
                a = peek((ushort)(0xD736 + y));
                buf_0016[x] = a;
                a = 0x20;
                if (y < 3) { a |= 3; }
                obj[x] = a;
                zp_FE = 0x80;
                x = vram_ptr_hi;
                a = 9;
                if (a == 0)
                {
                    }
                    if (a == 8) return;
                    if (a == 0x0C) return;
                    if (a >= 0x15) return;
                }
    func_DA11();
    zp_FF = 8;
}

// bank=1 $D7C4
void func_D7C4()
{
    a = zp_09;
    if ((a & 1) == 0) return;
    a = game[0x4B];
    a = (byte)(a | vram_buf[0xD6]);
    if (a != 0) return;
    a = x;
    a *= 4;
    a += 0x24;
    y = a;
    func_E325();
    x = zp_08;
    if (!carry)
    {
        a = 0;
        buf_06BE[x] = a;
    }
    else
    {
        a = buf_06BE[x];
        if (a != 0) return;
        a = 1;
        buf_06BE[x] = a;
        a = obj_0058[x + 12];
        a ^= 0xFF;
        a += 1;
        obj_0058[x + 12] = a;
        a = ram_079F;
        if (a != 0) return;
        func_D92C(); return;
    }
}

// bank=1 $D853
void FlushVramBuf_D853()
{
    do
    {
        return;
        a = zp_09;
        carry = (a & 1) != 0; a >>= 1;
    }
    while (carry);
    func_DC41();
    // if (carry) goto L_D880 // label not in this routine
    a = vram_buf[x + 216];
    // if (a != 0) goto L_D880 // label not in this routine
    a = zp_0E;
    // if (a != 8) goto L_D880 // label not in this routine
    a = obj[x];
    a &= 0x20;
    if (a == 0)
    {
        func_DC52();
        func_E325();
        x = zp_08;
        if (!(carry))
        {
            a = buf_0491[x];
            a &= 0xFE;
            buf_0491[x] = a;
            }
            return;
        }
    // LDY $16
    if (y == 0x2E) { func_D800(); return; }
    a = ram_079F;
    if (a != 0) { func_D795(); return; }
    a = buf_0491[x];
    a &= 1;
    a = (byte)(a | vram_buf[x + 216]);
    // if (a != 0) goto L_D8F8 // label not in this routine
    a = 1;
    a = (byte)(a | buf_0491[x]);
    buf_0491[x] = a;
    if (y == 0x12) goto L_D8F9;
    if (y == 0x0D) { func_D92C(); return; }
    if (y == 0x0C) { func_D92C(); return; }
    if (y == 0x33) goto L_D8F9;
    if (y >= 0x15) { func_D92C(); return; }
    a = game[0x52];
    if (a == 0) { func_D92C(); return; }
    a = obj[x];
    if ((a & 0x80) != 0) goto L_D8F9;
    a = obj[x];
    a &= 7;
    if (a < 2) goto L_D8F9;
    a = buf_0016[x];
    if (a != 6)
    {
        zp_FF = 8;
        a = obj[x];
        a |= 0x80;
        obj[x] = a;
        func_DA05();
        a = peek((ushort)(0xD84F + y));
        obj_0058[x] = a;
        a = 3;
        carry = false;
        a = (byte)(a + ram_0484 + (carry ? 1 : 0));
        // LDY $0796
        if (y < 3) { a = peek((ushort)(0xD892 + y)); }
        func_DA11();
    }
    return;
    L_D8F9: ;
    a = obj_009F_00;
    if (a < 0x80)
    {
        if (!zero) goto L_D969;
    }
    a = buf_0016[x];
    if (a >= 7)
    {
        a = buf_00CE_00;
        a += 0x0C;
        if (a < obj_00CF[x]) goto L_D969;
    }
    a = ram_0791;
    if (a != 0) goto L_D969;
    a = ram_079E;
    if (a != 0) goto L_D955;
    a = vram_buf[0xAD];
    if (a >= vram_buf[0xAE])
    {
        func_D9F6(); return;
    }
    a = obj_0046[x];
    if (a != 1) { func_D92C(); return; }
    func_D9FF(); return;
    L_D955: ;
    x = zp_08;
    return;
    L_D969: ;
    a = buf_0016[x];
    if (a == 0x12) { func_D92C(); return; }
    zp_FF = 4;
    a = buf_0016[x];
    y = 0;
    // if (a == 0x14) goto L_D996 // label not in this routine
    // if (a == 8) goto L_D996 // label not in this routine
    // if (a == 0x33) goto L_D996 // label not in this routine
    // if (a == 0x0C) goto L_D996 // label not in this routine
    y++;
    // if (a == 5) goto L_D996 // label not in this routine
    y++;
    if (a != 0x11)
    {
        y++;
        if (a == 7)
        {
            }
            a = peek((ushort)(0xD965 + y));
            func_DA11();
            a = obj_0046[x];
            pha();
            func_E02F();
            a = pla();
            obj_0046[x] = a;
            a = 0x20;
            obj[x] = a;
            func_C363();
            obj_0058[x] = a;
            obj_009F_00 = 0xFD;
            return;
        }
    if (a >= 9)
    {
        a &= 1;
        buf_0016[x] = a;
        y = 0;
        // STY $1E
        a = 3;
        func_DA11();
        func_C363();
        func_DA05();
        a = peek((ushort)(0xD851 + y));
        obj_0058[x] = a;
        func_D9F1(); return;
    }
    a = 4;
    obj[x] = a;
    ram_0484++;
    a = ram_0484;
    carry = false;
    a = (byte)(a + ram_0791 + (carry ? 1 : 0));
    func_DA11();
    ram_0791++;
    y = game[0x6E];
    a = peek((ushort)(0xD9D2 + y));
    buf_0796[x] = a;
}

// bank=1 $D931
void func_D931()
{
    x = game[0x5A];
    if (x == 0) goto L_D958;
    game[0x5A] = a;
    ram_079E = 8;
    a *= 2;
    zp_FF = a;
    FlushVramBuf_85F1();
    a = 0x0A;
    L_D946: ;
    y = 1;
    L_D958: ;
    obj_0046[0x11] = x;
    x++;
    zp_FC = x;
    obj_009F_00 = 0xFC;
    a = 0x0B;
    if (a != 0) goto L_D946;
    // ???(02)
    { byte _m = ptr_04_hi; carry = (_m & 0x80) != 0; ptr_04_hi = (byte)(_m << 1); }
    { byte _m = obj_009F_16; carry = (_m & 0x80) != 0; obj_009F_16 = (byte)(_m << 1); }
    // ASL $C9
    // ???(12)
    if (zero) { func_D92C(); return; }
    zp_FF = 4;
    a = buf_0016[x];
    y = 0;
    // if (a == 0x14) goto L_D996 // label not in this routine
    // if (a == 8) goto L_D996 // label not in this routine
    // if (a == 0x33) goto L_D996 // label not in this routine
    // if (a == 0x0C) goto L_D996 // label not in this routine
    y++;
    // if (a == 5) goto L_D996 // label not in this routine
    y++;
    if (a != 0x11)
    {
        y++;
        if (a == 7)
        {
            }
            a = peek((ushort)(0xD965 + y));
            func_DA11();
            a = obj_0046[x];
            pha();
            func_E02F();
            a = pla();
            obj_0046[x] = a;
            a = 0x20;
            obj[x] = a;
            func_C363();
            obj_0058[x] = a;
            obj_009F_00 = 0xFD;
            return;
        }
    if (a >= 9)
    {
        a &= 1;
        buf_0016[x] = a;
        y = 0;
        // STY $1E
        a = 3;
        func_DA11();
        func_C363();
        func_DA05();
        a = peek((ushort)(0xD851 + y));
        obj_0058[x] = a;
        func_D9F1(); return;
    }
    a = 4;
    obj[x] = a;
    ram_0484++;
    a = ram_0484;
    carry = false;
    a = (byte)(a + ram_0791 + (carry ? 1 : 0));
    func_DA11();
    ram_0791++;
    y = game[0x6E];
    a = peek((ushort)(0xD9D2 + y));
    buf_0796[x] = a;
}

// bank=1 $D948
void func_D948()
{
    zp_0E = a;
    zp_1D = y;
    game[0x4B] = 0xFF;
    y++;
    game[0x79] = 0xFF;
    x = zp_08;
    return;
}

// bank=1 $DA05
void func_DA05()
{
    y = 1;
    func_E143();
    if (negative) { y++; }
    // STY $46
    y--;
    return;
}

// bank=1 $DA11
void func_DA11()
{
    buf_0110[x] = a;
    a = 0x30;
    buf_0110[x + 28] = a;
    buf_0110[x + 14] = obj_00CF[x];
    buf_0110[x + 7] = vram_buf[0xAE];
    return;
}

// bank=1 $DB1C
void func_DB1C()
{
    a = buf_0016[x];
    if (a == 0x0D) return;
    if (a == 0x11) return;
    if (a == 5) return;
    // if (a == 0x12) goto L_DB36 // label not in this routine
    if (a != 0x0E)
    {
        if (a >= 7) return;
    }
    a = obj_0058[x];
    a ^= 0xFF;
    y = (byte)(a + 1);
    // STY $58
    a = obj_0046[x];
    a ^= 3;
    obj_0046[x] = a;
}

// bank=1 $DC41
void func_DC41()
{
    a = vram_buf[0xD0];
    if (a >= 0xF0) return;
    y = obj_009F_16;
    y--;
    if (y != 0) return;
    a = buf_00CE_00;
    zero = a == 0xD0; carry = a >= 0xD0; negative = (byte)(a - 0xD0) >= 0x80;
}

// bank=1 $DC52
void func_DC52()
{
    a = zp_08;
    a *= 4;
    a += 4;
    y = a;
    a = vram_buf[0xD1];
    a &= 0x0F;
    zero = a == 0x0F; carry = a >= 0x0F; negative = (byte)(a - 0x0F) >= 0x80;
    return;
}

// bank=1 $DC64
void func_DC64()
{
    a = game[0x1A];
    // if (a != 0) goto L_DC97 // label not in this routine
    a = zp_0E;
    // if (a == 0x0B) goto L_DC97 // label not in this routine
    // if (a < 4) goto L_DC97 // label not in this routine
    a = 1;
    y = game[0x08];
    if (y == 0)
    {
        a = zp_1D;
        if (a != 0)
        {
        if (a == 3)
        {
            }
            a = 2;
            }
            zp_1D = 2;
        }
    a = obj_009F_16;
    if (a == 1)
    {
        ram_0490 = 0xFF;
        a = buf_00CE_00;
        if (a >= 0xCF)
        {
            }
            return;
        }
    y = 2;
    a = game[0x18];
    // if (a != 0) goto L_DCAB // label not in this routine
    a = game[0x58];
    // if (a != 0) goto L_DCAB // label not in this routine
    y--;
    a = game[0x08];
    if (a == 0) { y--; }
    a = peek((ushort)(0xE3AD + y));
    zp_EB = a;
    y = a;
    x = game[0x58];
    a = game[0x18];
    if (a != 0) { x++; }
    a = buf_00CE_00;
    { byte _v = peek((ushort)(0xDC62 + x)); zero = a == _v; carry = a >= _v; negative = (byte)(a - _v) >= 0x80; }
    if (!carry) { func_DCF6(); return; }
    func_E3E9();
    if (zero) { func_DCF6(); return; }
    func_DFA1();
    if (!(carry))
    {
        y = obj_009F_00;
        if (!negative) { func_DCF6(); return; }
        y = ptr_04_lo;
        if (y < 4) { func_DCF6(); return; }
        func_DF8F();
        if (!carry)
        {
        y = game[0x52];
        // if (y == 0) goto L_DCF2 // label not in this routine
        y = game[0x88];
        // if (y != 0) goto L_DCF2 // label not in this routine
        FlushVramBuf_BCED();
        func_DCF6(); return;
        }
        if (a != 0x26) { zp_FF = 2; }
        obj_009F_00 = 1;
    }
    func_DE05(); return;
}

// bank=1 $DE1C
void func_DE1C()
{
    y = zp_02;
    a = 0;
    poke((ushort)((ptr_06_lo | ptr_06_hi << 8) + y), a);
    func_00_8A4D(); return;
}

// bank=1 $DEBD
void func_DEBD()
{
    if (a == 0x5F) return;
    zero = a == 0x60; carry = a >= 0x60; negative = (byte)(a - 0x60) >= 0x80;
}

// bank=1 $DEC4
void func_DEC4()
{
    func_DEDD();
    if (!carry) return;
    game[0x0D] = 0x70;
    buf_06BE[0x1D] = 0xF9;
    game[0x8A] = 3;
    carry = (a & 1) != 0; a >>= 1;
    game[0x12] = a;
}

// bank=1 $DEDD
void func_DEDD()
{
    if (a != 0x67)
    {
        zero = a == 0x68; carry = a >= 0x68; negative = (byte)(a - 0x68) >= 0x80;
        carry = false;
        if (!zero) return;
    }
    carry = true;
}

// bank=1 $DEE8
void func_DEE8()
{
    a = zp_0B;
    a &= 4;
    if (a == 0) return;
    a = vram_ptr_lo;
    if (a != 0x11) return;
    a = vram_ptr_hi;
    if (a != 0x10) return;
    buf_06BE[0x20] = 0x30;
    zp_0E = 3;
    zp_FF = 0x10;
    vram_buf[0xC4] = 0x20;
    a = buf_06BE[0x18];
    if (a == 0) return;
    a &= 3;
    a *= 4;
    x = a;
    a = buf_0086_00;
    // if (a < 0x60) goto L_DF22 // label not in this routine
    x++;
    if (a >= 0xA0) { x++; }
    // LDY $87F2
    y--;
    game[0x63] = y;
    // LDX $9CB4
    a = peek((ushort)(0x9CBC + x));
    game[0x54] = a;
    zp_FC = 0x80;
    game[0x55] = 0;
    game[0x64] = 0;
    game[0x60] = 0;
    game[0x56] = 0;
    game[0x61]++;
    game[0x5B]++;
}

// bank=1 $DF4B
void func_DF4B()
{
    a = 0;
    y = obj_0046[0x11];
    x = vram_ptr_lo;
    x--;
    if (x == 0)
    {
        x++;
        zero = y == 0; carry = y >= 0;
        if (negative) goto L_DF81;
        a = 0xFF;
        func_DF66(); return;
    }
    x = 2;
    zero = y == 1; carry = y >= 1;
    if (!negative) goto L_DF81;
    a = 1;
    L_DF81: ;
    a = x;
    a ^= 0xFF;
    a = (byte)(a & ram_0490);
    ram_0490 = a;
    return;
}

// bank=1 $DF8F
void func_DF8F()
{
    func_DFB0();
    { byte _v = peek((ushort)(0xDF8B + x)); zero = a == _v; carry = a >= _v; negative = (byte)(a - _v) >= 0x80; }
    return;
}

// bank=1 $DF9A
void func_DF9A()
{
    func_DFB0();
    { byte _v = peek((ushort)(0xDF96 + x)); zero = a == _v; carry = a >= _v; negative = (byte)(a - _v) >= 0x80; }
    return;
}

// bank=1 $DFA1
void func_DFA1()
{
    // if (a == 0xC2) goto L_DFAB // label not in this routine
    if (a != 0xC3)
    {
        carry = false;
        return;
    }
    zp_FE = 1;
    return;
}

// bank=1 $DFB0
void func_DFB0()
{
    y = a;
    a &= 0xC0;
    a *= 2;
    { bool _c = carry; carry = (a & 0x80) != 0; a = (byte)((a << 1) | (_c ? 1 : 0)); }
    { bool _c = carry; carry = (a & 0x80) != 0; a = (byte)((a << 1) | (_c ? 1 : 0)); }
    x = a;
    a = y;
    return;
}

// bank=1 $E01B
void func_E01B()
{
    if (a < 9) { func_E02F(); return; }
    if (a >= 0x11) { func_E02F(); return; }
    if (a >= 0x0A)
    {
        if (a < 0x0D) { func_E02F(); return; }
    }
    a &= 1;
    buf_0016[x] = a;
}

// bank=1 $E02F
void func_E02F()
{
    a = obj[x];
    a &= 0xF0;
    a |= 2;
    obj[x] = a;
    // DEC $CF
    // DEC $CF
    a = buf_0016[x];
    if (a != 7)
    {
        a = 0xFD;
        y = game[0x52];
        if (y == 0)
        {
            }
            a = 0xFF;
        }
    obj_009F[x + 1] = a;
    y = 1;
    func_E143();
    if (negative) { y++; }
    a = buf_0016[x];
    // if (a == 0x33) goto L_E060 // label not in this routine
    if (a != 8)
    {
        // STY $46
    }
    y--;
    a = peek((ushort)(0xDFBF + y));
    obj_0058[x] = a;
    return;
}

// bank=1 $E143
void func_E143()
{
    a = obj_0087[x];
    carry = true;
    a = (byte)(a - buf_0086_00 - (carry ? 0 : 1));
    vram_ptr_lo = a;
    a = obj_006E[x];
    a = (byte)(a - buf_006D_00 - (carry ? 0 : 1));
    return;
}

// bank=1 $E1B5
void func_E1B5()
{
    if (a == 0x26) return;
    if (a == 0xC2) return;
    if (a == 0xC3) return;
    if (a == 0x5F) return;
    zero = a == 0x60; carry = a >= 0x60; negative = (byte)(a - 0x60) >= 0x80;
}

// bank=1 $E1C8
void func_E1C8()
{
    a = obj_00CF[x + 6];
    if (a >= 0x18)
    {
        func_E39C();
        // if (zero) goto L_E1EF // label not in this routine
        func_E1B5();
        // if (zero) goto L_E1EF // label not in this routine
        a = obj_009F[x + 7];
        if (a >= 0x80) goto L_E1F4;
        a = obj_003A[x];
        if (a != 0) goto L_E1F4;
        a = 0xFD;
        obj_009F[x + 7] = a;
        a = 1;
        obj_003A[x] = a;
        a = obj_00CF[x + 6];
        a &= 0xF8;
        obj_00CF[x + 6] = a;
        return;
    }
    a = 0;
    obj_003A[x] = a;
    return;
    L_E1F4: ;
    a = 0x80;
    obj[x + 6] = a;
    zp_FF = 2;
    return;
}

// bank=1 $E22D
void func_E22D()
{
    a = x;
    a += 7;
    x = a;
    y = 2;
    func_E29C();
    func_E2DE(); return;
}

// bank=1 $E236
void func_E236()
{
    a = x;
    a += 9;
    x = a;
    y = 6;
    func_E29C();
    func_E2DE(); return;
}

// bank=1 $E243
void func_E243()
{
    vram_ptr_lo = 0x48;
    y = 0x44;
    func_E252(); return;
}

// bank=1 $E29C
void func_E29C()
{
    vram_ptr_lo = x;
    zp_02 = vram_buf[y + 184];
    vram_ptr_hi = vram_buf[y + 173];
    a = x;
    a *= 4;
    pha();
    y = a;
    a = buf_0491[x + 8];
    a *= 4;
    x = a;
    a = vram_ptr_hi;
    carry = false;
    // ADC $E1FD
    buf_04AC[y] = a;
    a = vram_ptr_hi;
    carry = false;
    // ADC $E1FF
    buf_04AC[y + 2] = a;
    x++;
    y++;
    a = zp_02;
    carry = false;
    // ADC $E1FD
    buf_04AC[y] = a;
    a = zp_02;
    carry = false;
    // ADC $E1FF
    buf_04AC[y + 2] = a;
    a = pla();
    y = a;
    x = vram_ptr_lo;
    return;
}

// bank=1 $E325
void func_E325()
{
    x = 0;
}

// bank=1 $E327
void func_E327()
{
    ptr_06_lo = y;
    ptr_06_hi = 1;
    L_E32D: ;
    a = buf_04AC[y];
    if (a >= buf_04AC[x]) goto L_E35F;
    if (a >= buf_04AC[x + 2])
    {
        if (zero) goto L_E37E;
        a = buf_04AC[y + 2];
        if (a < buf_04AC[y]) goto L_E37E;
        if (a >= buf_04AC[x]) goto L_E37E;
        y = ptr_06_lo;
        return;
    }
    a = buf_04AC[x + 2];
    if (a < buf_04AC[x]) goto L_E37E;
    a = buf_04AC[y + 2];
    if (a >= buf_04AC[x]) goto L_E37E;
    y = ptr_06_lo;
    return;
    L_E35F: ;
    if (a == buf_04AC[x]) goto L_E37E;
    if (a < buf_04AC[x + 2]) goto L_E37E;
    if (zero) goto L_E37E;
    if (a < buf_04AC[y + 2]) goto L_E37A;
    if (zero) goto L_E37A;
    a = buf_04AC[y + 2];
    if (a >= buf_04AC[x]) goto L_E37E;
    L_E37A: ;
    carry = false;
    y = ptr_06_lo;
    return;
    L_E37E: ;
    x++;
    y++;
    ptr_06_hi--;
    if (!negative) goto L_E32D;
    carry = true;
    y = ptr_06_lo;
    return;
}

// bank=1 $E39C
void func_E39C()
{
    y = 0x1A;
    a = x;
    a += 7;
    x = a;
    a = 0;
    func_E3F0();
    x = zp_08;
    zero = a == 0; carry = a >= 0; negative = (byte)(a - 0) >= 0x80;
    return;
}

// bank=1 $E3E8
void func_E3E8()
{
    y++;
}

// bank=1 $E3E9
void func_E3E9()
{
    a = 0;
    // BIT $01A9
    x = 0;
}

// bank=1 $E3EC
void func_E3EC()
{
    a = 1;
    x = 0;
}

// bank=1 $E3F0
void func_E3F0()
{
    pha();
    ptr_04_lo = y;
    a = peek((ushort)(0xE3B0 + y));
    carry = false;
    // ADC $86
    ptr_04_hi = a;
    a = buf_006D[x];
    a += (byte)(carry ? 1 : 0);
    a &= 1;
    carry = (a & 1) != 0; a >>= 1;
    a = (byte)(a | ptr_04_hi);
    { bool _c = carry; carry = (a & 1) != 0; a = (byte)((a >> 1) | (_c ? 0x80 : 0)); }
    a >>= 3;
    func_00_9BE1();
    y = ptr_04_lo;
    a = buf_00CE[x];
    carry = false;
    // ADC $E3CC
    a &= 0xF0;
    carry = true;
    a = (byte)(a - 0x20 - (carry ? 0 : 1));
    zp_02 = a;
    y = a;
    a = peek((ushort)((ptr_06_lo | ptr_06_hi << 8) + y));
    zp_03 = a;
    y = ptr_04_lo;
    a = pla();
    if (a != 0)
    {
        a = buf_0086[x];
    }
    else
    {
        a = buf_00CE[x];
        func_E42B(); return;
    }
}

// bank=1 $E4DC
void ClearOam_E4DC()
{
    // LDY $06F3
    a = game[0x4B];
    if (a == 0)
    {
        a = obj[x + 12];
        a &= 0x7F;
        if (a != 1)
        {
            }
            x = 0;
            if (x != 0)
            {
                }
                a = zp_09;
                a >>= 2;
                a &= 3;
                x = a;
            }
    a = vram_buf[0xBE];
    carry = false;
    // ADC $E4C4
    oam_y[y] = a;
    carry = false;
    // ADC $E4CC
    oam_y[y + 4] = a;
    a = vram_buf[0xB3];
    carry = false;
    // ADC $E4C0
    oam_x[y] = a;
    carry = false;
    // ADC $E4C8
    oam_x[y + 4] = a;
    a = peek((ushort)(0xE4D0 + x));
    oam_tile[y] = a;
    a = peek((ushort)(0xE4D4 + x));
    oam_tile[y + 4] = a;
    a = peek((ushort)(0xE4D8 + x));
    oam_attr[y] = a;
    oam_attr[y + 4] = a;
    x = zp_08;
    a = vram_buf[0xD6];
    a &= 0xFC;
    if (a == 0) return;
    a = 0;
    obj[x + 12] = a;
    a = 0xF8;
    ClearOam_E5C1();
}

// bank=1 $E54B
void func_E54B()
{
    // LDY $06E5
    oam_x[y] = vram_buf[0xAE];
    a += 8;
    oam_x[y + 4] = a;
    oam_x[y + 8] = a;
    a += 0x0C;
    ptr_04_hi = a;
    a = obj_00CF[x];
    ClearOam_E5C1();
    a = (byte)(a + 8 + (carry ? 1 : 0));
    oam_y[y + 8] = a;
    zp_02 = ram_010D;
    zp_03 = 1;
    ptr_04_lo = 1;
    oam_attr[y] = a;
    oam_attr[y + 4] = a;
    oam_attr[y + 8] = a;
    a = 0x7E;
    oam_tile[y] = a;
    oam_tile[y + 8] = a;
    a = 0x7F;
    oam_tile[y + 4] = a;
    a = game[0x13];
    if (a != 0)
    {
        a = y;
        a += 0x0C;
        y = a;
        a = ram_010F;
        a *= 2;
        x = a;
        a = peek((ushort)(0xE541 + x));
        vram_ptr_lo = a;
        a = peek((ushort)(0xE542 + x));
        func_EBB2();
    }
    x = zp_08;
    // LDY $06E5
    a = vram_buf[0xD1];
    a &= 0x0E;
    if (a == 0) return;
    a = 0xF8;
}

// bank=1 $E5B5
void func_E5B5()
{
    oam_y[y + 20] = a;
    oam_y[y + 16] = a;
}

// bank=1 $E5BB
void func_E5BB()
{
    oam_y[y + 12] = a;
    oam_y[y + 8] = a;
}

// bank=1 $E5C1
void ClearOam_E5C1()
{
    oam_y[y + 4] = a;
    oam_y[y] = a;
    return;
}

// bank=1 $E686
void func_E686()
{
    L_E655: ;
    a = zp_09;
    if ((a & 1) == 0)
    {
        // DEC $DB
    }
    a = obj_00CF[x + 12];
    ClearOam_E5C1();
    oam_x[y] = vram_buf[0xB3];
    a += 8;
    oam_x[y + 4] = a;
    a = 2;
    oam_attr[y] = a;
    oam_attr[y + 4] = a;
    a = 0xF7;
    oam_tile[y] = a;
    a = 0xFB;
    oam_tile[y + 4] = a;
    func_E6BD(); return;
    // LDY $06F3
    a = obj[x + 12];
    if (a >= 2) goto L_E655;
    oam_y[y] = obj_00CF[x + 12];
    a += 8;
    oam_y[y + 4] = a;
    oam_x[y] = vram_buf[0xB3];
    oam_x[y + 4] = a;
    a = zp_09;
    carry = (a & 1) != 0; a >>= 1;
    a &= 3;
    x = a;
    a = peek((ushort)(0xE682 + x));
    y++;
    ClearOam_E5C1();
    y--;
    a = 2;
    oam_attr[y] = a;
    a = 0x82;
    oam_attr[y + 4] = a;
    x = zp_08;
}

// bank=1 $EBAA
void func_EBAA()
{
    a = peek((ushort)(0xE73E + x));
    vram_ptr_lo = a;
    a = peek((ushort)(0xE73F + x));
}

// bank=1 $EBB2
void func_EBB2()
{
    vram_ptr_hi = a;
    func_F282(); return;
}

// bank=1 $EBB7
void func_EBB7()
{
    carry = false;
    // ADC $06E5
    y = a;
    a = 0xF8;
    ClearOam_E5C1(); return;
}

// bank=1 $EBC1
void func_EBC1()
{
    carry = false;
    // ADC $06E5
    y = a;
    ClearOam_EC4A();
    oam_y[y + 16] = a;
    return;
}

// bank=1 $EBD1
void FlushVramBuf_EBD1()
{
    zp_02 = vram_buf[0xBC];
    ptr_04_hi = vram_buf[0xB1];
    ptr_04_lo = 3;
    carry = (a & 1) != 0; a >>= 1;
    zp_03 = a;
    // LDY $06EC
    x = 0;
    do
    {
        a = peek((ushort)(0xEBCD + x));
        vram_ptr_lo = a;
        a = peek((ushort)(0xEBCE + x));
        func_EBB2();
    }
    while (x != 4);
    x = zp_08;
    // LDY $06EC
    a = game[0x52];
    if (a != 1)
    {
        a = 0x86;
        oam_tile[y] = a;
        oam_tile[y + 4] = a;
    }
    a = vram_buf[x + 232];
    if (a == 0xC4)
    {
        a = 0x87;
        y++;
        func_E5BB();
        y--;
        a = 3;
        x = game[0x52];
        x--;
        if (x != 0) { carry = (a & 1) != 0; a >>= 1; }
        x = zp_08;
        oam_attr[y] = a;
        a |= 0x40;
        oam_attr[y + 4] = a;
        a |= 0x80;
        oam_attr[y + 12] = a;
        a &= 0x83;
        oam_attr[y + 8] = a;
    }
    a = vram_buf[0xD4];
    pha();
    a &= 4;
    if (a != 0)
    {
        a = 0xF8;
        oam_y[y + 4] = a;
        oam_y[y + 12] = a;
    }
    a = pla();
}

// bank=1 $EC46
void func_EC46()
{
    a &= 8;
    if (a == 0) return;
}

// bank=1 $EC4A
void ClearOam_EC4A()
{
    a = 0xF8;
    oam_y[y] = a;
    oam_y[y + 8] = a;
    return;
}

// bank=1 $EC53
void FlushVramBuf_EC53()
{
    vram_ptr_lo = 2;
    a = 0x75;
    y = zp_0E;
    if (y != 5)
    {
        vram_ptr_lo = 3;
        a = 0x84;
    }
    // LDY $06EC
    y++;
    func_E5BB();
    a = zp_09;
    a *= 16;
    a &= 0xC0;
    a = (byte)(a | vram_ptr_lo);
    y++;
    func_E5BB();
    y--;
    y--;
    a = vram_buf[0xBC];
    ClearOam_E5C1();
    oam_x[y] = vram_buf[0xB1];
    a = vram_buf[x + 241];
    carry = true;
    // SBC $071C
    vram_ptr_lo = a;
    carry = true;
    // SBC $03B1
    a = (byte)(a + vram_ptr_lo + (carry ? 1 : 0));
    a = (byte)(a + 6 + (carry ? 1 : 0));
    oam_x[y + 4] = a;
    oam_y[y + 8] = vram_buf[0xBD];
    oam_y[y + 12] = a;
    oam_x[y + 8] = vram_buf[0xB2];
    a = vram_ptr_lo;
    carry = true;
    // SBC $03B2
    a = (byte)(a + vram_ptr_lo + (carry ? 1 : 0));
    a = (byte)(a + 6 + (carry ? 1 : 0));
    oam_x[y + 12] = a;
    a = vram_buf[0xD4];
    func_EC46();
    a = vram_buf[0xD4];
    if ((a & 0x80) != 0)
    {
        a = 0xF8;
        ClearOam_E5C1();
    }
    a = vram_ptr_lo;
    if (a < 0x80) return;
    a = oam_x[y];
    if (a < oam_x[y + 4]) return;
    a = 0xF8;
    oam_y[y + 4] = a;
    oam_y[y + 12] = a;
}

// bank=1 $EDE1
void func_EDE1()
{
    y = obj_009F_16;
    y--;
    if (y != 0) return;
    a = vram_buf[0xD3];
    a &= 8;
    if (a != 0) return;
    // LDY $06EE
    oam_x[y] = vram_buf[0xB0];
    oam_y[y] = vram_buf[0xBB];
    a = 0x74;
    oam_tile[y] = a;
    a = 2;
    oam_attr[y] = a;
}

// bank=1 $EEE9
void func_EEE9()
{
    a = ram_079E;
    if (a != 0)
    {
        a = zp_09;
        carry = (a & 1) != 0; a >>= 1;
        if (carry) goto L_EF33;
    }
    a = zp_0E;
    if (a == 0x0B) goto L_EF40;
    a = game[0x0F];
    if (a != 0) goto L_EF3A;
    y = game[0x08];
    if (y == 0) { func_EF34(); return; }
    a = zp_1D;
    if (a == 0) { func_EF34(); return; }
    func_EF34();
    a = zp_09;
    a &= 4;
    if (a != 0) goto L_EF33;
    x = a;
    y = buf_06BE_26;
    a = zp_33;
    if ((a & 1) == 0)
    {
        y++;
        y++;
        y++;
        y++;
    }
    a = game[0x58];
    if (a != 0)
    {
        a = oam_tile[y + 24];
        if (a == peek((ushort)0xEEB5)) goto L_EF33;
        x++;
    }
    a = peek((ushort)(0xEEE7 + x));
    oam_tile[y + 24] = a;
    L_EF33: ;
    return;
    L_EF3A: ;
    func_F0B0();
    func_EF45(); return;
    L_EF40: ;
    y = 0x0E;
    a = rom_EE07[y];
}

// bank=1 $EF34
void func_EF34()
{
    func_EFEC();
    func_EF45(); return;
}

// bank=1 $EFBE
void func_EFBE()
{
    ptr_06_hi = a;
    game[0x59] = vram_buf[0xAD];
    ptr_04_hi = a;
    zp_02 = vram_buf[0xB8];
    zp_03 = zp_33;
    ptr_04_lo = vram_buf[0xC4];
    x = buf_06BE[0x17];
    y = buf_06BE_26;
    do
    {
        vram_ptr_lo = rom_EE07[x + 16];
        a = rom_EE07[x + 17];
        func_EBB2();
    }
    while (--ptr_06_hi != 0);
    return;
}

// bank=1 $EFEC
void func_EFEC()
{
    a = zp_1D;
    if (a == 3) goto L_F044;
    if (a == 2) goto L_F034;
    if (a == 1)
    {
        a = game[0x08];
        if (a != 0) goto L_F050;
        y = 6;
        a = game[0x18];
        if (a != 0) { func_F028(); return; }
        y = 0;
        func_F028(); return;
    }
    y = 6;
    a = game[0x18];
    if (a != 0) { func_F028(); return; }
    y = 2;
    a = obj_0046[0x11];
    a = (byte)(a | zp_0C);
    if (a == 0) { func_F028(); return; }
    a = game[0x04];
    if (a < 9) goto L_F03C;
    a = zp_45;
    a = (byte)(a & zp_33);
    if (a != 0) goto L_F03C;
    y++;
    L_F034: ;
    y = 4;
    func_F091();
    func_F062(); return;
    L_F03C: ;
    y = 4;
    func_F091();
    func_F068(); return;
    L_F044: ;
    y = 5;
    a = obj_009F_00;
    if (a == 0) { func_F028(); return; }
    func_F091();
    func_F06D(); return;
    L_F050: ;
    y = 1;
    func_F091();
    a = game[0x86];
    a = (byte)(a | game[0x11]);
    if (a != 0) { func_F068(); return; }
    a = zp_0A;
    a *= 2;
    if (carry) { func_F068(); return; }
}

// bank=1 $F062
void func_F062()
{
    a = game[0x11];
    func_F0D0(); return;
}

// bank=1 $F091
void func_F091()
{
    a = game[0x58];
    if (a == 0) return;
    a = y;
    a += 8;
    y = a;
}

// bank=1 $F0B0
void func_F0B0()
{
    y = game[0x11];
    a = zp_09;
    a &= 3;
    if (a == 0)
    {
        y++;
        if (y >= 0x0A) { game[0x0F] = 0; }
        game[0x11] = 0;
    }
    a = game[0x58];
    if (a == 0)
    {
        a = peek((ushort)(0xF09C + y));
        y = 0x0F;
    }
    a = y;
    a += 0x0A;
    x = a;
    y = 9;
    a = peek((ushort)(0xF09C + x));
    if (a == 0) { y = 1; }
    a = rom_EE07[y];
    return;
}

// bank=1 $F0E9
void func_F0E9()
{
    y = buf_06BE_26;
    a = zp_0E;
    if (a != 0x0B)
    {
        a = buf_06BE[0x17];
        if (a == 0x50) goto L_F117;
        if (a == 0xB8) goto L_F117;
        if (a == 0xC0) goto L_F117;
        if (a != 0xC8) return;
    }
    a = oam_attr[y + 16];
    a &= 0x3F;
    oam_attr[y + 16] = a;
    a = oam_attr[y + 20];
    a &= 0x3F;
    a |= 0x40;
    oam_attr[y + 20] = a;
    L_F117: ;
    a = oam_attr[y + 24];
    a &= 0x3F;
    oam_attr[y + 24] = a;
    a = oam_attr[y + 28];
    a &= 0x3F;
    a |= 0x40;
    oam_attr[y + 28] = a;
}

// bank=1 $F12A
void func_F12A()
{
    x = 0;
    y = 0;
    func_F142(); return;
}

// bank=1 $F131
void func_F131()
{
    y = 1;
    func_F1A8();
    y = 3;
    func_F142(); return;
}

// bank=1 $F13B
void func_F13B()
{
    y = 0;
    func_F1A8();
    y = 2;
}

// bank=1 $F148
void func_F148()
{
    y = 2;
    func_F1A8();
    y = 6;
    func_F142(); return;
}

// bank=1 $F152
void func_F152()
{
    a = 1;
    y = 1;
    func_F165(); return;
}

// bank=1 $F159
void func_F159()
{
    a = 9;
    y = 4;
    func_F165();
    x++;
    x++;
    a = 9;
    y++;
}

// bank=1 $F165
void func_F165()
{
    vram_ptr_lo = x;
    carry = false;
    a = (byte)(a + vram_ptr_lo + (carry ? 1 : 0));
    x = a;
    func_F171();
    x = zp_08;
    return;
}

// bank=1 $F171
void func_F171()
{
    vram_buf[y + 184] = buf_00CE[x];
    a = buf_0086[x];
    carry = true;
    // SBC $071C
    vram_buf[y + 173] = a;
    return;
}

// bank=1 $F180
void func_F180()
{
    x = 0;
    y = 0;
    func_F1C0(); return;
}

// bank=1 $F187
void func_F187()
{
    y = 0;
    func_F1A8();
    y = 2;
    func_F1C0(); return;
}

// bank=1 $F191
void func_F191()
{
    y = 1;
    func_F1A8();
    y = 3;
    func_F1C0(); return;
}

// bank=1 $F19B
void func_F19B()
{
    y = 2;
    func_F1A8();
    y = 6;
    func_F1C0(); return;
}

// bank=1 $F1A8
void func_F1A8()
{
    a = x;
    carry = false;
    // ADC $F1A5
    x = a;
    return;
}

// bank=1 $F1AF
void func_F1AF()
{
    a = 1;
    y = 1;
    func_F1BA(); return;
}

// bank=1 $F1B6
void func_F1B6()
{
    a = 9;
    y = 4;
}

// bank=1 $F1D7
void func_F1D7()
{
    func_F1F6();
    a >>= 4;
    vram_ptr_lo = a;
    func_F239(); return;
}

// bank=1 $F1F6
void func_F1F6()
{
    ptr_04_lo = x;
    y = 1;
    L_F1FA: ;
    a = game[y + 32];
    carry = true;
    // SBC $86
    ptr_06_hi = a;
    a = game[y + 30];
    // SBC $6D
    // LDX $F1F3
    zero = a == 0; carry = a >= 0; negative = (byte)(a - 0) >= 0x80;
    if (negative) goto L_F21E;
    // LDX $F1F4
    zero = a == 1; carry = a >= 1; negative = (byte)(a - 1) >= 0x80;
    if (!negative) goto L_F21E;
    ptr_06_lo = 0x38;
    a = 8;
    func_F26D();
    L_F21E: ;
    a = peek((ushort)(0xF1E3 + x));
    x = ptr_04_lo;
    if (a != 0) return;
    y--;
    if (y < 0x80) goto L_F1FA;
}

// bank=1 $F26D
void func_F26D()
{
    ptr_04_hi = a;
    a = ptr_06_hi;
    if (a >= ptr_06_lo) return;
    a >>= 3;
    a &= 7;
    if (y < 1) { a = (byte)(a + ptr_04_hi + (carry ? 1 : 0)); }
    x = a;
}

// bank=0 $82BB
void func_00_82BB()
{
    game_00 = 0;
}

// bank=0 $82C0
void func_00_82C0()
{
    func_00_AEEA();
    a = zp_0E;
    if (a != 6) return;
    game[0x74] = 0;
    game[0x76] = 0;
    game[0x26] = 0;
    game[0x78]++;
    return;
}

// bank=0 $82D8
void func_00_82D8()
{
    do
    {
        game[0x74] = 0;
        game[0x76] = 0;
        game[0x26] = 0;
        game[0x78]++;
        return;
        y = ram_07A2;
    }
    while (zero);
    a *= 2;
    if (carry)
    {
        a = ram_07FD;
        func_00_830E();
    }
    func_00_9C03();
    game[0x61]++;
    f_82D8_0764++;
    game[0x5B]++;
    game[0x74]++;
    game[0x6E] = ram_07FC;
    game[0x76] = 0;
    ram_07A2 = 0;
    x = 0x17;
    a = 0;
    do
    {
        score[x + 6] = a;
        x--;
    }
    while (!negative);
    return;
}

// bank=0 $863F
void func_00_863F()
{
    vram_buf[0] = a;
    return;
}

// bank=0 $8F75
void func_00_8F75()
{
    L_8F75: ;
    L_8F68: ;
    a = buf_0134[x];
    carry = false;
    // ADC $07D7
    if (!(negative))
    {
        if (a < 0x0A)
        {
            score[y] = a;
            y--;
            x--;
            if (x < 0x80) goto L_8F68;
            a = 0;
            x = 6;
            do
            {
            buf_0110[x + 35] = a;
            x--;
            }
            while (!negative);
            return;
            }
            // DEC $0133
            a = 9;
            if (a != 0) goto L_8F75;
        }
    carry = true;
    a = (byte)(a - 0x0A - (carry ? 0 : 1));
    // INC $0133
    goto L_8F75;
}

// bank=0 $AF67
void func_00_AF67()
{
    zp_0D = zp_0A;
    zp_0C = 0;
    a = game[0x77];
    if (a == 6) return;
    a = game[0x23];
    if (a == 0)
    {
        a = game[0x41];
        zero = a == 0x20; carry = a >= 0x20; negative = (byte)(a - 0x20) >= 0x80;
        if (negative) return;
        a = game[0x41];
        a = (byte)(a - 0x20 - (carry ? 0 : 1));
        game[0x41] = a;
        vram_buf[0x40] = 0;
    }
    func_00_92B0();
}

// bank=0 $B000
void func_00_B000()
{
    x = 0;
    func_F1F6();
    vram_ptr_lo = a;
    y = 0;
    a *= 2;
    if (!carry)
    {
        y++;
        a = vram_ptr_lo;
        a &= 0x20;
        if (a == 0) goto L_B02E;
    }
    a = game[y + 32];
    carry = true;
    // SBC $B034
    buf_0086_00 = a;
    a = game[y + 30];
    a = (byte)(a - 0 - (carry ? 0 : 1));
    buf_006D_00 = a;
    a = zp_0C;
    if (a == peek((ushort)(0xB036 + y))) goto L_B02E;
    obj_0046[0x11] = 0;
    L_B02E: ;
    vram_buf[0xA1] = 0;
    return;
}

// bank=0 $B315
void func_00_B315()
{
    game[0x64]++;
    func_00_9C03();
    game[0x5B]++;
    func_00_B213();
    game[0x5F] = a;
    zp_FC = 0x80;
    return;
}

// bank=0 $B51C
void func_00_B51C()
{
    vram_ptr_lo = 0;
    a = zp_1D;
    if (a != 0)
    {
        a = game[0x04];
        if (a >= 0x19) { func_00_B55E(); return; }
        // if (!carry) goto L_B545 // label not in this routine
    }
    y++;
    a = game[0x52];
    if (a != 0)
    {
        y--;
        a = zp_0C;
        // if (a != zp_45) goto L_B545 // label not in this routine
        a = zp_0A;
        a &= 0x40;
        if (a == 0)
        {
            a = f_B51C_0783;
            if (a != 0) { func_00_B55E(); return; }
            }
            y++;
            vram_ptr_lo++;
            a = game[0x07];
            if (a == 0)
            {
            a = game[0x04];
            if (a < 0x21) { func_00_B55E(); return; }
            }
            vram_ptr_lo++;
            func_00_B55E(); return;
        }
    f_B51C_0783 = 0x0A;
}

// bank=0 $B55E
void func_00_B55E()
{
    a = peek((ushort)(0xB440 + y));
    poke((ushort)0x0450, a);
    a = zp_0E;
    if (a == 7) { y = 3; }
    a = peek((ushort)(0xB443 + y));
    poke((ushort)0x0456, a);
    y = vram_ptr_lo;
    a = peek((ushort)(0xB447 + y));
    f_B55E_0702 = a;
    f_B55E_0701 = 0;
    a = zp_33;
    if (a == zp_45) return;
    { byte _m = f_B55E_0702; carry = (_m & 0x80) != 0; f_B55E_0702 = (byte)(_m << 1); }
    { bool _c = carry; byte _m = f_B55E_0701; carry = (_m & 0x80) != 0; f_B55E_0701 = (byte)((_m << 1) | (_c ? 1 : 0)); }
}

// bank=0 $B5C5
void func_00_B5C5()
{
    a = peek((ushort)(0xB58C + y));
    game[0x10] = a;
    return;
}

// bank=0 $B839
void func_00_B839()
{
    buf_006D_00 = a;
    vram_ptr_lo = 0x10;
    buf_047D_00 = 1;
    zp_02 = 1;
    carry = (a & 1) != 0; a >>= 1;
    x = a;
    func_00_BFD7(); return;
}

// bank=0 $B8AC
void func_00_B8AC()
{
    func_F1AF();
    func_F152();
    func_E54B();
    return;
}

// bank=0 $BA1A
void func_00_BA1A()
{
    a = buf_0016[x];
    if (a != 0x33) { func_00_BA2D(); return; }
    func_D67A();
    a = buf_000F[x];
    if (a == 0) { func_00_BA2D(); return; }
    func_F1AF();
    func_00_BA33();
}

// bank=0 $BA2D
void func_00_BA2D()
{
    L_BA2D: ;
    L_B9C3: ;
    zp_08 = x;
    a = buf_000F[x];
    if (a != 0) { func_00_BA1A(); return; }
    a = buf_07A8[x];
    y = buf_06BE[0x0E];
    a = (byte)(a & peek((ushort)(0xB9BA + y)));
    if (a >= 6) { func_00_BA1A(); return; }
    y = a;
    a = buf_046B[y];
    if (a == 0) { func_00_BA1A(); return; }
    a = buf_047D[y];
    if (a != 0)
    {
        a = (byte)(a - 0 - (carry ? 0 : 1));
        buf_047D[y] = a;
        func_00_BA1A(); return;
    }
    a = game[0x4B];
    if (a != 0) { func_00_BA1A(); return; }
    a = 0x0E;
    buf_047D[y] = a;
    obj_006E[x] = buf_046B[y];
    obj_0087[x] = buf_046B[y + 6];
    a = buf_046B[y + 12];
    carry = true;
    a = (byte)(a - 8 - (carry ? 0 : 1));
    obj_00CF[x] = a;
    a = 1;
    obj_00B6[x] = a;
    buf_000F[x] = a;
    carry = (a & 1) != 0; a >>= 1;
    obj[x] = a;
    a = 9;
    buf_0491[x + 9] = a;
    a = 0x33;
    buf_0016[x] = a;
    goto L_BA2D;
    x--;
    if (x < 0x80) goto L_B9C3;
    return;
}

// bank=0 $BB28
void func_00_BB28()
{
    func_D7C4();
    func_F19B();
    func_F148();
    func_E236();
    ClearOam_E4DC();
    return;
}

// bank=0 $BBF4
void func_00_BBF4()
{
    L_BBF4: ;
    L_BB98: ;
    zp_08 = x;
    a = obj[x + 12];
    if (a == 0) goto L_BBF4;
    a *= 2;
    if (!carry) goto L_BBA7;
    func_00_BAC3();
    goto L_BBF4;
    L_BBA7: ;
    // LDY $2A
    y--;
    if (y == 0) goto L_BBC9;
    // INC $2A
    a = obj_0087[x + 12];
    carry = false;
    a = (byte)(a + game[0x79] + (carry ? 1 : 0));
    obj_0087[x + 12] = a;
    obj_006E[x + 12] += (byte)(carry ? 1 : 0);
    a = obj[x + 12];
    // if (a != 0x30) goto L_BBE8 // label not in this routine
    a = 0;
    obj[x + 12] = a;
    goto L_BBF4;
    L_BBC9: ;
    a = x;
    a += 0x0D;
    x = a;
    vram_ptr_lo = 0x50;
    zp_02 = 6;
    carry = (a & 1) != 0; a >>= 1;
    vram_ptr_hi = a;
    a = 0;
    func_00_BFD7();
    x = zp_08;
    a = obj_009F[x + 13];
    if (a == 5)
    {
        // INC $2A
    }
    func_F148();
    func_F19B();
    func_E236();
    func_E686();
    x--;
    if (x < 0x80) goto L_BB98;
    return;
}

// bank=0 $BC49
void func_00_BC49()
{
    poke((ushort)0x1B, 0x2E);
    obj_006E[0x05] = obj_006E[x + 8];
    obj_0087[0x05] = obj_0087[x + 8];
    obj_00B6[0x05] = 1;
    a = obj_00CF[x + 8];
    carry = true;
    a = (byte)(a - 8 - (carry ? 0 : 1));
    obj_00CF[0x05] = a;
    obj[0x05] = 1;
    poke((ushort)0x14, 1);
    buf_0491[0x0E] = 3;
    a = zp_39;
    if (a < 2)
    {
        a = game[0x5A];
        if (a >= 2) { carry = (a & 1) != 0; a >>= 1; }
        zp_39 = a;
    }
    vram_buf[0xCA] = 0x20;
    zp_FE = 2;
    return;
}

// bank=0 $BD7B
void func_00_BD7B()
{
    a = vram_buf[0xEE];
    a ^= 1;
    vram_buf[0xEE] = a;
    return;
}

// bank=0 $BF94
void func_00_BF94()
{
    a = 3;
    vram_ptr_lo = y;
    x++;
    func_00_BFAD();
    x = zp_08;
    return;
}

// bank=1 $C0CC
void func_C0CC()
{
    L_C0CC: ;
    a = f_C0CC_0745;
    if (a == 0) goto L_C12F;
    a = game[0x2A];
    if (a != 0) goto L_C12F;
    y = 0x0B;
    L_C0D8: ;
    y--;
    if (y >= 0x80) goto L_C12F;
    a = game[0x63];
    if (a != peek((ushort)(0xC06B + y))) goto L_C0D8;
    a = game[0x29];
    if (a != peek((ushort)(0xC076 + y))) goto L_C0D8;
    a = buf_00CE_00;
    if (a != peek((ushort)(0xC081 + y))) goto L_C115;
    a = zp_1D;
    if (a != 0) goto L_C115;
    a = game[0x63];
    if (a != 6) goto L_C122;
    f_C0CC_06D9++;
    L_C102: ;
    f_C0CC_06DA++;
    a = f_C0CC_06DA;
    if (a != 3) goto L_C12A;
    a = f_C0CC_06D9;
    if (a == 3) goto L_C122;
    if (!zero) goto L_C11C;
    L_C115: ;
    a = game[0x63];
    if (a == 6) goto L_C102;
    L_C11C: ;
    func_C08C();
    func_D071();
    L_C122: ;
    f_C0CC_06DA = 0;
    f_C0CC_06D9 = 0;
    L_C12A: ;
    f_C0CC_0745 = 0;
    L_C12F: ;
    a = f_C0CC_06CD;
    if (a != 0)
    {
        buf_0016[x] = a;
        a = 1;
        buf_000F[x] = a;
        a = 0;
        obj[x] = a;
        f_C0CC_06CD = a;
        func_C226(); return;
    }
    y = game[0x3D];
    a = peek((ushort)((ptr_E9_lo | ptr_E9_hi << 8) + y));
    if (a == 0xFF) { func_C216(); return; }
    a &= 0x0F;
    // if (a == 0x0E) goto L_C164 // label not in this routine
    // if (x < 5) goto L_C164 // label not in this routine
    y++;
    a = peek((ushort)((ptr_E9_lo | ptr_E9_hi << 8) + y));
    a &= 0x3F;
    if (a != 0x2E) { return; }
    ushort _u3 = (ushort)(game[0x21] | (game[0x1F] << 8));
    _u3 += 0x30;
    ptr_06_hi = (byte)(_u3 & 0xF0);
    ptr_06_lo = (byte)(_u3 >> 8);
    y = game[0x3D];
    y++;
    a = peek((ushort)((ptr_E9_lo | ptr_E9_hi << 8) + y));
    a *= 2;
    // if (!carry) goto L_C189 // label not in this routine
    a = game[0x3F];
    if (a == 0)
    {
        game[0x3F]++;
        game[0x3E]++;
    }
    y--;
    a = peek((ushort)((ptr_E9_lo | ptr_E9_hi << 8) + y));
    a &= 0x0F;
    // if (a != 0x0F) goto L_C1AB // label not in this routine
    a = game[0x3F];
    if (a == 0)
    {
        y++;
        a = peek((ushort)((ptr_E9_lo | ptr_E9_hi << 8) + y));
        a &= 0x3F;
        game[0x3E] = a;
        game[0x3D]++;
        game[0x3D]++;
        game[0x3F]++;
        goto L_C0CC;
    }
    obj_006E[x] = game[0x3E];
    a = peek((ushort)((ptr_E9_lo | ptr_E9_hi << 8) + y));
    a &= 0xF0;
    obj_0087[x] = a;
    { byte _v = game[0x21]; zero = a == _v; carry = a >= _v; negative = (byte)(a - _v) >= 0x80; }
    a = obj_006E[x];
    // SBC $071B
    if (!carry)
    {
        a = peek((ushort)((ptr_E9_lo | ptr_E9_hi << 8) + y));
        a &= 0x0F;
        if (a == 0x0E) goto L_C231;
        func_C250(); return;
    }
    a = ptr_06_hi;
    { byte _v = obj_0087[x]; zero = a == _v; carry = a >= _v; negative = (byte)(a - _v) >= 0x80; }
    a = ptr_06_lo;
    // SBC $6E
    if (!carry) { func_C216(); return; }
    a = 1;
    obj_00B6[x] = a;
    a = peek((ushort)((ptr_E9_lo | ptr_E9_hi << 8) + y));
    a *= 16;
    obj_00CF[x] = a;
    if (a == 0xE0) goto L_C231;
    y++;
    a = peek((ushort)((ptr_E9_lo | ptr_E9_hi << 8) + y));
    a &= 0x40;
    if (a != 0)
    {
        a = buf_06BE[0x0E];
        if (a == 0) { func_C25E(); return; }
    }
    a = peek((ushort)((ptr_E9_lo | ptr_E9_hi << 8) + y));
    a &= 0x3F;
    if (a >= 0x37)
    {
        if (a >= 0x3F)
        {
            }
            // if (a != 6) goto L_C208 // label not in this routine
            y = game[0x6E];
            if (y != 0) { a = 2; }
            buf_0016[x] = a;
            a = 1;
            buf_000F[x] = a;
            func_C226();
            a = buf_000F[x];
            if (a != 0) { func_C25E(); return; }
            return;
        }
    func_C71B(); return;
    L_C231: ;
    y++;
    y++;
    a = peek((ushort)((ptr_E9_lo | ptr_E9_hi << 8) + y));
    a >>= 5;
    if (a == game[0x63])
    {
        y--;
        a = peek((ushort)((ptr_E9_lo | ptr_E9_hi << 8) + y));
        game[0x54] = a;
        y++;
        a = peek((ushort)((ptr_E9_lo | ptr_E9_hi << 8) + y));
        a &= 0x1F;
        game[0x55] = a;
    }
    func_C25B(); return;
}

// bank=1 $C216
void func_C216()
{
    a = buf_06BE[0x0D];
    if (a == 0)
    {
        a = vram_buf[0x98];
        if (a != 1) return;
        a = 0x2F;
    }
    buf_0016[x] = a;
}

// bank=1 $C250
void func_C250()
{
    y = game[0x3D];
    a = peek((ushort)((ptr_E9_lo | ptr_E9_hi << 8) + y));
    a &= 0x0F;
    if (a != 0x0E) { func_C25E(); return; }
}

// bank=1 $C25B
void func_C25B()
{
    game[0x3D]++;
}

// bank=1 $C25E
void func_C25E()
{
    game[0x3D]++;
    game[0x3D]++;
    game[0x3F] = 0;
    x = zp_08;
    return;
}

// bank=1 $C71B
void func_C71B()
{
    y = 0;
    carry = true;
    a = (byte)(a - 0x37 - (carry ? 0 : 1));
    pha();
    if (a < 4)
    {
        pha();
        y = 6;
        a = game[0x6E];
        if (a != 0) { y = 2; }
        a = pla();
    }
    vram_ptr_hi = 2;
    y = 0xB0;
    a &= 2;
    if (a != 0) { y = 0x70; }
    vram_ptr_lo = 0x70;
    zp_02 = game[0x1F];
    zp_03 = game[0x21];
    y = 2;
    a = pla();
    carry = (a & 1) != 0; a >>= 1;
    if (carry) { y++; }
    f_C71B_06D3 = 2;
    L_C750: ;
    x = 0xFF;
    L_C752: ;
    x++;
    if (x < 5)
    {
        a = buf_000F[x];
        if (a != 0) goto L_C752;
        buf_0016[x] = vram_ptr_hi;
        obj_006E[x] = zp_02;
        obj_0087[x] = zp_03;
        a += 0x18;
        zp_03 = a;
        zp_02 += (byte)(carry ? 1 : 0);
        obj_00CF[x] = vram_ptr_lo;
        a = 1;
        obj_00B6[x] = a;
        buf_000F[x] = a;
        func_C26C();
        f_C71B_06D3--;
        if (f_C71B_06D3 != 0) goto L_C750;
    }
    func_C25E(); return;
}

// bank=1 $C828
void func_C828()
{
    func_C363();
    a = 5;
    y = game[0x52];
    // if (y == 3) goto L_C83B // label not in this routine
    y = buf_06BE[0x0E];
    if (y == 0) { a = 6; }
    buf_0491[x + 9] = a;
    return;
}

// bank=1 $C860
void func_C860()
{
    y = 1;
    func_C871();
    a = 4;
    buf_0491[x + 9] = a;
    return;
}

// bank=1 $C882
void func_C882()
{
    L_C7EB: ;
    // ADC $C8
    y = 0xFF;
    obj[x] = f_C882_03A0;
    if (negative)
    {
        a = x;
        y = a;
    }
    f_C882_03A0 = 0xFF;
    a = 0;
    obj_0046[x] = a;
    y = a;
    func_C871();
    a = 0xFF;
    vram_buf[x + 162] = a;
    func_C828(); return;
    L_C84C: ;
    if (!negative) goto L_C7EB;
    // ???(34)
    // ???(04)
    a = 0xFF;
    obj_009F[x + 1] = a;
    func_C860(); return;
    x = zp_08;
    a = 0;
    // LDY $16
    if (y >= 0x15)
    {
        a = y;
        a = (byte)(a - 0x14 - (carry ? 0 : 1));
    }
    func_00_8E04();
    zero = x == 0xC8; carry = x >= 0xC8;
    // AND $C9
    obj_00CF[x + 3] = a;
    // DEC $C8
    // DEC $C8
    // DEC $C8
    // DEC $C8
    // ???(47)
    zero = a == 0x47; carry = a >= 0x47; negative = (byte)(a - 0x47) >= 0x80;
    zero = a == 0x47; carry = a >= 0x47; negative = (byte)(a - 0x47) >= 0x80;
    zero = a == 0x47; carry = a >= 0x47; negative = (byte)(a - 0x47) >= 0x80;
    zero = a == 0x47; carry = a >= 0x47; negative = (byte)(a - 0x47) >= 0x80;
    zero = a == 0x47; carry = a >= 0x47; negative = (byte)(a - 0x47) >= 0x80;
    zero = a == 0x47; carry = a >= 0x47; negative = (byte)(a - 0x47) >= 0x80;
    zero = a == 0x47; carry = a >= 0x47; negative = (byte)(a - 0x47) >= 0x80;
    zero = a == 0xD6; carry = a >= 0xD6; negative = (byte)(a - 0xD6) >= 0x80;
    y++;
    a = (byte)(a + f_C882_C9 + (carry ? 1 : 0));
    a = (byte)(a + f_C882_C9 + (carry ? 1 : 0));
    a = (byte)(a + f_C882_C9 + (carry ? 1 : 0));
    a = (byte)(a + f_C882_C9 + (carry ? 1 : 0));
    a = (byte)(a + f_C882_C9 + (carry ? 1 : 0));
    a = (byte)(a + f_C882_C9 + (carry ? 1 : 0));
    a = (byte)(a + f_C882_C9 + (carry ? 1 : 0));
    a = (byte)(a ^ peek((ushort)0x4DC9));
    if (a != 0x65) goto L_C84C;
    // LDY $B94B
    // DEC $C8
    { byte _v = peek((ushort)(0xBAD2 + y)); zero = a == _v; carry = a >= _v; negative = (byte)(a - _v) >= 0x80; }
    // CLV
    // DEC $C8
    y = obj_00B6[0x01];
    // ???(D7)
    y++;
    return;
}

// bank=1 $D795
void func_D795()
{
    a = buf_0016[x];
    if (a == 0x0D)
    {
        a = obj_00CF[x];
        a = (byte)(a + 0x18 + (carry ? 1 : 0));
        obj_00CF[x] = a;
    }
    func_E01B();
    a = obj[x];
    a &= 0x1F;
    a |= 0x20;
    obj[x] = a;
    a = 2;
    // LDY $16
    if (y == 5) { a = 6; }
    if (y == 6) { a = 1; }
    func_DA11();
    zp_FF = 8;
    return;
}

// bank=1 $D800
void func_D800()
{
    func_C998();
    a = 6;
    func_DA11();
    zp_FE = 0x20;
    a = zp_39;
    if (a >= 2)
    {
        if (a != 3)
        {
            ram_079F = 0x23;
            zp_FB = 0x40;
            return;
            }
            a = game[0x5A];
            if (a != 0)
            {
                if (a != 1) return;
                x = zp_08;
                game[0x5A] = 2;
                FlushVramBuf_85F1();
                x = zp_08;
                a = 0x0C;
                func_D847(); return;
                }
                a = 0x0B;
                buf_0110[x] = a;
                return;
            }
    game[0x5A] = 1;
    a = 9;
}

// bank=1 $D847
void func_D847()
{
    y = 0;
    func_D948();
    return;
}

// bank=1 $D92C
void func_D92C()
{
    a = ram_079E;
    x = zp_08;
    return;
}

// bank=1 $D9F1
void func_D9F1()
{
    obj_009F_00 = 0xFC;
    return;
}

// bank=1 $D9F6
void func_D9F6()
{
    a = obj_0046[x];
    if (a != 1) { func_D9FF(); return; }
    func_D92C(); return;
}

// bank=1 $D9FF
void func_D9FF()
{
    func_DB1C();
    func_D92C(); return;
}

// bank=1 $DCF6
void func_DCF6()
{
    y = zp_EB;
    a = buf_00CE_00;
    if (a >= 0xCF) goto L_DD5E;
    func_E3E8();
    func_DFA1();
    if (!carry)
    {
        pha();
        func_E3E8();
        vram_ptr_lo = a;
        a = pla();
        vram_ptr_hi = a;
        if (!zero) goto L_DD1D;
        a = vram_ptr_lo;
        if (a == 0) goto L_DD5E;
        func_DFA1();
        if (!carry) goto L_DD1D;
    }
    func_DE05(); return;
    L_DD1D: ;
    func_DF9A();
    if (carry) goto L_DD5E;
    y = obj_009F_00;
    if (y >= 0x80) goto L_DD5E;
    if (a == 0xC5) { func_DE0E(); return; }
    func_DEBD();
    if (zero) goto L_DD5E;
    y = game[0x12];
    if (y == 0)
    {
        y = ptr_04_lo;
        if (y >= 5)
        {
        vram_ptr_lo = zp_45;
        func_DF4B(); return;
        }
        func_DEC4();
        a = 0xF0;
        a = (byte)(a & buf_00CE_00);
        buf_00CE_00 = a;
        func_DEE8();
        obj_009F_00 = 0;
        buf_0433_00 = 0;
        ram_0484 = 0;
    }
    zp_1D = 0;
    L_DD5E: ;
    y = zp_EB;
    y++;
    y++;
    vram_ptr_lo = 2;
    L_DD66: ;
    y++;
    zp_EB = y;
    a = buf_00CE_00;
    // if (a < 0x20) goto L_DD85 // label not in this routine
    // if (a >= 0xE4) goto L_DD9B // label not in this routine
    func_E3EC();
    // if (zero) goto L_DD85 // label not in this routine
    // if (a == 0x1C) goto L_DD85 // label not in this routine
    if (a != 0x6B)
    {
        func_DF9A();
        if (!carry) goto L_DD9C;
    }
    y = zp_EB;
    y++;
    a = buf_00CE_00;
    // if (a < 8) goto L_DD9B // label not in this routine
    if (a < 0xD0)
    {
        func_E3EC();
        if (!zero) goto L_DD9C;
        vram_ptr_lo--;
        if (vram_ptr_lo != 0) goto L_DD66;
    }
    return;
    L_DD9C: ;
    func_DEBD();
    if (zero) return;
    func_DF9A();
    if (carry)
    {
        func_DE2E(); return;
    }
    func_DFA1();
    if (carry) { func_DE05(); return; }
    func_DEDD();
    if (carry)
    {
        a = game[0x12];
        if (a != 0) return;
        func_DDFF(); return;
    }
    y = zp_1D;
    if (y != 0) { func_DDFF(); return; }
    y = zp_33;
    y--;
    if (y != 0) { func_DDFF(); return; }
    if (a != 0x6C)
    {
        if (a != 0x1F) { func_DDFF(); return; }
    }
    a = vram_buf[0xC4];
    if (a == 0) { zp_FF = 0x10; }
    a |= 0x20;
    vram_buf[0xC4] = a;
    a = buf_0086_00;
    a &= 0x0F;
    if (a != 0)
    {
        y = 0;
        a = game[0x1E];
        if (a != 0) { y++; }
        a = peek((ushort)(0xDE03 + y));
        buf_06BE[0x20] = a;
    }
    a = zp_0E;
    if (a == 7) return;
    if (a != 8) return;
    zp_0E = 2;
    return;
}

// bank=1 $DDFF
void func_DDFF()
{
    func_DF4B();
    return;
}

// bank=1 $DE05
void func_DE05()
{
    func_DE1C();
    game[0x4C]++;
    func_00_BBFE(); return;
}

// bank=1 $DE0E
void func_DE0E()
{
    game[0x76] = 0;
    game[0x74] = 2;
    obj_0046[0x11] = 0x18;
}

// bank=1 $DE2E
void func_DE2E()
{
    y = ptr_04_lo;
    if (y >= 6)
    {
        if (y >= 0x0A)
        {
            }
            return;
        }
    if (a != 0x24)
    {
        if (a != 0x25) goto L_DE7A;
    }
    a = zp_0E;
    if (a == 5) { func_DE88(); return; }
    zp_33 = 1;
    game[0x27]++;
    a = zp_0E;
    if (a == 4) goto L_DE73;
    a = 0x33;
    func_00_9716();
    zp_FC = 0x80;
    carry = (a & 1) != 0; a >>= 1;
    game[0x17] = a;
    x = 4;
    game[0x13] = buf_00CE_00;
    L_DE68: ;
    if (a < peek((ushort)(0xDE29 + x)))
    {
        x--;
        if (x != 0) goto L_DE68;
    }
    ram_010F = 4;
    L_DE73: ;
    zp_0E = 4;
    func_DE88(); return;
    L_DE7A: ;
    if (a != 0x26) { func_DE88(); return; }
    a = buf_00CE_00;
    if (a >= 0x20) { func_DE88(); return; }
    zp_0E = 1;
}

// bank=1 $DE88
void func_DE88()
{
    zp_1D = 3;
    obj_0046[0x11] = 0;
    game[0x09] = 0;
    a = buf_0086_00;
    carry = true;
    // SBC $071C
    if (a < 0x10) { zp_33 = 2; }
    y = zp_33;
    a = ptr_06_lo;
    a *= 16;
    carry = false;
    // ADC $DE24
    buf_0086_00 = a;
    a = ptr_06_lo;
    if (a != 0) return;
    a = game[0x1F];
    carry = false;
    // ADC $DE26
    buf_006D_00 = a;
}

// bank=1 $DF66
void func_DF66()
{
    game[0x89] = 0x10;
    obj_0046[0x11] = 0;
    zero = a == 0; carry = a >= 0; negative = (byte)(a - 0) >= 0x80;
    if (negative) { y--; }
    vram_ptr_lo = 0;
    carry = false;
    a = (byte)(a + buf_0086_00 + (carry ? 1 : 0));
    buf_0086_00 = a;
    a = buf_006D_00;
    a = (byte)(a + vram_ptr_lo + (carry ? 1 : 0));
    buf_006D_00 = a;
    a = x;
    a ^= 0xFF;
    a = (byte)(a & ram_0490);
    ram_0490 = a;
    return;
}

// bank=1 $E252
void func_E252()
{
    a = obj_0087[x];
    carry = true;
    // SBC $071C
    vram_ptr_hi = a;
    a = obj_006E[x];
    // SBC $071A
    // if (negative) goto L_E267 // label not in this routine
    a = (byte)(a | vram_ptr_hi);
    if (a != 0) { y = vram_ptr_lo; }
    a = y;
    a = (byte)(a & vram_buf[0xD1]);
    vram_buf[x + 216] = a;
    if (!zero)
    {
        a = x;
        a *= 4;
        y = a;
        a = 0xFF;
        buf_04AC[y + 4] = a;
        buf_04AC[y + 5] = a;
        buf_04AC[y + 6] = a;
        buf_04AC[y + 7] = a;
        return;
    }
    else
    {
        func_E27C(); return;
    }
}

// bank=1 $E27C
void func_E27C()
{
    a = x;
    a += 1;
    x = a;
    y = 1;
    func_E29C();
    func_E2DE(); return;
}

// bank=1 $E2DE
void func_E2DE()
{
    ushort _u4 = (ushort)(game[0x20] | (game[0x1E] << 8));
    _u4 += 0x80;
    zp_02 = (byte)_u4;
    vram_ptr_hi = (byte)(_u4 >> 8);
    a = buf_0086[x];
    { byte _v = zp_02; zero = a == _v; carry = a >= _v; negative = (byte)(a - _v) >= 0x80; }
    a = buf_006D[x];
    a = (byte)(a - vram_ptr_hi - (carry ? 0 : 1));
    if (carry)
    {
        a = buf_04AC[y + 2];
        if (a < 0x80)
        {
        a = 0xFF;
        // LDX $04AC
        if (!negative) { buf_04AC[y] = a; }
        buf_04AC[y + 2] = a;
        }
        x = zp_08;
        return;
    }
    a = buf_04AC[y];
    // if (!negative) goto L_E322 // label not in this routine
    if (a >= 0xA0)
    {
        a = 0;
        // LDX $04AE
        if (negative) { buf_04AC[y + 2] = a; }
        buf_04AC[y] = a;
    }
    x = zp_08;
    return;
}

// bank=1 $E42B
void func_E42B()
{
    a &= 0x0F;
    ptr_04_lo = a;
    a = zp_03;
    return;
}

// bank=1 $E6BD
void func_E6BD()
{
    return;
}

// bank=1 $E87D
void FlushVramBuf_E87D()
{
    zp_02 = obj_00CF[x];
    ptr_04_hi = vram_buf[0xAE];
    // LDY $06E5
    zp_EB = y;
    ram_0109 = 0;
    zp_03 = obj_0046[x];
    ptr_04_lo = vram_buf[x + 197];
    a = buf_0016[x];
    if (a == 0x0D)
    {
        // LDY $58
        // if (negative) goto L_E8A9 // label not in this routine
        // LDY $078A
        // if (zero) goto L_E8A9 // label not in this routine
        return;
    }
    zp_ED = obj[x];
    a &= 0x1F;
    y = a;
    a = buf_0016[x];
    if (a == 0x35)
    {
        y = 0;
        zp_03 = 1;
        a = 0x15;
    }
    if (a == 0x33)
    {
        zp_02--;
        a = 3;
        // LDY $078A
        if (!zero) { a |= 0x20; }
        ptr_04_lo = a;
        zp_ED = 0;
        a = 8;
    }
    if (a == 0x32)
    {
        y = 3;
        x = game[0x12];
        a = peek((ushort)(0xE878 + x));
    }
    zp_EF = a;
    zp_EC = 3;
    x = zp_08;
    if (a == 0x0C)
    {
        a = obj_009F[x + 1];
        // if (negative) goto L_E8F2 // label not in this routine
        ram_0109++;
    }
    a = vram_buf[0x6A];
    if (a != 0)
    {
        y = 0x16;
        if (a != 1) { y++; }
        zp_EF = 0x16;
    }
    y = zp_EF;
    // if (y != 6) goto L_E923 // label not in this routine
    a = obj[x];
    if (a >= 2) { zp_EC = 4; }
    a &= 0x20;
    a = (byte)(a | game[0x4B]);
    // if (a != 0) goto L_E923 // label not in this routine
    a = zp_09;
    a &= 8;
    if (a == 0)
    {
        a = zp_03;
        a ^= 3;
        zp_03 = a;
    }
    a = peek((ushort)(0xE85B + y));
    a = (byte)(a | ptr_04_lo);
    ptr_04_lo = a;
    a = peek((ushort)(0xE840 + y));
    x = a;
    y = zp_EC;
    a = vram_buf[0x6A];
    if (a != 0)
    {
        if (a == 1)
        {
        a = f_E87D_0363;
        if (negative) { x = 0xDE; }
        a = zp_ED;
        a &= 0x20;
        func_EA4B(); return;
        }
        a = f_E87D_0363;
        a &= 1;
        if (a != 0) { x = 0xE4; }
        a = zp_ED;
        a &= 0x20;
        // if (a == 0) goto L_E949 // label not in this routine
        a = zp_02;
        carry = true;
        a = (byte)(a - 0x10 - (carry ? 0 : 1));
        zp_02 = a;
        func_E946(); return;
    }
    if (x == 0x24)
    {
        if (y == 5)
        {
        x = 0x30;
        zp_03 = 2;
        zp_EC = 5;
        }
        func_E9CA(); return;
    }
    if (x == 0x90)
    {
        a = zp_ED;
        a &= 0x20;
        // if (a != 0) goto L_E98D // label not in this routine
        a = ram_078F;
        if (a < 0x10) { x = 0x96; }
        func_EA37(); return;
    }
    a = zp_EF;
    // if (a >= 4) goto L_E9A6 // label not in this routine
    // if (y < 2) goto L_E9A6 // label not in this routine
    x = 0x5A;
    y = zp_EF;
    if (y == 2)
    {
        x = 0x7E;
        zp_02++;
    }
    a = zp_EC;
    if (a != 4) { func_E9CA(); return; }
    x = 0x72;
    zp_02++;
    y = zp_EF;
    if (y != 2)
    {
        x = 0x66;
        zp_02++;
    }
    if (y != 6) { func_E9CA(); return; }
    x = 0x54;
    a = zp_ED;
    a &= 0x20;
    if (a != 0) { func_E9CA(); return; }
    x = 0x8A;
    zp_02--;
}

// bank=1 $E946
void func_E946()
{
    ram_0109 = x;
    func_EA4B(); return;
}

// bank=1 $E9CA
void func_E9CA()
{
    y = zp_08;
    a = zp_EF;
    if (a == 5)
    {
        a = zp_ED;
        // if (a == 0) goto L_E9FA // label not in this routine
        a &= 8;
        if (a == 0) { func_EA37(); return; }
        x = 0xB4;
        // if (x != 0) goto L_E9FA // label not in this routine
    }
    // if (x == 0x48) goto L_E9FA // label not in this routine
    a = buf_0796[y];
    if (a >= 5) { func_EA37(); return; }
    if (x == 0x3C)
    {
        if (a == 1) { func_EA37(); return; }
        zp_02++;
        zp_02++;
        zp_02++;
        func_EA29(); return;
    }
    a = zp_EF;
    switch (a)
    {
        case 6:
            func_EA37(); return;
            break;
        case 8:
            func_EA37(); return;
            break;
        case 0x0C:
            func_EA37(); return;
            break;
    }
    if (a >= 0x18) { func_EA37(); return; }
    y = 0;
    if (a == 0x15)
    {
        y++;
        a = game[0x63];
        if (a >= 7) { func_EA37(); return; }
        x = 0xA2;
        zp_EC = 3;
        if (!zero) { func_EA37(); return; }
    }
    a = zp_09;
    a = (byte)(a & peek((ushort)(0xE876 + y)));
    if (a != 0) { func_EA37(); return; }
}

// bank=1 $EA29
void func_EA29()
{
    a = zp_ED;
    a &= 0xA0;
    a = (byte)(a | game[0x4B]);
    if (a != 0) { func_EA37(); return; }
    a = x;
    a += 6;
    x = a;
}

// bank=1 $EA37
void func_EA37()
{
    a = zp_ED;
    a &= 0x20;
    if (a == 0) { func_EA4B(); return; }
    a = zp_EF;
    if (a < 4) { func_EA4B(); return; }
    ram_0109 = 1;
    y--;
    zp_EC = 1;
}

// bank=1 $EA4B
void func_EA4B()
{
    y = zp_EB;
    func_EBAA();
    func_EBAA();
    func_EBAA();
    x = zp_08;
    // LDY $06E5
    a = zp_EF;
    if (a != 8) goto L_EA64;
    L_EA61: ;
    func_EB64(); return;
    L_EA64: ;
    a = ram_0109;
    if (a == 0) goto L_EAA6;
    a = oam_attr[y];
    a |= 0x80;
    y++;
    y++;
    func_E5B5();
    y--;
    y--;
    a = y;
    x = a;
    a = zp_EF;
    // if (a == 5) goto L_EA8A // label not in this routine
    // if (a == 0x11) goto L_EA8A // label not in this routine
    if (a < 0x15)
    {
        a = x;
        a += 8;
        x = a;
    }
    a = oam_tile[x];
    pha();
    a = oam_tile[x + 4];
    pha();
    oam_tile[x] = oam_tile[y + 16];
    oam_tile[x + 4] = oam_tile[y + 20];
    a = pla();
    oam_tile[y + 20] = a;
    a = pla();
    oam_tile[y + 16] = a;
    L_EAA6: ;
    a = vram_buf[0x6A];
    if (a != 0) goto L_EA61;
    a = zp_EF;
    x = zp_EC;
    if (a == 5) { func_EB64(); return; }
    if (a == 7) goto L_EAD7;
    if (a == 0x0D) goto L_EAD7;
    if (a == 0x0C) goto L_EAD7;
    if (a == 0x12)
    {
        // if (x != 5) goto L_EB12 // label not in this routine
    }
    if (a == 0x15)
    {
        a = 0x42;
        oam_attr[y + 20] = a;
    }
    // if (x < 2) goto L_EB12 // label not in this routine
    L_EAD7: ;
    a = vram_buf[0x6A];
    // if (a != 0) goto L_EB12 // label not in this routine
    a = oam_attr[y];
    a &= 0xA3;
    oam_attr[y] = a;
    oam_attr[y + 8] = a;
    oam_attr[y + 16] = a;
    a |= 0x40;
    if (x == 5) { a |= 0x80; }
    oam_attr[y + 4] = a;
    oam_attr[y + 12] = a;
    oam_attr[y + 20] = a;
    if (x == 4)
    {
        a = oam_attr[y + 8];
        a |= 0x80;
        oam_attr[y + 8] = a;
        oam_attr[y + 16] = a;
        a |= 0x40;
        oam_attr[y + 12] = a;
        oam_attr[y + 20] = a;
    }
    a = zp_EF;
    if (a == 0x11)
    {
        a = ram_0109;
        if (a == 0)
        {
        a = oam_attr[y + 16];
        a &= 0x81;
        oam_attr[y + 16] = a;
        a = oam_attr[y + 20];
        a |= 0x41;
        oam_attr[y + 20] = a;
        x = ram_078F;
        if (x >= 0x10) { func_EB64(); return; }
        oam_attr[y + 12] = a;
        a &= 0x81;
        oam_attr[y + 8] = a;
        if (!carry) { func_EB64(); return; }
        }
        a = oam_attr[y];
        a &= 0x81;
        oam_attr[y] = a;
        a = oam_attr[y + 4];
        a |= 0x41;
        oam_attr[y + 4] = a;
    }
    a = zp_EF;
    if (a < 0x18) { func_EB64(); return; }
    a = 0x82;
    oam_attr[y + 8] = a;
    oam_attr[y + 16] = a;
    a |= 0x40;
    oam_attr[y + 12] = a;
    oam_attr[y + 20] = a;
}

// bank=1 $EB64
void func_EB64()
{
    x = zp_08;
    a = vram_buf[0xD1];
    a >>= 3;
    pha();
    if (carry)
    {
        a = 4;
        func_EBC1();
    }
    a = pla();
    carry = (a & 1) != 0; a >>= 1;
    pha();
    if (carry)
    {
        a = 0;
        func_EBC1();
    }
    a = pla();
    a >>= 2;
    pha();
    if (carry)
    {
        a = 0x10;
        func_EBB7();
    }
    a = pla();
    carry = (a & 1) != 0; a >>= 1;
    pha();
    if (carry)
    {
        a = 8;
        func_EBB7();
    }
    a = pla();
    if ((a & 1) == 0) return;
    func_EBB7();
    a = buf_0016[x];
    if (a == 0x0C) return;
    a = obj_00B6[x];
    if (a != 2) return;
    func_C998();
}

// bank=1 $ECDE
void func_ECDE()
{
    // LDY $06F1
    oam_y[y] = vram_buf[0xBA];
    oam_x[y] = vram_buf[0xAF];
    a = zp_09;
    a >>= 2;
    pha();
    a &= 1;
    a ^= 0x64;
    oam_tile[y] = a;
    a = pla();
    a >>= 2;
    a = 2;
    if (carry) { a |= 0xC0; }
    oam_attr[y] = a;
    return;
}

// bank=1 $ED09
void ClearOam_ED09()
{
    // LDY $06EC
    a = obj[x + 6];
    // INC $24
    carry = (a & 1) != 0; a >>= 1;
    a &= 7;
    if (a < 3)
    {
        x = a;
        a = peek((ushort)(0xED06 + x));
        y++;
        func_E5BB();
        y--;
        x = zp_08;
        a = vram_buf[0xBA];
        carry = true;
        a = (byte)(a - 4 - (carry ? 0 : 1));
        oam_y[y] = a;
        oam_y[y + 8] = a;
        a += 8;
        oam_y[y + 4] = a;
        oam_y[y + 12] = a;
        a = vram_buf[0xAF];
        carry = true;
        a = (byte)(a - 4 - (carry ? 0 : 1));
        oam_x[y] = a;
        oam_x[y + 4] = a;
        a += 8;
        oam_x[y + 8] = a;
        oam_x[y + 12] = a;
        a = 2;
        oam_attr[y] = a;
        a = 0x82;
        oam_attr[y + 4] = a;
        a = 0x42;
        oam_attr[y + 8] = a;
        a = 0xC2;
        oam_attr[y + 12] = a;
        return;
    }
    a = 0;
    obj[x + 6] = a;
    return;
}

// bank=1 $EF45
void func_EF45()
{
    buf_06BE[0x17] = a;
    a = 4;
    func_EFBE();
    func_F0E9();
    a = game[0x15];
    if (a != 0)
    {
        y = 0;
        a = game[0x85];
        { byte _v = game[0x15]; zero = a == _v; carry = a >= _v; negative = (byte)(a - _v) >= 0x80; }
        game[0x15] = 0;
        // if (carry) goto L_EF7A // label not in this routine
        game[0x15] = a;
        y = 7;
        buf_06BE[0x17] = rom_EE07[y];
        y = 4;
        a = obj_0046[0x11];
        a = (byte)(a | zp_0C);
        if (a != 0) { y--; }
        a = y;
        func_EFBE();
    }
    a = vram_buf[0xD0];
    a >>= 4;
    vram_ptr_lo = a;
    x = 3;
    a = buf_06BE_26;
    a += 0x18;
    y = a;
    do
    {
        a = 0xF8;
        { byte _m = vram_ptr_lo; carry = (_m & 1) != 0; vram_ptr_lo = (byte)(_m >> 1); }
        if (carry) { ClearOam_E5C1(); }
        a = y;
        carry = true;
        a = (byte)(a - 8 - (carry ? 0 : 1));
        y = a;
        x--;
    }
    while (!negative);
    return;
}

// bank=1 $F028
void func_F028()
{
    func_F091();
    game[0x11] = 0;
    a = rom_EE07[y];
    return;
}

// bank=1 $F068
void func_F068()
{
    a = 3;
    func_F06F(); return;
}

// bank=1 $F06D
void func_F06D()
{
    a = 2;
}

// bank=1 $F06F
void func_F06F()
{
    vram_ptr_lo = a;
    func_F062();
    pha();
    a = game[0x85];
    if (a != 0) goto L_F08F;
    game[0x85] = game[0x10];
    a = game[0x11];
    a += 1;
    if (a >= vram_ptr_lo)
    {
        a = 0;
    }
    game[0x11] = 0;
    L_F08F: ;
    a = pla();
    return;
}

// bank=1 $F0D0
void func_F0D0()
{
    a *= 8;
    // ADC $EE07
    return;
}

// bank=1 $F142
void func_F142()
{
    func_F171();
    x = zp_08;
    return;
}

// bank=1 $F1BA
void func_F1BA()
{
    vram_ptr_lo = x;
    carry = false;
    a = (byte)(a + vram_ptr_lo + (carry ? 1 : 0));
    x = a;
}

// bank=1 $F1C0
void func_F1C0()
{
    a = y;
    pha();
    func_F1D7();
    a *= 16;
    a = (byte)(a | vram_ptr_lo);
    vram_ptr_lo = a;
    a = pla();
    y = a;
    vram_buf[y + 208] = vram_ptr_lo;
    x = zp_08;
    return;
}

// bank=1 $F239
void func_F239()
{
    ptr_04_lo = x;
    y = 1;
    L_F23D: ;
    a = peek((ushort)(0xF237 + y));
    carry = true;
    // SBC $CE
    ptr_06_hi = a;
    a = 1;
    // SBC $B5
    // LDX $F234
    zero = a == 0; carry = a >= 0; negative = (byte)(a - 0) >= 0x80;
    if (negative) goto L_F260;
    // LDX $F235
    zero = a == 1; carry = a >= 1; negative = (byte)(a - 1) >= 0x80;
    if (!negative) goto L_F260;
    ptr_06_lo = 0x20;
    a = 4;
    func_F26D();
    L_F260: ;
    a = peek((ushort)(0xF22B + x));
    x = ptr_04_lo;
    if (a != 0) return;
    y--;
    if (y < 0x80) goto L_F23D;
}

// bank=1 $F282
void func_F282()
{
    a = zp_03;
    a >>= 2;
    a = vram_ptr_lo;
    if (carry)
    {
        oam_tile[y + 4] = a;
        oam_tile[y] = vram_ptr_hi;
        a = 0x40;
        if (a == 0)
        {
            }
            oam_tile[y] = a;
            oam_tile[y + 4] = vram_ptr_hi;
            a = 0;
        }
    a = (byte)(a | ptr_04_lo);
    oam_attr[y] = a;
    oam_attr[y + 4] = a;
    oam_y[y] = zp_02;
    oam_y[y + 4] = a;
    oam_x[y] = ptr_04_hi;
    a += 8;
    oam_x[y + 4] = a;
    a = zp_02;
    a += 8;
    zp_02 = a;
    a = y;
    a += 8;
    y = a;
    x++;
    x++;
    return;
}

