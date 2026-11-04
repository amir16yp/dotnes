using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace dotnes;

/// <summary>
/// Loads non-stub NESLib method bodies from the implementation assembly and merges them
/// into <see cref="UserMethods"/> so they transpile like game helpers.
/// </summary>
partial class Transpiler
{
    /// <summary>
    /// NESLib methods whose C# bodies are being transpiled (not throw-only stubs).
    /// Used to skip conflicting BuiltInSubroutines / ILDispatch intrinsics.
    /// </summary>
    public HashSet<string> NesLibImplementedMethods { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Compile-time macros already handled by ILDispatch; never transpile their C# bodies.
    /// </summary>
    static readonly HashSet<string> CompileTimeNesLibMacros = new(StringComparer.Ordinal)
    {
        nameof(NESLib.NTADR_A),
        nameof(NESLib.NTADR_B),
        nameof(NESLib.NTADR_C),
        nameof(NESLib.NTADR_D),
        nameof(NESLib.NT_ATTR_A),
        nameof(NESLib.NT_ATTR_B),
        nameof(NESLib.NT_ATTR_C),
        nameof(NESLib.NT_ATTR_D),
        nameof(NESLib.MSB),
        nameof(NESLib.LSB),
    };

    void LoadNesLibImplementedMethods()
    {
        string path = typeof(NESLib).Assembly.Location;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            _logger.WriteLine($"NESLib implementation assembly not found; skipping C# body load.");
            return;
        }

        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();

        TypeDefinition? neslibType = null;
        foreach (var handle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(handle);
            if (reader.GetString(type.Namespace) != nameof(NES) || reader.GetString(type.Name) != nameof(NESLib))
                continue;
            neslibType = type;
            break;
        }
        if (neslibType is null)
            throw new InvalidOperationException("Did not find type NES.NESLib in the implementation assembly!");

        var methodsByName = new Dictionary<string, List<MethodDefinition>>(StringComparer.Ordinal);
        foreach (var handle in neslibType.Value.GetMethods())
        {
            var method = reader.GetMethodDefinition(handle);
            string name = reader.GetString(method.Name);
            if (!methodsByName.TryGetValue(name, out var list))
                methodsByName[name] = list = new List<MethodDefinition>();
            list.Add(method);
        }

        var emptyArrays = new Dictionary<string, ArrayValue>(StringComparer.Ordinal);
        var worklist = new Queue<string>(UsedMethods);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        while (worklist.Count > 0)
        {
            string name = worklist.Dequeue();
            if (!seen.Add(name) || CompileTimeNesLibMacros.Contains(name))
                continue;
            if (!methodsByName.TryGetValue(name, out var overloads))
                continue;

            MethodDefinition? chosen = null;
            foreach (var candidate in overloads)
            {
                if (IsThrowOnlyStub(pe, candidate))
                    continue;
                chosen = candidate;
                break;
            }
            if (chosen is null)
                continue;

            var method = chosen.Value;
            ReadNumericTypes(pe, reader, method, name);
            var instructions = ReadMethodBody(pe, reader, method, emptyArrays).ToArray();
            UserMethods[name] = instructions;
            NesLibImplementedMethods.Add(name);

            var signature = method.DecodeSignature(new ArrayTypeDecoder(), null);
            int paramCount = signature.ParameterTypes.Length;
            bool hasReturnValue = signature.ReturnType != ArraySignatureType.Void;
            bool[] isArrayParam = signature.ParameterTypes.Select(type => type == ArraySignatureType.ByteArray).ToArray();
            UserMethodMetadata[name] = (paramCount, hasReturnValue, isArrayParam);

            _logger.WriteLine($"NESLib C# body '{name}': {instructions.Length} IL instructions, {paramCount} params");

            foreach (var instruction in instructions)
            {
                if (instruction.OpCode != ILOpCode.Call || instruction.String is not string callee)
                    continue;
                UsedMethods.Add(callee);
                if (!seen.Contains(callee))
                    worklist.Enqueue(callee);
            }
        }
    }

    internal static bool IsThrowOnlyStub(PEReader pe, MethodDefinition method)
    {
        if (method.RelativeVirtualAddress == 0)
            return true;

        var body = pe.GetMethodBody(method.RelativeVirtualAddress);
        var blob = body.GetILReader();
        var opcodes = new List<ILOpCode>(4);
        while (blob.RemainingBytes > 0)
        {
            var op = DecodeOpCode(ref blob);
            opcodes.Add(op);
            SkipOperand(ref blob, op);
        }

        // `=> throw null!` compiles to ldnull + throw (optional leading nop).
        int i = 0;
        if (i < opcodes.Count && opcodes[i] == ILOpCode.Nop)
            i++;
        return i + 1 < opcodes.Count
            && opcodes[i] == ILOpCode.Ldnull
            && opcodes[i + 1] == ILOpCode.Throw
            && i + 2 == opcodes.Count;
    }

    static void SkipOperand(ref BlobReader blob, ILOpCode opCode)
    {
        switch (GetOperandType(opCode))
        {
            case OperandType.None:
                break;
            case OperandType.ShortI:
            case OperandType.ShortVariable:
            case OperandType.ShortBrTarget:
                blob.ReadByte();
                break;
            case OperandType.Variable:
                blob.ReadInt16();
                break;
            case OperandType.BrTarget:
            case OperandType.Field:
            case OperandType.I:
            case OperandType.Method:
            case OperandType.Sig:
            case OperandType.Tok:
            case OperandType.Type:
            case OperandType.String:
            case OperandType.ShortR:
                blob.ReadInt32();
                break;
            case OperandType.I8:
            case OperandType.R:
                blob.ReadInt64();
                break;
            case OperandType.Switch:
                {
                    uint n = blob.ReadUInt32();
                    blob.Offset += (int)(n * 4);
                }
                break;
            default:
                throw new NotSupportedException($"Cannot skip operand for {opCode}");
        }
    }
}
