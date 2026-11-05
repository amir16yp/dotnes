using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Server;

namespace dotnes.lsp;

static class Program
{
    static async Task Main(string[] args)
    {
        var server = await LanguageServer.From(options => options
            .WithInput(Console.OpenStandardInput())
            .WithOutput(Console.OpenStandardOutput())
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.SetMinimumLevel(LogLevel.Warning);
            })
            .WithServices(services =>
            {
                services.AddSingleton<DocumentStore>();
            })
            .WithHandler<TextDocumentHandler>()
            .WithHandler<HoverHandler>()
            .WithHandler<GetTilesHandler>()
            .WithHandler<SetTilesHandler>()
        );

        await server.WaitForExit;
    }
}
