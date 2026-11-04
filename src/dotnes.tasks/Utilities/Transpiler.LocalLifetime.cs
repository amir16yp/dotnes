using System.Reflection.Metadata;
using dotnes.ObjectModel;

namespace dotnes;

/// <summary>
/// Lifetime-based packing of scalar locals so non-overlapping same-size locals
/// share NES RAM slots when <see cref="OptimizeCode"/> is enabled.
/// </summary>
partial class Transpiler
{
    /// <summary>
    /// Relative (frame-local) offsets for packed scalar locals, plus total packed
    /// scalar bytes. Empty map when packing is disabled or no eligible locals.
    /// </summary>
    internal readonly record struct ScalarPacking(
        IReadOnlyDictionary<int, int> RelativeOffsets,
        int PackedScalarBytes,
        IReadOnlyCollection<int> PackedLocals);

    /// <summary>
    /// Packs eligible scalar locals by live range. Returns an empty packing when
    /// <paramref name="optimize"/> is false.
    /// </summary>
    internal ScalarPacking ComputeScalarPacking(
        ILInstruction[] instructions,
        ReflectionCache reflectionCache,
        string methodName,
        bool optimize,
        ArrayStorageAnalysis? arrayStorage = null,
        ISet<int>? fixedArrayOffsets = null)
    {
        if (!optimize)
            return new ScalarPacking(new Dictionary<int, int>(), 0, Array.Empty<int>());

        var wordLocals = DetectWordLocals(instructions, reflectionCache, methodName);
        var excluded = CollectExcludedScalarLocals(instructions, reflectionCache, arrayStorage, fixedArrayOffsets);
        if (_closureStructLocalIndex >= 0)
            excluded.Add(_closureStructLocalIndex);

        var ranges = new List<(int Local, int First, int Last, int Size)>();
        var seen = new HashSet<int>();
        for (int i = 0; i < instructions.Length; i++)
        {
            int? idx = instructions[i].GetStlocIndex() ?? instructions[i].GetLdlocIndex();
            if (idx is null || excluded.Contains(idx.Value) || !seen.Add(idx.Value))
                continue;

            int first = i, last = i;
            bool hasDef = false;
            for (int j = 0; j < instructions.Length; j++)
            {
                if (instructions[j].GetStlocIndex() == idx.Value)
                {
                    hasDef = true;
                    if (j < first) first = j;
                    if (j > last) last = j;
                }
                else if (instructions[j].GetLdlocIndex() == idx.Value)
                {
                    if (j < first) first = j;
                    if (j > last) last = j;
                }
            }
            // Locals that are only loaded (parameters / undef) stay on the bump path.
            if (!hasDef)
                continue;

            int size = wordLocals.Contains(idx.Value) ? 2 : 1;
            ranges.Add((idx.Value, first, last, size));
        }

        ranges.Sort((a, b) =>
        {
            int cmp = a.First.CompareTo(b.First);
            return cmp != 0 ? cmp : a.Local.CompareTo(b.Local);
        });

        // Interval first/last ranges are unsafe to reuse across loops: a local can be
        // live on a back-edge after its last textual use in a linear scan. Fail closed
        // when any backward branch is present and assign unique dense slots only.
        bool allowReuse = !HasBackwardBranch(instructions);

        var offsets = new Dictionary<int, int>();
        var active = new List<(int Local, int Offset, int Size, int Last)>();
        var freeBytes = new List<int>();
        var freeWords = new List<int>();
        int nextOffset = 0;

        foreach (var (local, first, last, size) in ranges)
        {
            if (allowReuse)
            {
                for (int a = active.Count - 1; a >= 0; a--)
                {
                    if (active[a].Last < first)
                    {
                        if (active[a].Size == 1)
                            freeBytes.Add(active[a].Offset);
                        else
                            freeWords.Add(active[a].Offset);
                        active.RemoveAt(a);
                    }
                }
            }

            int offset;
            if (allowReuse && size == 1 && freeBytes.Count > 0)
            {
                freeBytes.Sort();
                offset = freeBytes[0];
                freeBytes.RemoveAt(0);
            }
            else if (allowReuse && size == 2 && freeWords.Count > 0)
            {
                freeWords.Sort();
                offset = freeWords[0];
                freeWords.RemoveAt(0);
            }
            else
            {
                offset = nextOffset;
                nextOffset += size;
            }

            offsets[local] = offset;
            active.Add((local, offset, size, last));
        }

        return new ScalarPacking(offsets, nextOffset, offsets.Keys.ToArray());
    }

