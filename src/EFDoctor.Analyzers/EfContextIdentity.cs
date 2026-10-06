using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace EFDoctor.Analyzers;

// Proves, by symbol, that two expressions in one operation block are the same DbContext
// instance. A context is comparable when it is a never-written local or parameter, a `this` or
// static field or auto-property, or `this`. Anything else, such as a method result, a custom
// getter, or another object's member, has no key.
internal sealed class EfContextIdentity
{
    private readonly IOperation _root;
    private readonly INamedTypeSymbol _dbContext;
    private readonly Dictionary<ISymbol, bool> _neverWritten = new(SymbolEqualityComparer.Default);

    public EfContextIdentity(IOperation root, INamedTypeSymbol dbContext)
    {
        _root = root;
        _dbContext = dbContext;
    }

    public bool TryGetKey(IOperation? context, out ISymbol key, out string display)
    {
        key = null!;
        display = null!;
        if (context is null)
        {
            return false;
        }

        switch (EfQueryOperationAnalysis.Unwrap(context))
        {
            case ILocalReferenceOperation local when !local.Local.IsRef && IsNeverWritten(local.Local):
                key = local.Local;
                display = local.Local.Name;
                return true;
            case IParameterReferenceOperation parameter when parameter.Parameter.RefKind == RefKind.None && IsNeverWritten(parameter.Parameter):
                key = parameter.Parameter;
                display = parameter.Parameter.Name;
                return true;
            case IFieldReferenceOperation field when IsThisOrStatic(field.Instance):
                key = field.Field;
                display = MemberDisplay(field.Instance, field.Field);
                return true;
            case IPropertyReferenceOperation property when IsThisOrStatic(property.Instance) && IsAutoProperty(property.Property):
                key = property.Property;
                display = MemberDisplay(property.Instance, property.Property);
                return true;
            case IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance, Type: { } type }:
                key = type;
                display = "this";
                return true;
            default:
                return false;
        }
    }

    // The context a DbSet expression belongs to: `context.Orders` or `context.Set<Order>()`.
    public IOperation? ContextOfDbSet(IOperation dbSetExpression)
    {
        switch (EfQueryOperationAnalysis.Unwrap(dbSetExpression))
        {
            case IPropertyReferenceOperation { Instance: { } instance } when EfQueryOperationAnalysis.IsOrDerivesFrom(instance.Type, _dbContext):
                return instance;
            case IFieldReferenceOperation { Instance: { } instance } when EfQueryOperationAnalysis.IsOrDerivesFrom(instance.Type, _dbContext):
                return instance;
            case IInvocationOperation { Instance: { } instance } invocation
                when invocation.TargetMethod.Name == "Set" && EfQueryOperationAnalysis.IsOrDerivesFrom(instance.Type, _dbContext):
                return instance;
            default:
                return null;
        }
    }

    private bool IsNeverWritten(ISymbol symbol)
    {
        if (!_neverWritten.TryGetValue(symbol, out var result))
        {
            result = !_root.Descendants().Any(operation => operation switch
            {
                ILocalReferenceOperation local => SymbolEqualityComparer.Default.Equals(local.Local, symbol) && IsWrite(local),
                IParameterReferenceOperation parameter => SymbolEqualityComparer.Default.Equals(parameter.Parameter, symbol) && IsWrite(parameter),
                _ => false,
            });
            _neverWritten.Add(symbol, result);
        }

        return result;
    }

    private static bool IsWrite(IOperation reference)
    {
        if (reference is ILocalReferenceOperation { IsDeclaration: true })
        {
            return true;
        }

        var current = reference;
        while (current.Parent is ITupleOperation or IConversionOperation)
        {
            current = current.Parent;
        }

        return current.Parent switch
        {
            IAssignmentOperation assignment => ReferenceEquals(assignment.Target, current),
            IIncrementOrDecrementOperation or IAddressOfOperation => true,
            IArgumentOperation { Parameter.RefKind: not RefKind.None and not RefKind.In and not RefKind.RefReadOnlyParameter } => true,
            _ => false,
        };
    }

    private static bool IsThisOrStatic(IOperation? instance) =>
        instance is null or IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance };

    private static bool IsAutoProperty(IPropertySymbol property) =>
        property.ContainingType.GetMembers()
            .OfType<IFieldSymbol>()
            .Any(field => SymbolEqualityComparer.Default.Equals(field.AssociatedSymbol, property));

    private static string MemberDisplay(IOperation? instance, ISymbol member) =>
        instance is null ? $"{member.ContainingType.Name}.{member.Name}" : $"this.{member.Name}";
}
