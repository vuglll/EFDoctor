## MODIFIED Requirements

### Requirement: Load targets with the SDK selected for the target
The CLI SHALL select the .NET SDK used to load a target by resolving SDKs from the target's own directory, so that a `global.json` governing the target is honored regardless of the directory from which the CLI is run. When no installed SDK satisfies the target's resolution, the CLI SHALL report an analysis failure that names the target directory and explains that a matching .NET SDK must be installed, and SHALL NOT silently load the target with a different SDK. Resolving the SDK SHALL NOT write anything to the CLI's standard output, so that in JSON mode standard output holds only the error envelope.

#### Scenario: Target without global.json
- **WHEN** a user analyzes a target whose directory tree has no `global.json`
- **THEN** the CLI loads it with the default installed SDK, as before

#### Scenario: Target pins an installed SDK
- **WHEN** a user analyzes a target whose `global.json` selects an installed SDK and runs the CLI from a different directory
- **THEN** the CLI resolves the SDK from the target's directory rather than the working directory

#### Scenario: Target pins a missing SDK
- **WHEN** a user analyzes a target whose `global.json` requires an SDK that is not installed
- **THEN** the CLI exits with code `2` and an error explaining that no matching .NET SDK was found for the target directory

#### Scenario: Missing SDK in JSON mode keeps standard output pure
- **WHEN** the CLI process analyzes, in JSON mode, a target whose `global.json` requires an SDK that is not installed
- **THEN** the process's standard output parses as exactly one JSON document, the error envelope with code `analysis-failure`, and contains no installed-SDK list
