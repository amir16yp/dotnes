using System.Reflection.Metadata;
using dotnes.ObjectModel;
using static dotnes.NESConstants;
using static dotnes.ObjectModel.Asm;

namespace dotnes;

/// <summary>
/// NES layout-class OOP: <c>newobj</c> bump allocation, object-ref locals, instance
/// <c>ldfld</c>/<c>stfld</c>, and <c>callvirt</c> → direct <c>JSR</c>.
/// </summary>
partial class IL2NESWriter
{
    /// <summary>
    /// True when <paramref name="name"/> is a known layout-class instance method / ctor label
    /// (<c>Type_Method</c> / <c>Type_ctor</c>).
    /// </summary>
    bool IsClassInstanceMethod(string name)
    {
        int us = name.IndexOf('_');
        return us > 0 && UserMethodNames.Contains(name)
            && StructLayouts.ContainsKey(name.Substring(0, us));
    }

    /// <summary>
    /// Handles <c>newobj Type::.ctor(...)</c> for sealed layout classes.
    /// Operand is the mangled ctor name (<c>Player_ctor</c>).
    /// </summary>
    void HandleNewobjClass(string ctorName, ILInstruction instruction)
    {
        if (ctorName is "Object_ctor" or "Program_ctor")
        {
            // Base Object/.ctor — no-op; `this` already on stack / in A:X from outer newobj.
            return;
        }

        int us = ctorName.LastIndexOf("_ctor", StringComparison.Ordinal);
        if (us <= 0 || !ctorName.EndsWith("_ctor", StringComparison.Ordinal))
            throw new TranspileException(
                $"newobj '{ctorName}' is not a supported layout-class constructor.", MethodName);

        string typeName = ctorName.Substring(0, us);
        if (!StructLayouts.ContainsKey(typeName))
            throw new TranspileException(
                $"Unknown layout class '{typeName}' for newobj.", MethodName);

        ushort baseAddr = Variables.AllocateObjectBody(typeName);
        int ctorArgs = _reflectionCache.GetNumberOfArguments(ctorName); // includes this
        int userArgs = Math.Max(0, ctorArgs - 1);

        // Pop ctor arguments (excluding this) from the eval stack — they were pushed before newobj.
        var argValues = new int[userArgs];
        for (int i = userArgs - 1; i >= 0; i--)
            argValues[i] = Stack.Count > 0 ? Stack.Pop() : 0;

        // Parameterized ctor: push user args, then this in A:X as last arg (word).
        // For the common parameterless case: just JSR with this in A:X.
        if (userArgs > 0)
        {
            // Conservative: require compile-time constant args for v1 parameterized ctors
            for (int i = 0; i < userArgs - 1; i++)
            {
                Emit(Opcode.LDA, AddressMode.Immediate, (byte)(argValues[i] & 0xFF));
                EmitJSR("pusha");
                UsedMethods?.Add("pusha");
            }
            if (userArgs >= 1)
            {
                // last user arg stays in A until we need to pushax this — push it first
                Emit(Opcode.LDA, AddressMode.Immediate, (byte)(argValues[userArgs - 1] & 0xFF));
                EmitJSR("pusha");
                UsedMethods?.Add("pusha");
            }
        }

        // Load object address into A:X and call ctor (this = last / only arg, 16-bit → pushax prologue)
        Emit(Opcode.LDA, AddressMode.Immediate, (byte)(baseAddr & 0xFF));
        Emit(Opcode.LDX, AddressMode.Immediate, (byte)(baseAddr >> 8));
        EmitWithLabel(Opcode.JSR, AddressMode.Absolute, ctorName);

        // newobj result: object reference (address) on eval stack and in A:X
        Stack.Push(baseAddr);
        _pendingObjectBase = baseAddr;
        _ushortInAX = true;
        _runtimeValueInA = true;
        _immediateInA = null;
    }

    /// <summary>
    /// Records that a local holds a class object reference at a known body address.
    /// </summary>
    void TrackClassLocal(int localIndex, ushort bodyAddr, string? typeName = null)
    {
        _classLocalBases[localIndex] = bodyAddr;
        if (typeName != null)
            _classLocalTypes[localIndex] = typeName;
        WordLocals.Add(localIndex);
    }

    /// <summary>
    /// After ldloc of a class ref: park the body address for upcoming field/method use
    /// without emitting a RAM load (address is a compile-time constant).
    /// </summary>
    bool TryLoadClassLocal(int localIndex)
    {
        if (!_classLocalBases.TryGetValue(localIndex, out ushort body))
            return false;
        _pendingObjectBase = body;
        Stack.Push(body);
        _lastLoadedLocalIndex = localIndex;
        _ushortInAX = false;
        _runtimeValueInA = false;
        _immediateInA = null;
        return true;
    }

