using MediatR;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;

namespace dotnes.lsp;

sealed class TextDocumentHandler : TextDocumentSyncHandlerBase
{
    readonly DocumentStore _documents;
    readonly ILogger<TextDocumentHandler> _logger;
    readonly OmniSharp.Extensions.LanguageServer.Protocol.Server.ILanguageServerFacade _server;

    public TextDocumentHandler(
        DocumentStore documents,
        ILogger<TextDocumentHandler> logger,
        OmniSharp.Extensions.LanguageServer.Protocol.Server.ILanguageServerFacade server)
    {
        _documents = documents;
        _logger = logger;
        _server = server;
    }

    public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri) =>
        new(uri, "ca65");

    public override Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken cancellationToken)
    {
        var uri = request.TextDocument.Uri.ToString();
        _documents.Set(uri, request.TextDocument.Text);
        PublishDiagnostics(uri, request.TextDocument.Text);
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken cancellationToken)
    {
        var uri = request.TextDocument.Uri.ToString();
        if (request.ContentChanges.Any(c => c.Range == null))
        {
            var text = request.ContentChanges.Last(c => c.Range == null).Text;
            _documents.Set(uri, text);
            PublishDiagnostics(uri, text);
        }
        else if (_documents.TryGet(uri, out var existing))
        {
            // Full sync only for v1; ignore incremental if somehow sent.
            _logger.LogDebug("Ignoring incremental change for {Uri}", uri);
            PublishDiagnostics(uri, existing);
        }

        return Unit.Task;
    }

    public override Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken cancellationToken)
    {
        var uri = request.TextDocument.Uri.ToString();
        _documents.Remove(uri);
        _server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = request.TextDocument.Uri,
            Diagnostics = new Container<Diagnostic>(),
        });
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidSaveTextDocumentParams request, CancellationToken cancellationToken) =>
        Unit.Task;

    protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(
        TextSynchronizationCapability capability,
        ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = TextDocumentSelector.ForPattern("**/*.s"),
            Change = TextDocumentSyncKind.Full,
            Save = new SaveOptions { IncludeText = false },
        };

    void PublishDiagnostics(string uri, string text)
    {
        var diagnostics = new List<Diagnostic>();

        if (!ChrAssembly.IsCharsDocument(text))
        {
            _server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
            {
                Uri = DocumentUri.From(uri),
                Diagnostics = diagnostics,
            });
            return;
        }

        try
        {
            var bytes = ChrAssembly.Parse(text);
            if (bytes.Length == 0)
            {
                diagnostics.Add(new Diagnostic
                {
                    Severity = DiagnosticSeverity.Warning,
                    Message = "CHARS segment has no .byte data.",
                    Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
                        new Position(0, 0), new Position(0, 1)),
                });
            }
            else if (bytes.Length % ChrCodec.BytesPerTile != 0)
            {
                diagnostics.Add(new Diagnostic
                {
                    Severity = DiagnosticSeverity.Warning,
                    Message = $"CHARS data length ({bytes.Length}) is not a multiple of {ChrCodec.BytesPerTile} (one tile).",
                    Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
                        new Position(0, 0), new Position(0, 1)),
                });
            }
        }
        catch (Exception ex)
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Message = $"Failed to parse CHARS assembly: {ex.Message}",
                Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
                    new Position(0, 0), new Position(0, 1)),
            });
        }

        _server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = DocumentUri.From(uri),
            Diagnostics = diagnostics,
        });
    }
}
