# Connector Structure Migrator (.NET 8)

Converts connector JSON files from the **old** connector structure to the **new** structure
(connector metadata + section metadata + a top-level `FormBuilderInfo`). One output file is
written per input file, with the same file name, into the folder you choose.

## Projects

```
ConnectorMigrator.sln
├─ src/ConnectorMigrator.Core       All conversion logic (class library, no UI)
│  ├─ Mapping/ConnectorVocabulary   Every name and mapping table (edit rules here)
│  ├─ Json/JsonHelpers              Order-preserving JsonNode helpers, read/write options
│  ├─ Inspection/StepInspector      "Does this part hold non-empty steps?"
│  ├─ Inspection/SectionDetector    Pure detection rules (capabilities, options, methods, features)
│  ├─ Transformers/                 One transformer per new section
│  │   ├─ ConnectorMetadataTransformer   Metadata.connectorId, Metadata.capabilities
│  │   ├─ SsoMetadataTransformer         SSOInfo.Metadata
│  │   ├─ LcmMetadataTransformer         LCMInfo.Metadata
│  │   ├─ UagMetadataTransformer         UAGInfo.Metadata
│  │   └─ FormBuilderRelocator           *FormBuilderData -> FormBuilderInfo
│  ├─ Validation/InputValidator     Parse + structural checks, readable errors
│  ├─ Pipeline/                     ConversionContext, ConversionPipeline
│  └─ ConnectorConversionService    File/folder batch conversion (used by CLI and GUI)
├─ src/ConnectorMigrator.Cli        Cross-platform console tool
├─ src/ConnectorMigrator.WinForms   Windows desktop GUI
└─ tests/ConnectorMigrator.Tests    xUnit tests (real O365/AWS fixtures + rule tests)
```

The converter works on the JSON DOM (`System.Text.Json.Nodes`), not typed models, so any field
it does not know about is carried over untouched and property order is preserved.

## Usage

### CLI

```bash
dotnet run --project src/ConnectorMigrator.Cli -- <file-or-folder>... -o <output-folder> [options]
```

| Option | Meaning |
|---|---|
| `-o, --output <folder>` | Output folder (created if missing). Required. |
| `-r, --recursive` | Include sub-folders of input folders. |
| `--overwrite` | Replace existing output files (default: refuse). |
| `--id-mode slug\|guid` | `connectorId` style. Default `slug` (`Microsoft O365` → `microsoft-o365`). |
| `--report <file>` | Write a JSON report of the run (per-file status, warnings, derived metadata). |
| `-q, --quiet` | Print only failures and the summary. |

Exit codes: `0` all converted, `1` one or more files failed, `2` invalid arguments.

### GUI (Windows)

```bash
dotnet run --project src/ConnectorMigrator.WinForms
```

Add files or folders (or drag and drop), choose the output folder, then click **Convert**.
Double-click a result row to see the derived metadata and all warnings.

### Tests

```bash
dotnet test
```

## Conversion rules

"Non-empty steps" means a `steps` array with at least one item. For the SSO browser-bot
`Steps` object, it must contain at least one non-empty array (for example `SeleniumData`).

| New field | Rule |
|---|---|
| `Metadata.connectorId` | Slug of `AssetName`, made unique within the batch (`-2`, `-3`…). Falls back to the file name if `AssetName` is empty. `--id-mode guid` gives a random GUID instead. |
| `Metadata.capabilities` | `DISCOVERY` if UAGInfo recon has steps; `LIFECYCLE` if any `SupportedLCMMethod` block has steps; `VAULT` if VaultInfo has steps or any configured value (for example `CLI.ServiceType: [49]`; empty `{}` blocks, empty lists and FormBuilder lists do not count); `SSO` if `SSOOptions` is non-empty. Written in FRS order: DISCOVERY, LIFECYCLE, VAULT, SSO. |
| `SSOInfo.Metadata.ApplicationUrl` | `Metadata.AssetURL`. |
| `SSOInfo.Metadata.SSOOptions` | `ArconBOT` with steps → `RPA`; `CLI` with steps, or with at least one launcher entry (`SSH`, `App MobaXterm`, `App Xshell`, `App SecureCRT`, …) whose `Metadata` holds a configured value → `RPA`; `SAML`/`OIDC`/`OAuth` with any configured value → same name. Empty `{}` blocks are ignored. |
| `LCMInfo.Metadata.MethodUsed` | Every method block under `SupportedLCMMethod` that holds steps (`API`, `SDK`, `LDAP`, `SSH`, `ArconBOT`, …), named as in the file, in file order. Nothing is hard-coded, so new methods are picked up automatically. |
| `LCMInfo.Metadata.features` | Per operation container with steps: Provisioning → `CREATE`, ModificationUser → `UPDATE`, ModificationEntitlement → `UPDATE_ENTITLEMENT`, Disable → `DISABLE`, Deprovisioning → `DELETE`, Reactivation → `REACTIVATE`, ReactivationWithModification → `REACTIVATE_WITH_UPDATE`. Container names are matched case-insensitively (`ReActivationInfo` = `ReactivationInfo`). |
| `UAGInfo.Metadata.MethodUsed` | Same rule as LCM, over `SupportedReconMethod`. |
| `UAGInfo.Metadata.discoveryObjects` | `Metadata.EntityNames` copied as-is. |
| `FormBuilderInfo` | Every `*FormBuilderData` list under `LCMInfo` and `UAGInfo` is removed from its section and merged here, de-duplicated by `KeyName` (first occurrence wins). Each entry is unchanged except for two appended attributes: `UseItforCapability` (`LCMInfo` / `UAGInfo`) and `ForSupportedBotMethod` (the method(s) the field was found under). Both are unions over every place the field appeared. |

Everything else is preserved as-is, including empty method blocks, `EntityNames`, and sections
outside the scope such as `SOT` (with its own `SOTFormBuilderData`).

### Warnings (the conversion still succeeds)

- A FormBuilder field with the same `KeyName` has different content in different lists. The first
  version is kept and the differing properties are named.
- An LCM container with steps has no feature mapping. It is not guessed; add it to
  `ConnectorVocabulary.LcmFeatureMap`.
- `AssetName`, `AssetURL` or `EntityNames` is missing or empty, or a section is absent.

### Errors (the file is skipped; the rest of the batch continues)

- Invalid JSON (reported with line and position), an empty file, or a non-object root.
- Duplicate property names (reported with the JSON path).
- `Metadata` is missing, a section has the wrong type, or no connector sections are found.
- The file is already in the new structure.
- The output file exists without `--overwrite`, the output would overwrite the input, or two
  inputs in the batch have the same file name.

JSON comments and trailing commas in the input are tolerated.

### Changing a rule

All names and mapping tables are in `src/ConnectorMigrator.Core/Mapping/ConnectorVocabulary.cs`.
For example, to rename a feature or add a new LCM operation, edit `LcmFeatureMap`. `MethodUsed` needs no
configuration: any method block with steps is listed.
