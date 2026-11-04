/*
OOP chase — sealed layout-class demo.

Move with the D-pad and collect coins. An enemy patrols left/right.
Player / Enemy / Coin are sealed NES layout classes (no GC).
*/

byte[] PALETTE = [
    DarkBlue,

    Azure, White, LightOrange, 0x0,
    Cyan, LightGray, LightCyan, 0x0,
    DarkGray, Gray, LightGray, 0x0,
    DarkRed, Red, LightRed, 0x0,

    Magenta, PaleMagenta, Black, 0x0,
    DarkGray, PaleOrange, LightRose, 0x0,
    Black, MediumGray, PaleGreen, 0x0,
    Black, LightOrange, LightGreen
];

pal_all(PALETTE);
oam_clear();

vram_adr(NTADR_A(2, 2));
vram_write("OOP CHASE");
vram_adr(NTADR_A(2, 4));
vram_write("COLLECT COINS!");

ppu_on_all();

var player = new Player();
player.X = 40;
player.Y = 120;

var enemy = new Enemy();
enemy.X = 180;
enemy.Y = 100;
enemy.GoingRight = 1;

var coin = new Coin();
coin.X = 100;
coin.Y = 80;

byte score = 0x30; // tile '0'

while (true)
{
    ppu_wait_nmi();
    oam_clear();

    PAD pad = pad_poll(0);
    byte mx = (byte)pad_dpad_x(pad);
    byte my = (byte)pad_dpad_y(pad);

    // Move player (read-modify-write on layout-class fields)
    player.X = (byte)(player.X + mx);
    player.Y = (byte)(player.Y + my);
    if (player.X < 8)
        player.X = 8;
    if (player.X > 240)
        player.X = 240;
    if (player.Y < 32)
        player.Y = 32;
    if (player.Y > 208)
        player.Y = 208;

    enemy.Patrol();

    // Collect coin
    byte dx = (byte)(player.X - coin.X);
    byte dy = (byte)(player.Y - coin.Y);
    if ((dx < 12 || dx > 244) && (dy < 12 || dy > 244))
    {
        score = (byte)(score + 1);
        coin.X = (byte)(coin.X + 37);
        if (coin.X < 16)
            coin.X = 40;
        if (coin.X > 232)
            coin.X = 40;
        coin.Y = (byte)(coin.Y + 53);
        if (coin.Y < 32)
            coin.Y = 64;
        if (coin.Y > 200)
            coin.Y = 64;
    }

    byte spr = 0;
    spr = oam_spr(player.X, player.Y, 0xD8, 0, spr);
    spr = oam_spr(enemy.X, enemy.Y, 0xDA, 1, spr);
    spr = oam_spr(coin.X, coin.Y, 0xD9, 2, spr);
    oam_spr(16, 16, score, 0, spr);
}

/// <summary>Player position — moved from the game loop via field access.</summary>
sealed class Player
{
    public byte X;
    public byte Y;
}

/// <summary>Horizontally patrolling enemy.</summary>
sealed class Enemy
{
    public byte X;
    public byte Y;
    public byte GoingRight;

    public void Patrol()
    {
        if (GoingRight != 0)
            X = (byte)(X + 1);
        if (GoingRight == 0)
            X = (byte)(X - 1);
        if (X > 220)
            GoingRight = 0;
        if (X < 20)
            GoingRight = 1;
    }
}

/// <summary>Collectible coin.</summary>
sealed class Coin
{
    public byte X;
    public byte Y;
}
