# Design

## Context

Every existing rule that proves a query chain follows the *query*: from the reported operator back to a `DbSet<T>`, through resolved operators and statically determined `IQueryable` locals. EFD037 is the first rule that follows the *result*: it has to know everything the method does with the materialized entities. The roadmap notes the same need for EFD032.

`PrematureQueryMaterializationAnalyzer` already classifies materializers (`ToList`, `ToArray`, `ToListAsync`, `ToArrayAsync`) and finds the materialized value through an `await`. `EfQueryOperationAnalysis.TryAnalyzeSource` proves the chain and lists its operators.

## Goals / Non-Goals

**Goals:**

- Report only when the whole life of the entities is visible in one method and consists of scalar reads.
- Give evidence a developer can act on: the properties read, and how many the entity has.

**Non-Goals:**

- Single-entity loads (`First`, `Single`, `Find`). One over-fetched row is rarely worth a finding.
- Following entities through helper methods, fields, or returned values.
- A general result-flow engine. EFD037 needs only "every use is a scalar read", which is a closed check.

## Decisions

### Anchor on the materializer whose value initializes a local

The rule runs on each materializer invocation. It continues only when:

- the chain is proven to a `DbSet<T>` or `DbContext.Set<T>()`;
- the materialized element type is that entity type `T`, so the query has no projection;
- the chain has no `Include` or `ThenInclude`, because loading a graph is a decision to use navigations;
- the materialized value, after any `await`, is the initializer of a local declared by a declaration statement.

The finding is reported on the materializer, the same anchor as EFD004, EFD005, and EFD019.

### A closed list of uses

Every reference to the local in the method must be one of these, or the rule is silent:

- the collection of a `foreach` whose loop variable is a single local;
- the source of `Enumerable.Select` with a one-parameter selector;
- the source of `Enumerable.Any`, `All`, `Count`, `Sum`, `Min`, `Max`, or `Average` with a one-parameter lambda;
- the source of `Enumerable.Any` or `Count` with no lambda, or the instance of `List<T>.Count` or array `Length`;
- an index read (`list[i]`, `array[i]`) that is itself the instance of a property read.

An element is the loop variable, the lambda parameter, or the indexed value. Every reference to an element must be the instance of a read of a *counted* property (below). Passing the element, returning it, storing it, comparing it, writing one of its properties, or reading a navigation or computed member all make the rule silent.

`Where`, `OrderBy`, `First`, and the like are deliberately not on the list: they return entities, and following those would need real flow analysis. They can be added one at a time later.

### Counted properties

The entity's mapped scalar properties are approximated from the type, without the EF model: public instance auto-properties with a getter and a setter, declared on the entity type or a base type, without `[NotMapped]`, whose type is a primitive, `string`, `decimal`, `DateTime`, `DateTimeOffset`, `TimeSpan`, `DateOnly`, `TimeOnly`, `Guid`, an enum, `byte[]`, or a nullable form of one. Navigations, collections, owned types, and computed properties are not counted, and a read of one makes the rule silent.

This can miscount an entity configured with value converters or shadow properties. The threshold has enough margin, and the confidence is advisory.

### Threshold

Report when the method reads at least one counted property, at most half of them, and leaves at least four unread. So the smallest entity that can be reported has five scalar properties with one read. Decided with the maintainer: small entities stay silent.

### Advisory, Info

Over-fetching is a cost, not a bug, and the right threshold depends on context. Confidence is advisory, severity `Info`, category `Performance`.

### Overlap

- EFD004 reports `ToList().Select(…)` inline. EFD037 requires a local, so they don't report the same code.
- EFD005 may report the same materializer for having no row bound. The two findings are independent: one is about rows, the other about columns.
- EFD013 needs entity writes, which make EFD037 silent.

## Risks / Trade-offs

- [The closed list misses common shapes, such as `list.Where(…).Select(…)`] → Accepted for a first version: silence is cheap for an advisory rule, and each shape can be added with its own tests.
- [The property count differs from the real model] → Advisory confidence, a margin of four unread properties, and evidence that names the counted number.
- [The whole method is scanned once per materializer] → Only for a materializer that already passed the chain proof and initializes a local.
