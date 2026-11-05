using OmniSharp.Extensions.JsonRpc;

namespace dotnes.lsp;

sealed class GetTilesHandler : IJsonRpcRequestHandler<GetTilesParams, GetTilesResult>
{
    readonly DocumentStore _documents;

    public GetTilesHandler(DocumentStore documents) => _documents = documents;

    public Task<GetTilesResult> Handle(GetTilesParams request, CancellationToken cancellationToken)
    {
        var result = new GetTilesResult();
        try
        {
            var text = _documents.GetOrRead(request.Uri);
            if (string.IsNullOrEmpty(text))
            {
                result.Error = "Document is empty or missing.";
                return Task.FromResult(result);
            }

            if (!ChrAssembly.IsCharsDocument(text))
            {
                result.Error = "Not a CHARS segment document.";
                return Task.FromResult(result);
            }

            var bytes = ChrAssembly.Parse(text);
            var tiles = ChrCodec.DecodeTiles(bytes);
            result.TileCount = tiles.Count;
            result.Tiles = tiles
                .Select(t => ChrCodec.FlattenTile(t).Select(b => (int)b).ToList())
                .ToList();
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }

        return Task.FromResult(result);
    }
}

sealed class SetTilesHandler : IJsonRpcRequestHandler<SetTilesParams, SetTilesResult>
{
    readonly DocumentStore _documents;

    public SetTilesHandler(DocumentStore documents) => _documents = documents;

    public Task<SetTilesResult> Handle(SetTilesParams request, CancellationToken cancellationToken)
    {
        var result = new SetTilesResult();
        try
        {
            if (request.Tiles == null || request.Tiles.Count == 0)
            {
                result.Error = "No tiles provided.";
                return Task.FromResult(result);
            }

            var decoded = new List<byte[,]>(request.Tiles.Count);
            foreach (var flat in request.Tiles)
            {
                var flatBytes = flat.Select(v => (byte)v).ToArray();
                decoded.Add(ChrCodec.UnflattenTile(flatBytes));
            }

            var encoded = ChrCodec.EncodeTiles(decoded);
            var text = ChrAssembly.Format(encoded);
            _documents.Set(request.Uri, text);

            var path = DocumentStore.UriToPath(request.Uri);
            if (path != null)
                File.WriteAllText(path, text);

            result.Ok = true;
            result.Text = text;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }

        return Task.FromResult(result);
    }
}
