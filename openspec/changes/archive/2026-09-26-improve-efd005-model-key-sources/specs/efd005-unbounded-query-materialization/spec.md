## MODIFIED Requirements

### Requirement: Recognize predicate-derived row bounds
EFD005 SHALL recognize row bounds established by the predicate of a resolved `Queryable.Where` in the proven inline chain, in addition to `Queryable.Take`. A local-collection membership predicate of the form `where entity => collection.Contains(entity.Property)` or `where entity => collection.Any(...)`, whose receiver is a local, parameter, or field collection and whose argument is a property path over the `Where` parameter, SHALL be a strong bound. A key-equality predicate of the form `where entity => entity.Key == value`, where the property resolves through EF model metadata to a primary key, foreign key, or a unique single-column alternate key, SHALL be a strong bound. Membership of a non-unique index, or of one column of a composite key or index, SHALL NOT by itself be a strong bound.

EF model metadata SHALL be any of the following sources:
- **Data annotations** on the entity: `[Key]`, `[ForeignKey]`, and a unique single-column `[Index]`.
- **Fluent configuration in the analyzed project**, whether in `OnModelCreating` or in an `IEntityTypeConfiguration<T>` implementation, in lambda or string form. The recognized calls are a single-column `HasKey`, a single-column `HasAlternateKey`, a single-column `HasForeignKey`, and a single-column `HasIndex` followed by `IsUnique()`. Fluent configuration applies to the entity type it names and to types derived from it.
- **EF Core's navigation foreign-key convention.** A property named `<Navigation>Id` is a foreign key when the entity type, or one of its base types, declares a reference navigation named `<Navigation>`. A reference navigation is a property whose type is a class other than `string` and is not a collection.

Configuration that is not visible in the analyzed project SHALL NOT be assumed. That includes configuration compiled into a referenced assembly, and a migration snapshot that names entity types by string. The `Id` and `<Entity>Id` primary-key convention SHALL NOT by itself prove a key, because a composite key configured elsewhere cannot be ruled out. The same equality shape whose key status is established only by an `Id`, `<Entity>Id`, or `*Id` name heuristic SHALL be a medium bound.

Equality SHALL be recognized in each of these forms, with the same bound strength:
- the built-in `==` operator;
- a user-defined `==` operator, including its lifted nullable form, whose two parameters have the entity key property's type, ignoring nullability. This covers `Guid`, `DateTimeOffset`, and other value types that define their own equality operator;
- a single-argument instance `Equals` call whose receiver is one side of the comparison and whose argument is the other.

The same operator forms SHALL apply to the equality inside a membership predicate written with `Any`.

In a key-equality predicate, the compared `value` SHALL be any single scalar value that is not a property path over the `Where` entity parameter. That includes:
- a constant, a local, a non-entity parameter, a field, or a default-value expression;
- a property or member access whose root is a different object, for example `parent.Id`, `product.ProductId`, or `request.Filter.OwnerId`.

A property or member access on the compared side MUST NOT be required to be a constant, local, parameter, or field to establish the bound. The bound strength SHALL be determined solely by the entity-side key (metadata versus name heuristic), never by the kind of the comparand or the equality form. Exactly one side of the equality SHALL be an entity key path over the `Where` parameter; when both sides are property paths over the `Where` parameter, or neither side is, no key-equality bound is established.

When a predicate is a conjunction, EFD005 SHALL take the strongest bound among its conjuncts; a null-guarded nullable value access such as `entity.Property.Value` SHALL resolve to the underlying entity property. Bound recognition MUST use the shared predicate model, MUST NOT alter the shared EFD004 scalar predicate model, and MUST remain linear in the inspected chain without general data-flow or interprocedural analysis.

#### Scenario: Local collection membership
- **WHEN** an inline EF query filters with `Where(x => ids.Contains(x.ProductId))` over a local collection
- **THEN** EFD005 recognizes a strong bound and does not report

#### Scenario: Membership via Any
- **WHEN** an inline EF query filters with `Where(x => ids.Any(id => id == x.Fk))` over a local collection
- **THEN** EFD005 recognizes a strong bound and does not report

#### Scenario: Membership within a conjunction
- **WHEN** an inline EF query filters with `Where(x => ids.Contains(x.Fk) && x.OtherFilter)` combining membership with additional conditions
- **THEN** EFD005 recognizes the membership conjunct as a strong bound and does not report

#### Scenario: Null-guarded membership over a nullable key
- **WHEN** an inline EF query filters with `Where(x => x.OptionalFk.HasValue && ids.Contains(x.OptionalFk.Value))`
- **THEN** EFD005 resolves the nullable value access to the entity property, recognizes a strong bound, and does not report

#### Scenario: Key-equality bound from metadata
- **WHEN** an inline EF query filters with `Where(x => x.Id == value)` where the property is a key resolved from EF metadata
- **THEN** EFD005 recognizes a strong bound and does not report

#### Scenario: Non-unique or composite index is not a strong key bound
- **WHEN** an inline EF query filters with `Where(x => x.Column == value)` where `Column` participates only in a non-unique or composite index
- **THEN** EFD005 does not treat it as a strong key-equality bound

