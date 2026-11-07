// APU Test - demonstrates apu_play_tone helper and direct register access
// Simplified port from 8bitworkshop aputest.c

// Set up palette
pal_col(1, DarkMagenta);
pal_col(2, LightGray);
pal_col(3, White);

// Display channel labels
vram_adr(NTADR_A(2, 2));
vram_write("APU TEST");

vram_adr(NTADR_A(2, 5));
vram_write("PULSE 1 HELPER");

vram_adr(NTADR_A(2, 8));
vram_write("PULSE 2 HELPER");

vram_adr(NTADR_A(2, 11));
vram_write("TRIANGLE");

vram_adr(NTADR_A(2, 14));
vram_write("NOISE");

// Enable rendering
ppu_on_all();

// Enable all sound channels (pulse1, pulse2, triangle, noise)
apu_status_write(APUSTATUS.PULSE1 | APUSTATUS.PULSE2 | APUSTATUS.TRIANGLE | APUSTATUS.NOISE);

// Pulse 1 via helper: 50% duty, volume 15, period ~440Hz (0x00FD)
apu_play_tone(PulseChannel.Pulse1, 0x00FD, APUDuty.Duty50, 15);

// Pulse 2 via helper: 25% duty, volume 10, period ~262Hz (0x01A9)
apu_play_tone(PulseChannel.Pulse2, 0x01A9, APUDuty.Duty25, 10);

// Triangle: linear counter max, period ~835Hz
apu_triangle_ctrl(0xFF);
apu_triangle_timer_lo(0x42);
apu_triangle_timer_hi(0x00);

// Noise: constant volume 15, period 6
apu_noise_ctrl(0x3F);
apu_noise_period(0x06);
apu_noise_length(0x18);

while (true) ;
