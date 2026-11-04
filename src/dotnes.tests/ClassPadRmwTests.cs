using Xunit.Abstractions;

namespace dotnes.tests;

public class ClassPadRmwTests : ExecutionTests
{
    public ClassPadRmwTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void OamSpr_ClassFields_LoadFieldAddressesNotObjectRef()
    {
        // Regression: EmitOamSprDecsp4 skipped ldfld and used the object-ref local
        // for both X and Y, so sprites ignored player.X/Y updates (oopchase "input dead").
        var bytes = GetProgramBytes(
            """
            var player = new Player();
            player.X = 40;
            player.Y = 120;
            ppu_on_all();
            while (true)
            {
                oam_spr(player.X, player.Y, 0xD8, 0, 0);
            }
            sealed class Player { public byte X; public byte Y; }
            """,
            optimizationLevel: Microsoft.CodeAnalysis.OptimizationLevel.Debug);
        var hex = Convert.ToHexString(bytes);

        // Init stores X=40 and Y=120 to consecutive body addresses.
        int xInit = hex.IndexOf("A9288D", StringComparison.Ordinal); // LDA #$28; STA abs
        Assert.True(xInit >= 0, "missing player.X = 40 store");
        string xAddr = hex.Substring(xInit + 6, 4); // lo hi of STA abs after A9288D

        int yInit = hex.IndexOf("A9788D", StringComparison.Ordinal);
        Assert.True(yInit >= 0, "missing player.Y = 120 store");
        string yAddr = hex.Substring(yInit + 6, 4);

        Assert.NotEqual(xAddr, yAddr);

        // oam_spr must LDA those field addresses (not the object-ref local twice).
        Assert.Contains("AD" + xAddr, hex, StringComparison.Ordinal);
        Assert.Contains("AD" + yAddr, hex, StringComparison.Ordinal);
    }

    [Fact]
    public void ClassField_AddLocal_UpdatesField()
    {
        var cpu = ExecuteProgram(
            """
            var player = new Player();
            player.X = 40;
            byte mx = 1;
            player.X = (byte)(player.X + mx);
            poke(0x6000, player.X);
            test_stop(); while (true) ;
            static extern void test_stop();
            sealed class Player { public byte X; public byte Y; }
            """);
        Assert.Equal(41, cpu.Memory[0x6000]);
    }
}
