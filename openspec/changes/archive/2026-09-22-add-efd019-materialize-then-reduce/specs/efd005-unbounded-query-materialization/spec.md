# Spec Delta

## ADDED Requirements

### Requirement: Avoid duplicate EFD019 findings at one materializer
EFD005 SHALL NOT report a materializer when the same invocation is eligible for EFD019 materialize-then-reduce analysis. EFD019 SHALL remain the more specific explanation when the buffered result is immediately reduced to an element, count, existence check, or aggregate.

#### Scenario: Immediate reduction after ToList
- **WHEN** an unbounded materializer is immediately consumed by a supported EFD019 reducer such as `First()` or `Count()`
- **THEN** EFD019 may report and EFD005 does not report at that materializer

#### Scenario: Unsupported reduction
- **WHEN** an eligible unbounded materializer is immediately consumed by a reducer overload or lambda that EFD019 cannot classify as SQL-capable
- **THEN** EFD005 may report because no duplicate EFD019 finding exists
