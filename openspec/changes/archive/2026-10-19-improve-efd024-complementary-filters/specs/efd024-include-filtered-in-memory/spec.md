## MODIFIED Requirements

### Requirement: Detect an Include whose collection is only used filtered
EFD024 SHALL report a resolved EF Core `Include` invocation when all of these hold:
- **The Include:** its navigation lambda is a direct property read of a collection navigation on the lambda's parameter, with no filter. The query's chain, followed through statically determined query locals, proves an EF Core `DbSet` origin.
- **The load:** the query is materialized by a statement directly in a block, into a local declared from a resolved single-entity terminal (`First`, `FirstOrDefault`, `Single`, `SingleOrDefault`, `Last`, or `LastOrDefault`) or `ToList`/`ToArray`, or their awaited EF Core async forms.
- **The entity variables:** for a single-entity load, the local itself. For a list load, every `foreach` iteration variable over the local, and every lambda parameter of an `Enumerable` method call whose source is the local.
- **The uses:** every read of the navigation property on an entity variable, anywhere in the method, is the source of an `Enumerable` `Where`, `First`, `FirstOrDefault`, `Single`, `SingleOrDefault`, `Last`, `LastOrDefault`, `Any`, `All`, `Count`, or `LongCount` call with a predicate lambda. There is at least one such read.
- **The filters leave rows out:** no two of the predicate lambdas are complementary. Two are complementary when, compared by their tokens with the lambda parameter renamed, one is `!e` and the other `e`, or they compare the same operands with `==` and `!=`, `<` and `>=`, or `>` and `<=`.
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

#### Scenario: Complementary filters
- **WHEN** the method reads `blog.Posts.Where(p => p.SeriesId == null)` and `blog.Posts.Where(r => r.SeriesId != null)`, or filters with `p.Published` and `!p.Published`
- **THEN** EFD024 does not report

#### Scenario: Different filters that aren't complementary
- **WHEN** the method filters `Posts` with `p.Published` and `!p.Pinned`, or with `p.Score < 5` and `p.Score > 5`
- **THEN** EFD024 reports the `Include`

#### Scenario: Entity escapes
- **WHEN** the entity or the list is returned, passed to a method, assigned elsewhere, or captured beyond the shapes above
- **THEN** EFD024 does not report

#### Scenario: Filtered Include or reference navigation
- **WHEN** the include is already `Include(b => b.Posts.Where(...))`, or the navigation is a single reference
- **THEN** EFD024 does not report
