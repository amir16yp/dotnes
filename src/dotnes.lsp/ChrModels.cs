using MediatR;
using OmniSharp.Extensions.JsonRpc;

namespace dotnes.lsp;

[Method("dotnes/chr/getTiles")]
public sealed class GetTilesParams : IRequest<GetTilesResult>
{
    public string Uri { get; set; } = "";
}

public sealed class GetTilesResult
{
    public int TileCount { get; set; }
    public int BytesPerTile { get; set; } = 16;
    /// <summary>Each tile is a length-64 row-major array of palette indices 0–3.</summary>
    public List<List<int>> Tiles { get; set; } = new();
    public string? Error { get; set; }
}

[Method("dotnes/chr/setTiles")]
public sealed class SetTilesParams : IRequest<SetTilesResult>
{
    public string Uri { get; set; } = "";
    /// <summary>Each tile is a length-64 row-major array of palette indices 0–3.</summary>
    public List<List<int>> Tiles { get; set; } = new();
}

public sealed class SetTilesResult
{
    public bool Ok { get; set; }
    public string? Text { get; set; }
    public string? Error { get; set; }
}