    static bool HasBackwardBranch(ILInstruction[] instructions)
    {
        var offsets = new Dictionary<int, int>(); // IL offset → index
        for (int i = 0; i < instructions.Length; i++)
            offsets[instructions[i].Offset] = i;
        for (int i = 0; i < instructions.Length; i++)
        {
            foreach (int target in ILValueAnalysis.GetBranchTargets(instructions[i]))
            {
                if (offsets.TryGetValue(target, out int targetIndex) && targetIndex <= i)
                    return true;
            }
        }
        return false;
    }

    static HashSet<int> CollectExcludedScalarLocals(
        ILInstruction[] instructions,
        ReflectionCache reflectionCache,
        ArrayStorageAnalysis? arrayStorage,
        ISet<int>? fixedArrayOffsets)
    {
        var excluded = new HashSet<int>();

        // Address-taken locals cannot share storage.
        for (int i = 0; i < instructions.Length; i++)
        {
            if (instructions[i].OpCode is ILOpCode.Ldloca or ILOpCode.Ldloca_s
                && instructions[i].Integer is int addrLocal)
                excluded.Add(addrLocal);
        }

        // Any local that stores a newarr (directly or through dup/init) is not a scalar.
        var values = new ILValueAnalysis(instructions, reflectionCache);
        for (int i = 0; i < instructions.Length; i++)
        {
            if (instructions[i].OpCode != ILOpCode.Newarr)
                continue;
            ExcludeNewarrConsumers(instructions, values, i, excluded);
        }

        // Locals used as the array operand of ldelem/stelem/ldlen are not scalars.
        for (int i = 0; i < instructions.Length; i++)
        {
            var op = instructions[i].OpCode;
            if (op is not (ILOpCode.Ldelem or ILOpCode.Ldelem_u1 or ILOpCode.Ldelem_i1 or ILOpCode.Ldelem_i2
                or ILOpCode.Ldelem_u2 or ILOpCode.Ldelem_i4 or ILOpCode.Ldelem_ref
                or ILOpCode.Stelem or ILOpCode.Stelem_i1 or ILOpCode.Stelem_i2 or ILOpCode.Stelem_i4
                or ILOpCode.Stelem_ref or ILOpCode.Ldlen))
                continue;
            if (values.Inputs[i].Length == 0)
                continue;
            int arrayProducer = values.Inputs[i][0];
            if (arrayProducer >= 0 && instructions[arrayProducer].GetLdlocIndex() is int arrayLocal)
                excluded.Add(arrayLocal);
        }

        if (arrayStorage is not null)
        {
            for (int i = 0; i < instructions.Length; i++)
            {
                if (instructions[i].GetStlocIndex() is int st
                    && arrayStorage.GetInputStorage(instructions, i, 0)
                        is ArrayStorage.Ram or ArrayStorage.Rom or ArrayStorage.Parameter)
                    excluded.Add(st);
                if (instructions[i].GetLdlocIndex() is int ld
                    && arrayStorage.GetStorage(instructions, i)
                        is ArrayStorage.Ram or ArrayStorage.Rom or ArrayStorage.Parameter)
                    excluded.Add(ld);
            }
        }

        _ = fixedArrayOffsets;
        return excluded;
    }

    static void ExcludeNewarrConsumers(ILInstruction[] instructions, ILValueAnalysis values,
        int newarrIndex, HashSet<int> excluded)
    {
        var pending = new Stack<int>();
        var seen = new HashSet<int> { newarrIndex };
        pending.Push(newarrIndex);
        while (pending.Count > 0)
        {
            int producer = pending.Pop();
            foreach (int consumer in values.Consumers[producer])
            {
                if (instructions[consumer].GetStlocIndex() is int st)
                    excluded.Add(st);
                if (instructions[consumer].OpCode == ILOpCode.Dup && seen.Add(consumer))
                    pending.Push(consumer);
            }
        }
    }

    /// <summary>
    /// Seeds a writer with absolute addresses for packed scalars. Advances
    /// <see cref="LocalVariableManager.LocalCount"/> to cover the packed region.
    /// </summary>
    static void ApplyScalarPacking(
        LocalVariableManager variables,
        ScalarPacking packing,
        ushort frameBase,
        HashSet<int> wordLocals)
    {
        if (packing.RelativeOffsets.Count == 0)
        {
            // Still reserve nothing; LocalCount already at frame base offset.
            return;
        }

        foreach (var pair in packing.RelativeOffsets.OrderBy(p => p.Value).ThenBy(p => p.Key))
        {
            bool isWord = wordLocals.Contains(pair.Key);
            ushort address = (ushort)(frameBase + pair.Value);
            variables.PreassignScalarLocal(pair.Key, address, isWord);
        }

        int packedEnd = packing.PackedScalarBytes;
        int slotsPastBase = frameBase - variables.BaseAddress + packedEnd;
        // frameBase may equal LocalStackBase + existing LocalCount; ensure high water.
        if (slotsPastBase > variables.LocalCount)
            variables.LocalCount = slotsPastBase;
    }
}
