using Microsoft.CodeAnalysis;
using Xunit.Abstractions;

namespace dotnes.tests;

public class ClassOopTests : RoslynTests
{
    public ClassOopTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void NewAndFieldWrite_EmitsAbsoluteStores()
    {
        var bytes = GetProgramBytes(
            """
            var p = new Player();
            p.X = 2;
            p.Y = 0x14;
            pal_col(0, p.X);
            pal_col(1, p.Y);
            ppu_on_all();
            while (true) ;

            sealed class Player
            {
                public byte X;
                public byte Y;
            }
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A902", hex); // LDA #$02
        Assert.Contains("A914", hex); // LDA #$14
        Assert.Contains("8D", hex);   // STA absolute (object field in BSS)
    }

    [Fact]
    public void InstanceMethod_MutatesField()
    {
        var bytes = GetProgramBytes(
            """
            var p = new Player();
            p.X = 2;
            p.Move(1);
            pal_col(0, p.X);
            ppu_on_all();
            while (true) ;

            sealed class Player
            {
                public byte X;
                public byte Y;
                public void Move(byte dx) => X += dx;
            }
            """);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A902", hex); // LDA #$02 for X = 2
        Assert.Contains("A901", hex); // LDA #$01 for Move(1)
        Assert.Contains("20", hex);   // JSR to Player_Move / ctor
    }

    [Fact]
    public void UnsealedClass_ThrowsAtTranspile()
    {
        Assert.ThrowsAny<Exception>(() => GetProgramBytes(
            """
            var p = new Player();
            while (true) ;

            class Player
            {
                public byte X;
            }
            """));
    }

    [Fact]
    public void OopChaseSample_Transpiles()
    {
        var bytes = GetProgramBytes(
            """
            pal_col(0, 0x02);
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

            byte score = 0x30;

            while (true)
            {
                ppu_wait_nmi();
                oam_clear();

                PAD pad = pad_poll(0);
                byte mx = (byte)pad_dpad_x(pad);
                byte my = (byte)pad_dpad_y(pad);

                player.X = (byte)(player.X + mx);
                player.Y = (byte)(player.Y + my);
                if (player.X < 8)
                    player.X = 8;
                if (player.X > 240)
                    player.X = 240;

                enemy.Patrol();

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

            sealed class Player
            {
                public byte X;
                public byte Y;
            }

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

            sealed class Coin
            {
                public byte X;
                public byte Y;
            }
            """,
            optimizationLevel: OptimizationLevel.Debug);
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void FieldInitAfterStloc_DebugPattern()
    {

        // Debug IL: newobj → stloc → ldloc → stfld (no dup). Must still absolute-store fields.
        var bytes = GetProgramBytes(
            """
            var player = new Player();
            player.X = 40;
            player.Y = 120;
            pal_col(0, player.X);
            ppu_on_all();
            while (true) ;

            sealed class Player
            {
                public byte X;
                public byte Y;
            }
            """,
            optimizationLevel: OptimizationLevel.Debug);
        Assert.NotNull(bytes);
        var hex = Convert.ToHexString(bytes);
        Assert.Contains("A928", hex); // LDA #$28 (40)
        Assert.Contains("A978", hex); // LDA #$78 (120)
    }
}
