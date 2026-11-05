using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace dotnes.lsp;

sealed class HoverHandler : HoverHandlerBase
{
    readonly DocumentStore _documents;

    public HoverHandler(DocumentStore documents) => _documents = documents;

    public override Task<Hover?> Handle(HoverParams request, CancellationToken cancellationToken)
    {
        var uri = request.TextDocument.Uri.ToString();
        var text = _documents.GetOrRead(uri);
        if (!ChrAssembly.IsCharsDocument(text))
            return Task.FromResult<Hover?>(null);

        var lines = text.Split('\n');
        int line = request.Position.Line;
        if (line < 0 || line >= lines.Length)
            return Task.FromResult<Hover?>(null);

        // Count .byte lines before this one to find tile index (16 bytes per tile / line in decompiler format).
        int byteOffset = 0;
        for (int i = 0; i <= line && i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (!trimmed.StartsWith(".byte ", StringComparison.Ordinal))
                continue;

            if (i == line)
            {
                int tileIndex = byteOffset / ChrCodec.BytesPerTile;
                int remainder = byteOffset % ChrCodec.BytesPerTile;
                string note = remainder == 0
                    ? $"Tile ${tileIndex:X2} ({tileIndex})"
                    : $"Tile ${tileIndex:X2} ({tileIndex}), mid-tile offset {remainder}";

                try
                {
                    var bytes = ChrAssembly.Parse(text);
                    if (tileIndex * ChrCodec.BytesPerTile + ChrCodec.BytesPerTile <= bytes.Length)
                    {
                        var pixels = ChrCodec.DecodeTile(bytes, tileIndex * ChrCodec.BytesPerTile);
                        note += "\n```\n" + AsciiPreview(pixels) + "\n```";
                    }
                }
                catch
                {
                    // preview optional
                }

                return Task.FromResult<Hover?>(new Hover
                {
                    Contents = new MarkedStringsOrMarkupContent(new MarkupContent
                    {
                        Kind = MarkupKind.Markdown,
                        Value = note,
                    }),
                });
            }

            // Approximate byte count from this line
            byteOffset += CountBytesOnLine(trimmed);
        }

        return Task.FromResult<Hover?>(null);
    }

    protected override HoverRegistrationOptions CreateRegistrationOptions(
        HoverCapability capability,
        ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = TextDocumentSelector.ForPattern("**/*.s"),
        };

    static int CountBytesOnLine(string line)
    {
        int count = 0;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '$')
                count++;
        }
        return count;
    }

    static string AsciiPreview(byte[,] pixels)
    {
        const string shades = " .:+=";
        var rows = new string[8];
        for (int y = 0; y < 8; y++)
        {
            var chars = new char[8];
            for (int x = 0; x < 8; x++)
            {
                int v = Math.Clamp(pixels[y, x], (byte)0, (byte)3);
                chars[x] = shades[v];
            }
            rows[y] = new string(chars);
        }
        return string.Join('\n', rows);
    }
}
