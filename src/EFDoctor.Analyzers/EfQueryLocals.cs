using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace EFDoctor.Analyzers;

// Resolves a read of a query local to the value the local holds at that read, but only when that
// value is statically determined:
// - the local is declared, with an initializer, by a statement directly inside a block;
// - every other write is a plain `local = value;` statement directly inside the same block;
// - the read sits in a later statement of that block, outside any lambda or local function.
// The value is then the latest write before the read's statement, whatever path reached it, so a
// chain proof can continue through it. Any other local ends the proof, as an unknown source does.
internal static class EfQueryLocals
{
    public const int MaxHops = 8;

    private static readonly ConditionalWeakTable<IOperation, Dictionary<ILocalSymbol, LocalWrites>> Cache = new();

    public static bool TryFollow(IOperation operation, int hops, out ILocalSymbol local, out IOperation value)
    {
        local = null!;
        value = null!;
        if (hops >= MaxHops || operation is not ILocalReferenceOperation read || !TryResolve(read, out value))
        {
            return false;
        }

        local = read.Local;
        return true;
    }

    public static bool TryResolve(ILocalReferenceOperation read, out IOperation value)
    {
        value = null!;
        var locals = Cache.GetValue(Root(read), Collect);
        if (!locals.TryGetValue(read.Local, out var writes) || !writes.IsFollowable)
        {
            return false;
        }

        var statementIndex = StatementIndex(read, writes.Block!);
        if (statementIndex <= writes.DeclarationIndex)
        {
            return false;
        }

        value = writes.Initializer!;
        foreach (var assignment in writes.Assignments)
        {
            if (assignment.Index >= statementIndex)
            {
                break;
            }

            value = assignment.Value;
        }

        return true;
    }

    private static IOperation Root(IOperation operation)
    {
        while (operation.Parent is not null)
        {
            operation = operation.Parent;
        }

        return operation;
    }

    // The index of the statement of `block` that contains `operation`, or -1 when the operation is
    // outside the block or inside a lambda or local function nested in it.
    private static int StatementIndex(IOperation operation, IBlockOperation block)
    {
        for (var current = operation; current.Parent is not null; current = current.Parent)
        {
            if (current is IAnonymousFunctionOperation or ILocalFunctionOperation)
            {
                return -1;
            }

            if (ReferenceEquals(current.Parent, block))
            {
                return block.Operations.IndexOf(current);
            }
        }

        return -1;
    }

    private static Dictionary<ILocalSymbol, LocalWrites> Collect(IOperation root)
    {
        var locals = new Dictionary<ILocalSymbol, LocalWrites>(SymbolEqualityComparer.Default);
        LocalWrites For(ILocalSymbol local)
        {
            if (!locals.TryGetValue(local, out var writes))
            {
                writes = new LocalWrites();
                locals.Add(local, writes);
            }

            return writes;
        }

        foreach (var operation in root.DescendantsAndSelf())
        {
            switch (operation)
            {
                case IVariableDeclaratorOperation declarator:
                    RecordDeclaration(For(declarator.Symbol), declarator);
                    break;
                case ILocalReferenceOperation reference:
                    RecordReference(For(reference.Local), reference);
                    break;
            }
        }

        foreach (var writes in locals.Values)
        {
            writes.Complete();
        }

        return locals;
    }

    private static void RecordDeclaration(LocalWrites writes, IVariableDeclaratorOperation declarator)
    {
        if (writes.Block is not null
            || declarator.Symbol.IsRef
            || declarator.Initializer?.Value is not { } initializer
            || declarator.Parent is not IVariableDeclarationOperation { Parent: IVariableDeclarationGroupOperation group }
            || group.Parent is not IBlockOperation block)
        {
            writes.Disqualify();
            return;
        }

        writes.Block = block;
        writes.DeclarationIndex = block.Operations.IndexOf(group);
        writes.Initializer = initializer;
    }

    private static void RecordReference(LocalWrites writes, ILocalReferenceOperation reference)
    {
        if (reference.IsDeclaration)
        {
            writes.Disqualify();
            return;
        }

        switch (reference.Parent)
        {
            case ISimpleAssignmentOperation assignment when ReferenceEquals(assignment.Target, reference):
                if (!assignment.IsRef
                    && assignment.Parent is IExpressionStatementOperation { Parent: IBlockOperation block } statement)
                {
                    writes.AddAssignment(block, block.Operations.IndexOf(statement), assignment.Value);
                }
                else
                {
                    writes.Disqualify();
                }

                break;
            case ICompoundAssignmentOperation compound when ReferenceEquals(compound.Target, reference):
            case ICoalesceAssignmentOperation coalesce when ReferenceEquals(coalesce.Target, reference):
            case IIncrementOrDecrementOperation:
            case IAddressOfOperation:
            case IArgumentOperation { Parameter.RefKind: not RefKind.None }:
            case IVariableInitializerOperation { Parent: IVariableDeclaratorOperation { Symbol.IsRef: true } }:
                writes.Disqualify();
                break;
            default:
                if (IsDeconstructionTarget(reference))
                {
                    writes.Disqualify();
                }

                break;
        }
    }

    private static bool IsDeconstructionTarget(IOperation reference)
    {
        var current = reference;
        while (current.Parent is ITupleOperation or IConversionOperation)
        {
            current = current.Parent;
        }

        return current.Parent is IDeconstructionAssignmentOperation deconstruction
            && ReferenceEquals(deconstruction.Target, current);
    }

    private sealed class LocalWrites
    {
        private readonly List<(IBlockOperation Block, int Index, IOperation Value)> _pending = new();
        private bool _disqualified;

        public IBlockOperation? Block { get; set; }

        public int DeclarationIndex { get; set; } = -1;

        public IOperation? Initializer { get; set; }

        public List<(int Index, IOperation Value)> Assignments { get; } = new();

        public bool IsFollowable { get; private set; }

        public void Disqualify() => _disqualified = true;

        public void AddAssignment(IBlockOperation block, int index, IOperation value) =>
            _pending.Add((block, index, value));

        public void Complete()
        {
            IsFollowable = !_disqualified
                && Block is not null
                && DeclarationIndex >= 0
                && _pending.All(pending => ReferenceEquals(pending.Block, Block) && pending.Index > DeclarationIndex);
            if (IsFollowable)
            {
                Assignments.AddRange(_pending
                    .Select(static pending => (pending.Index, pending.Value))
                    .OrderBy(static assignment => assignment.Index));
            }
        }
    }
}