#### Scenario: Key from fluent configuration
- **WHEN** an entity's key is declared with `HasKey(e => e.Code)` or `HasKey("Code")` in `OnModelCreating` or an `IEntityTypeConfiguration<T>` in the analyzed project, and an inline EF query filters with `Where(x => x.Code == value)`
- **THEN** EFD005 recognizes a strong key-equality bound and does not report

#### Scenario: Foreign key from fluent configuration
- **WHEN** a relationship declares `HasForeignKey(e => e.OwnerRef)` in the analyzed project, and an inline EF query filters with `Where(x => x.OwnerRef == ownerId)`
- **THEN** EFD005 recognizes a strong key-equality bound and does not report

#### Scenario: Unique index from fluent configuration
- **WHEN** the analyzed project declares `HasIndex(e => e.Email).IsUnique()`, and an inline EF query filters with `Where(x => x.Email == email)`
- **THEN** EFD005 recognizes a strong key-equality bound and does not report

#### Scenario: Composite or non-unique fluent configuration is not a strong key
- **WHEN** the only configuration for a property is a composite `HasKey`, a composite `HasForeignKey`, or a `HasIndex` without `IsUnique()`
- **THEN** EFD005 does not treat the property as a proven key, and a name-matched property stays a medium bound reported at advisory confidence

#### Scenario: Foreign key by navigation convention
- **WHEN** an entity declares a reference navigation `Organization` and a property `OrganizationId`, and an inline EF query filters with `Where(x => x.OrganizationId == organizationId)`
- **THEN** EFD005 recognizes a strong key-equality bound and does not report, even when no fluent configuration is visible

#### Scenario: Primary-key naming convention alone is not a proven key
- **WHEN** an inline EF query filters with `Where(x => x.Id == id)` and no data annotation, visible fluent configuration, or navigation convention proves `Id` is a key
- **THEN** EFD005 treats it as a medium bound and reports at advisory confidence

#### Scenario: Key-equality bound by name heuristic
- **WHEN** an inline EF query filters with `Where(x => x.CustomerId == value)` where key status is established only by the name heuristic
- **THEN** EFD005 recognizes a medium bound and reports at advisory confidence rather than suppressing

#### Scenario: Key-equality through a user-defined equality operator
- **WHEN** an inline EF query filters with `Where(x => x.Key == id)` where the key and `id` are a `Guid` or another value type that defines its own `==` operator
- **THEN** EFD005 recognizes the key-equality bound at the same strength as for a built-in `==`: strong for a metadata key (not reported) and medium for a name-heuristic key (reported at advisory confidence)

#### Scenario: Key-equality through a lifted nullable operator
- **WHEN** an inline EF query filters with `Where(x => x.OptionalKey == id)` where `OptionalKey` is a nullable `Guid` key
- **THEN** EFD005 recognizes the key-equality bound as it would for the non-nullable key

#### Scenario: Key-equality through Equals
- **WHEN** an inline EF query filters with `Where(x => x.Key.Equals(id))` or `Where(x => id.Equals(x.Key))`
- **THEN** EFD005 recognizes the key-equality bound at the same strength as for `x.Key == id`

#### Scenario: Membership via Any with a user-defined equality operator
- **WHEN** an inline EF query filters with `Where(x => ids.Any(id => id == x.Fk))` over a local collection of `Guid` values
- **THEN** EFD005 recognizes a strong bound and does not report

#### Scenario: Equals between two entity properties
- **WHEN** an inline EF query filters with `Where(x => x.Key.Equals(x.OtherProperty))`
- **THEN** EFD005 does not establish a key-equality bound and reports if no other bound applies

#### Scenario: Key-equality against a property of another entity
- **WHEN** an inline EF query filters with `Where(x => x.ProductId == product.ProductId)` where `x.ProductId` is an entity key path over the `Where` parameter and `product.ProductId` is a property access on a different object
- **THEN** EFD005 recognizes the key-equality bound at the same strength it would for a constant or local on the compared side and does not report differently because the comparand is a property access

#### Scenario: Key-equality against a nested member of a parameter
- **WHEN** an inline EF query filters with `Where(x => x.OwnerId == request.Filter.OwnerId)` where `request` is a method parameter and `request.Filter.OwnerId` is a nested member access resolving to a metadata key on the entity side
- **THEN** EFD005 recognizes the strong key-equality bound and does not report

#### Scenario: Key-equality with a compound predicate and a property comparand
- **WHEN** an inline EF query filters with `Where(x => x.ProductId == product.ProductId && x.ActionName == SomeEnum.Submit)` where `x.ProductId` resolves to a strong key
- **THEN** EFD005 recognizes the key-equality bound from the key-equality conjunct and does not report

#### Scenario: Both sides are entity property paths
- **WHEN** an inline EF query filters with `Where(x => x.Key == x.OtherProperty)` where both comparands are property paths over the `Where` parameter
- **THEN** EFD005 does not establish a key-equality bound because neither side is a scalar value external to the entity, and reports if no other bound applies

#### Scenario: Time-window intent signal
- **WHEN** an inline EF query filters with a temporal comparison such as `Where(x => x.CreatedUtc >= since)`
- **THEN** EFD005 recognizes a weak bound and reports at advisory confidence