    /// <summary>
    /// If the value being stored is a class object ref, record the local → body mapping.
    /// </summary>
    internal void NoteClassLocalStore(int localIndex, int value)
    {
        if (_pendingObjectBase is ushort body && value == body)
            TrackClassLocal(localIndex, body);
        else if (_classLocalBases.Values.Contains((ushort)value))
            TrackClassLocal(localIndex, (ushort)value);
    }

    /// <summary>
    /// Handles call/callvirt to a layout-class instance method.
    /// Eval stack: [... thisAddr, args...]. Last arg may already be in A.
    /// </summary>
    bool TryEmitClassInstanceCall(string methodName)
    {
        // Base Object/.ctor (and Program) — no-op; discard `this` on the eval stack.
        if (methodName is "Object_ctor" or "Program_ctor"
            || methodName is "Object..ctor" or "Program..ctor")
        {
            if (Stack.Count > 0) Stack.Pop();
            _pendingThisAccess = false;
            _ushortInAX = false;
            _runtimeValueInA = false;
            return true;
        }

        if (!IsClassInstanceMethod(methodName))
            return false;

        int argCount = _reflectionCache.GetNumberOfArguments(methodName);
        if (argCount < 1)
            return false;

        // Gather args from eval stack (top = last arg)
        var args = new int[argCount];
        for (int i = argCount - 1; i >= 0; i--)
            args[i] = Stack.Count > 0 ? Stack.Pop() : 0;

        int thisAddr = args[0];
        // Strip trailing arg materialization. For byte last-arg: 1 instr (LDA #imm).
        // this may only have lived on the eval stack (newobj/dup) with no load to remove.
        int lastArgInstrs = 0;
        if (argCount >= 2)
        {
            // Assume last arg is a byte constant loaded with LDA #imm
            lastArgInstrs = 1;
            if (CurrentBlock!.Count >= lastArgInstrs)
                RemoveLastInstructions(lastArgInstrs);
        }

        // Push this (16-bit), then reload last scalar arg into A (if any)
        Emit(Opcode.LDA, AddressMode.Immediate, (byte)(thisAddr & 0xFF));
        Emit(Opcode.LDX, AddressMode.Immediate, (byte)(thisAddr >> 8));
        if (argCount >= 2)
        {
            EmitJSR("pushax");
            UsedMethods?.Add("pushax");
            // Intermediate args (between this and last): push as bytes
            for (int i = 1; i < argCount - 1; i++)
            {
                Emit(Opcode.LDA, AddressMode.Immediate, (byte)(args[i] & 0xFF));
                EmitJSR("pusha");
                UsedMethods?.Add("pusha");
            }
            Emit(Opcode.LDA, AddressMode.Immediate, (byte)(args[argCount - 1] & 0xFF));
        }
        // else: only this — leave in A:X for pushax prologue in callee

        EmitWithLabel(Opcode.JSR, AddressMode.Absolute, methodName);
        _pendingObjectBase = null;
        _ushortInAX = false;
        _runtimeValueInA = _reflectionCache.HasReturnValue(methodName);
        _immediateInA = null;
        return true;
    }

    /// <summary>
    /// Instance-method field store via <c>this</c> pointer in ptr1, or absolute when base known.
    /// </summary>
    bool TryHandleClassStfld(string fieldName)
    {
        // this.field = value  (inside instance method; ptr1 holds this)
        if (_pendingThisAccess || _thisInPtr1)
        {
            _pendingThisAccess = false;
            string typeName = InstanceThisType
                ?? throw new TranspileException($"stfld '{fieldName}' with this, but method type is unknown.", MethodName);
            int fieldOffset = GetFieldOffset(typeName, fieldName);
            int value = Stack.Count > 0 ? Stack.Pop() : 0;

            // Ensure this pointer is in ptr1 (ldarg.0 should have loaded it)
            if (_runtimeValueInA && _ushortInAX)
            {
                Emit(Opcode.STA, AddressMode.ZeroPage, ptr1);
                Emit(Opcode.STX, AddressMode.ZeroPage, (byte)(ptr1 + 1));
                _ushortInAX = false;
                _thisInPtr1 = true;
            }

            if (_runtimeValueInA)
            {
                // Runtime value in A (e.g. after add) — don't strip; PHA around Y setup
                Emit(Opcode.PHA, AddressMode.Implied);
                Emit(Opcode.LDY, AddressMode.Immediate, (byte)fieldOffset);
                Emit(Opcode.PLA, AddressMode.Implied);
                Emit(Opcode.STA, AddressMode.IndirectIndexed, ptr1);
                _runtimeValueInA = false;
            }
            else
            {
                if (CurrentBlock!.Count > 0)
                    RemoveLastInstructions(1); // LDA #value
                Emit(Opcode.LDA, AddressMode.Immediate, (byte)(value & 0xFF));
                Emit(Opcode.LDY, AddressMode.Immediate, (byte)fieldOffset);
                Emit(Opcode.STA, AddressMode.IndirectIndexed, ptr1);
            }
            _immediateInA = null;
            return true;
        }

        // obj.field = value with compile-time object base (from newobj / class local / dup)
        // Do not steal struct stfld (ldloca → _pendingStructLocal).
        if (_pendingObjectBase is null || _pendingStructLocal is not null)
            return false;

        {
            int value = Stack.Count > 0 ? Stack.Pop() : 0;
            ushort baseAddr = _pendingObjectBase.Value;
            if (Stack.Count > 0 && Stack.Peek() == baseAddr)
                Stack.Pop(); // consume the obj ref under the value

            string typeName = ResolveLayoutTypeForField(fieldName);
            int fieldOffset = GetFieldOffset(typeName, fieldName);
            ushort fieldAddr = (ushort)(baseAddr + fieldOffset);

            if (_runtimeValueInA)
            {
                Emit(Opcode.STA, AddressMode.Absolute, fieldAddr);
                _runtimeValueInA = false;
            }
            else
            {
                RemoveLastInstructions(1);
                Emit(Opcode.LDA, AddressMode.Immediate, (byte)(value & 0xFF));
                Emit(Opcode.STA, AddressMode.Absolute, fieldAddr);
            }
            _immediateInA = null;
            // Keep pending base when dup left another copy for a following callvirt
            _pendingObjectBase = Stack.Count > 0 && Stack.Peek() == baseAddr ? baseAddr : null;
            return true;
        }
    }

