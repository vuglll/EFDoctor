## 1. Analyzer

- [x] 1.1 Add `EfSplitQueryConfiguration`: the syntactic `UseQuerySplittingBehavior(SplitQuery)` scan and the global option
- [x] 1.2 Skip EFD006 findings when split queries are the default
- [x] 1.3 Add analyzer tests for the call, a `SingleQuery` argument, no call, and the option

## 2. CLI

- [x] 2.1 Scan every C# project for the call, and pass the option to all of them
- [x] 2.2 Add the split-query data and host fixtures, and an end-to-end test

## 3. Docs

- [x] 3.1 Update the EFD006 docs, the rules README, usage, and the CHANGELOG
