using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Xunit.Abstractions;

namespace dotnes.tests;

/// <summary>
/// Coverage for NESLib C# bodies: stub detection, use-gated inclusion, single emission.
/// </summary>
public class NesLibMethodsTests : RoslynTests
{
    public NesLibMethodsTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void IsThrowOnlyStub_DetectsPalCol()
    {
        string path = typeof(NESLib).Assembly.Location;
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();

        var method = typeof(NESLib).GetMethod(nameof(NESLib.pal_col))!;
        var handle = MetadataTokens.MethodDefinitionHandle(method.MetadataToken);
        var def = reader.GetMethodDefinition(handle);
        Assert.True(Transpiler.IsThrowOnlyStub(pe, def));
    }

    [Fact]
    public void IsThrowOnlyStub_RejectsGetFrameCountBody()
    {
        string path = typeof(NESLib).Assembly.Location;
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();

        var method = typeof(NESLib).GetMethod(nameof(NESLib.get_frame_count))!;
        var handle = MetadataTokens.MethodDefinitionHandle(method.MetadataToken);
        var def = reader.GetMethodDefinition(handle);
        Assert.False(Transpiler.IsThrowOnlyStub(pe, def));
    }

    [Fact]
    public void UnusedNesLibWrapper_IsNotLoadedOrEmitted()
    {
        using var transpiler = BuildProgram(
            """
            pal_col(0, 0x0F);
            ppu_on_all();
            while (true) ;
            """, out var program);

        Assert.DoesNotContain("get_frame_count", transpiler.NesLibImplementedMethods);
        Assert.DoesNotContain("clear_vram_buffer", transpiler.NesLibImplementedMethods);
        Assert.DoesNotContain("get_pad_new", transpiler.NesLibImplementedMethods);
        Assert.Null(program.GetBlock("get_frame_count"));
        Assert.Null(program.GetBlock("clear_vram_buffer"));
        Assert.Null(program.GetBlock("get_pad_new"));
    }

    [Fact]
    public void UsedNesLibWrapper_LoadsCalleeIntoUsedMethods()
    {
        using var transpiler = BuildProgram(
            """
            byte f = get_frame_count();
            poke(0x6000, f);
            while (true) ;
            """, out var program);

        Assert.Contains("get_frame_count", transpiler.NesLibImplementedMethods);
        Assert.Contains("nesclock", transpiler.UsedMethods);
        Assert.NotNull(program.GetBlock("get_frame_count"));
        Assert.NotNull(program.GetBlock("nesclock"));
    }

    [Fact]
    public void PalSprCol_OffsetsIndexAndCallsPalCol()
    {
        using var transpiler = BuildProgram(
            """
            pal_spr_col(3, 0x30);
            while (true) ;
            """, out var program);

        Assert.Contains("pal_spr_col", transpiler.NesLibImplementedMethods);
        Assert.Single(program.Blocks, b => b.Label == "pal_spr_col");
        Assert.Contains("pal_col", transpiler.UsedMethods);
    }

    [Fact]
    public void Pad1Poll_IsThinWrapperOverPadPoll()
    {
        using var transpiler = BuildProgram(
            """
            PAD p = pad1_poll();
            oam_spr((byte)p, 0, 0, 0, 0);
            while (true) ;
            """, out var program);

        Assert.Contains("pad1_poll", transpiler.NesLibImplementedMethods);
        Assert.Contains("pad_poll", transpiler.UsedMethods);
        Assert.NotNull(program.GetBlock("pad1_poll"));
        Assert.NotNull(program.GetBlock("pad_poll"));
    }

    [Fact]
    public void PadPollMsb_EmitsRolBuiltin()
    {
        using var transpiler = BuildProgram(
            """
            PADMSB p = pad_poll_msb(0);
            if ((p & PADMSB.START) != 0) { }
            while (true) ;
            """, out var program);

        Assert.Contains("pad_poll_msb", transpiler.UsedMethods);
        Assert.NotNull(program.GetBlock("pad_poll_msb"));
        Assert.Null(program.GetBlock("pad_poll")); // LSB poll not pulled in
    }

    [Fact]
    public void PadReverseBits_ConvertsBetweenLayouts()
    {
        using var transpiler = BuildProgram(
            """
            PADMSB m = pad_poll_msb(0);
            PAD l = pad_from_msb(m);
            if ((l & PAD.A) != 0) { }
            while (true) ;
            """, out var program);

        Assert.Contains("pad_reverse_bits", transpiler.UsedMethods);
        Assert.NotNull(program.GetBlock("pad_reverse_bits"));
        Assert.NotNull(program.GetBlock("pad_poll_msb"));
    }

    [Fact]
    public void ApuStopAll_StopsBothPulseChannels()
    {
        using var transpiler = BuildProgram(
            """
            apu_stop_all();
            while (true) ;
            """, out var program);

        Assert.Contains("apu_stop_all", transpiler.NesLibImplementedMethods);
        Assert.NotNull(program.GetBlock("apu_stop_all"));
        Assert.Single(program.Blocks, b => b.Label == "apu_stop_all");
    }
}
