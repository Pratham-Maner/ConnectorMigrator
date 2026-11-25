# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A .NET 8 tool that converts Arcon connector JSON files from the **old** structure to the **new** structure (adds `Metadata.connectorId`/`capabilities`, per-section `Metadata` blocks for SSO/LCM/UAG, and moves every `*FormBuilderData` list into a top-level `FormBuilderInfo`). README.md holds the authoritative conversion rules table, warning/error semantics, and CLI options — keep it in sync when rules change.

## Commands

```powershell
dotnet build                                   # whole solution
dotnet test                                    # xUnit, all tests
dotnet test --filter "FullyQualifiedName~RealConnectorTests"          # one class
dotnet test --filter "FullyQualifiedName~RealConnectorTests.O365_section_metadata"  # one test

# CLI (note the `--` separator); exit codes 0 ok / 1 some files failed / 2 bad args
dotnet run --project src/ConnectorMigrator.Cli -- <file-or-folder>... -o <out> [-r] [--overwrite] [--id-mode slug|guid] [--report r.json] [-q]

# Windows GUI (net8.0-windows)
dotnet run --project src/ConnectorMigrator.WinForms
```

No linter/formatter is configured. `Nullable` and `ImplicitUsings` are enabled.

## Architecture

All logic lives in `ConnectorMigrator.Core`; the CLI and WinForms projects are thin shells over `ConnectorConversionService`.

- **Flow**: `ConnectorConversionService.ConvertFiles` (batch, per-file isolation, output-path safety checks, UTF-8 no BOM) → `ConversionPipeline.Convert` (one file) → `InputValidator.ParseAndValidate` → each `IConnectorTransformer.Apply(ConversionContext)` in order.
- **JSON DOM, not typed models**: everything operates on `System.Text.Json.Nodes.JsonObject` and mutates the root in place. Unknown fields must pass through untouched and **property order matters** (tests assert exact key order). Use `JsonHelpers` (`TryGetProperty` is case-insensitive and returns the real key; `SetFirst`/`SetBefore` control placement) rather than raw indexers.
- **Detection vs. writing**: `Inspection/SectionDetector` and `StepInspector` are pure read-only rules ("does this part hold non-empty steps?"); transformers decide where results go. `StepInspector` deliberately skips FormBuilder lists, which is why transformer order only affects layout, not results.
- **Vocabulary is the single source of names/mappings**: `Mapping/ConnectorVocabulary.cs` holds section keys, capability order, `SsoOptionMap`, `TrackedBotMethods` (API/SDK), `LcmFeatureMap`, and LCM section-name normalization. Change rules there, not in transformers.
- **Errors vs. warnings**: throw `ConnectorValidationException` to fail a file (the batch continues); call `context.Warn(...)` for non-fatal issues (deduplicated). The pipeline wraps `InvalidOperationException`/`ArgumentException`/`FormatException` from transformers into `ConnectorValidationException`.
- **Batch-scoped state**: `ConnectorIdRegistry` is shared across one batch to make slug ids unique (`-2`, `-3`…); `ConversionSummary` on the context feeds the CLI/GUI/report output and tests.

## Tests

- `RealConnectorTests` run end-to-end against real fixtures in `tests/ConnectorMigrator.Tests/Fixtures/` (O365 old structure, AWS, and `Microsoft_O365_NewStructure.json` as the expected reference). Fixtures are copied to the output dir; load via `TestSupport.Fixture(name)`.
- `RuleTests` build connectors from `TestSupport.MinimalConnector()` and mutate it to cover single rules — prefer this pattern for new rules.
- `TestSupport.StripMigratedParts` removes converter-added parts so output can be compared with input to prove nothing else changed.
- `samples/converted/` contains example output and a `report.json`; it is not used by tests.