    string ResolveLayoutTypeForField(string fieldName)
    {
        foreach (var kvp in StructLayouts.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (kvp.Value.Any(f => f.Name == fieldName))
                return kvp.Key;
        }
        throw new TranspileException($"Cannot resolve type for field '{fieldName}'.", MethodName);
    }

    bool TryHandleClassLdfld(string fieldName)
    {
        if (_pendingThisAccess || _thisInPtr1)
        {
            _pendingThisAccess = false;
            string typeName = InstanceThisType
                ?? throw new TranspileException($"ldfld '{fieldName}' with this, but method type is unknown.", MethodName);
            int fieldOffset = GetFieldOffset(typeName, fieldName);

            if (_runtimeValueInA && _ushortInAX)
            {
                Emit(Opcode.STA, AddressMode.ZeroPage, ptr1);
                Emit(Opcode.STX, AddressMode.ZeroPage, (byte)(ptr1 + 1));
                _ushortInAX = false;
                _thisInPtr1 = true;
            }

            Emit(Opcode.LDY, AddressMode.Immediate, (byte)fieldOffset);
            Emit(Opcode.LDA, AddressMode.IndirectIndexed, ptr1);
            _runtimeValueInA = true;
            _immediateInA = null;
            Stack.Push(0);
            return true;
        }

        if (_pendingObjectBase is ushort objBase)
        {
            string typeName = ResolveLayoutTypeForField(fieldName);
            int fieldOffset = GetFieldOffset(typeName, fieldName);
            ushort fieldAddr = (ushort)(objBase + fieldOffset);

            Emit(Opcode.LDA, AddressMode.Absolute, fieldAddr);
            _runtimeValueInA = true;
            _ushortInAX = false;
            _immediateInA = null;
            // Consume the object ref used for this load. If another copy remains
            // (ldloc/ldloc or dup before read-modify-write), keep pending for stfld.
            if (Stack.Count > 0 && Stack.Peek() == objBase)
                Stack.Pop();
            bool keepForStore = Stack.Count > 0 && Stack.Peek() == objBase;
            Stack.Push(0); // field value
            _pendingObjectBase = keepForStore ? objBase : null;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Loads <c>this</c> (arg 0) as a 16-bit pointer into A:X / ptr1 for field access.
    /// </summary>
    void HandleLdargThis()
    {
        // Mirror closure ldarg: load this into ptr1 without pushing an eval-stack placeholder.
        // this is ParamIsArray[0] (16-bit) at the bottom of the cc65 arg frame.
        if (_thisInPtr1 && _pendingThisAccess)
        {
            // Already loaded for a prior field op in the same expression (e.g. this.X +=)
            return;
        }

        int offset = ParameterOffset(0);
        Emit(Opcode.LDY, AddressMode.Immediate, (byte)(offset + 1));
        Emit(Opcode.LDA, AddressMode.IndirectIndexed, (byte)sp);
        Emit(Opcode.STA, AddressMode.ZeroPage, (byte)(ptr1 + 1));
        Emit(Opcode.LDY, AddressMode.Immediate, (byte)offset);
        Emit(Opcode.LDA, AddressMode.IndirectIndexed, (byte)sp);
        Emit(Opcode.STA, AddressMode.ZeroPage, ptr1);
        _pendingThisAccess = true;
        _thisInPtr1 = true;
        _ushortInAX = false;
        _runtimeValueInA = false;
        _immediateInA = null;
    }
}
