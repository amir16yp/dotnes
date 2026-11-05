using dotnes;

if (args.Length == 0)
{
    Commands.PrintHelp();
    return 0;
}

string command = args[0];
string[] rest = args.Skip(1).ToArray();

return command switch
{
    "new" => Commands.New(rest),
    "build" => Commands.Build(rest),
    "run" => Commands.Run(rest),
    "decompile" => Commands.Decompile(rest),
    "transpile" => Commands.Transpile(rest),
    "-h" or "--help" or "help" => Help(),
    // Legacy: `dotnes app.dll chr.s`
    _ when args.Length >= 2 && File.Exists(args[0]) => Commands.Transpile(args),
    _ => Unknown(command),
};

static int Help()
{
    Commands.PrintHelp();
    return 0;
}

static int Unknown(string command)
{
    Console.Error.WriteLine($"Unknown command '{command}'.");
    Console.Error.WriteLine();
    Commands.PrintHelp();
    return 1;
}
