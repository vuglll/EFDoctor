# efd024-include-filtered-in-memory Specification

## Purpose
Define advisory detection of a collection navigation that `Include` loads in full when the method only ever uses it filtered in memory, where a filtered `Include` would filter in SQL.

## Requirements

### Requirement: Detect an Include whose collection is only used filtered
EFD024 SHALL report a resolved EF Core `Include` invocation when all of these hold:
- **The Include:** its navigation lambda is a direct property read of a collection navigation on the lambda's parameter, with no filter. The query's chain, followed through statically determined query locals, proves an EF Core `DbSet` origin.
- **The load:** the query is materialized by a statement directly in a block, into a local declared from a resolved single-entity terminal (`First`, `FirstOrDefault`, `Single`, `SingleOrDefault`, `Last`, or `LastOrDefault`) or `ToList`/`ToArray`, or their awaited EF Core async forms.
- **The entity variables:** for a single-entity load, the local itself. For a list load, every `foreach` iteration variable over the local, and every lambda parameter of an `Enumerable` method call whose source is the local.
- **The uses:** every read of the navigation property on an entity variable, anywhere in the method, is the source of an `Enumerable` `Where`, `First`, `FirstOrDefault`, `Single`, `SingleOrDefault`, `Last`, `LastOrDefault`, `Any`, `All`, `Count`, or `LongCount` call with a predicate lambda. There is at least one such read.
- **Nothing escapes:** every other reference to the local is the instance of a property read, a `foreach` collection, or the source of an `Enumerable` call. Every other reference to an entity variable is the instance of a property read.

EFD024 SHALL anchor the finding on the `Include` invocation.

#### Scenario: Single entity with a filtered navigation
- **WHEN** `var blog = db.Blogs.Include(b => b.Posts).First(b => b.Id == id);` and the method only reads `blog.Posts.Where(p => p.Published)`
- **THEN** EFD024 reports the `Include`

#### Scenario: List iterated with foreach or projected
- **WHEN** a list loaded with `Include(b => b.Posts).ToList()` is iterated with `foreach (var blog in blogs)`, or projected with `blogs.Select(b => b.Posts.Count(p => p.Published))`, and every use of `Posts` has a predicate
- **THEN** EFD024 reports the `Include`

#### Scenario: Navigation also used in full
- **WHEN** the method also reads `blog.Posts.Count`, iterates `blog.Posts`, or calls an operator on it without a predicate
- **THEN** EFD024 does not report

#### Scenario: Entity escapes
- **WHEN** the entity or the list is returned, passed to a method, assigned elsewhere, or captured beyond the shapes above
- **THEN** EFD024 does not report

#### Scenario: Filtered Include or reference navigation
- **WHEN** the include is already `Include(b => b.Posts.Where(...))`, or the navigation is a single reference
- **THEN** EFD024 does not report

### Requirement: Produce advisory EFD024 findings
Each EFD024 finding SHALL have:
- rule title `Included collection filtered in memory`;
- `Performance` category, `Info` severity, and advisory confidence;
- documentation key `EFD024`;
- the message `'{navigation}' is loaded in full by Include, but this method only uses it filtered; use a filtered Include`.

Evidence SHALL name the navigation, the load, and the number of filtered uses. Impact SHALL explain the transferred and tracked rows. Remediation SHALL show a filtered `Include`, and mention that with tracking the navigation can contain other tracked entities.

#### Scenario: Complete structured finding
- **WHEN** EFD024 reports an `Include`
- **THEN** the finding has the title, category, severity, confidence, documentation key, message, evidence, impact, and remediation described above

### Requirement: Support standard suppression and existing reporting behavior
EFD024 SHALL use standard Roslyn suppression, and flow through the existing CLI, console and JSON output, and exit codes.

#### Scenario: Suppression
- **WHEN** a matching `Include` is covered by `#pragma warning disable EFD024`, `[SuppressMessage("Performance", "EFD024")]`, or `dotnet_diagnostic.EFD024.severity = none`
- **THEN** the finding is excluded

#### Scenario: Console and JSON reporting
- **WHEN** a CLI scan of the EFD024 fixture project runs in console and JSON modes
- **THEN** both outputs contain only the expected unsuppressed EFD024 findings, with exact locations, JSON schema version `1`, and exit code `1`
