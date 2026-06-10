# OpenTavu Templates

This folder contains `dotnet new` templates that scaffold new OpenTavu code
projects with the correct conventions (shared strong-name key, linked common
base classes, naming, NuGet packages, code skeleton).

## Available templates

### `opentavu-plugin`

Scaffolds a new Dataverse plugin project (`Pl.<EntityShortName>.<ActionName>`).
Used for synchronous, transactional logic that runs in the Dataverse pipeline
(Pre-Validation, Pre-Operation, Post-Operation).

Generated project inherits from `OpenTavu.Dataverse.Common.PluginBase`.

```powershell
dotnet new opentavu-plugin `
  -n Pl.<EntityShortName>.<ActionName> `
  --entityName <table_logical_name> `
  --actionName <ActionName> `
  --entityShortName <EntityShortName>
```

Example:

```powershell
dotnet new opentavu-plugin `
  -n Pl.Case.ResponseTimeTracker `
  --entityName tavu_case `
  --actionName ResponseTimeTracker `
  --entityShortName Case
```

> **`-n` is mandatory.** It is what names the project and creates its own
> subfolder. If you omit it, `dotnet new` falls back to the **current directory
> name** as the project name and writes the files **into the current folder**
> instead of a new subfolder. Running it from `src\` without `-n` produces a
> broken `src.csproj` with `namespace src` and wrong relative paths to
> `_Shared\Common\`. Always pass `-n Pl.<EntityShortName>.<ActionName>` and run
> it from `src\Plugins\` so the generated project lands at
> `src\Plugins\Pl.<EntityShortName>.<ActionName>\`.
>
> `--entityShortName` does **not** drive the folder name on its own — it has no
> `replaces`/`fileRename` in the manifest; you type it into the `-n` value.

> **SDK reference version is coupled.** The generated `.csproj` resolves
> `Microsoft.Xrm.Sdk` and `Microsoft.Crm.Sdk.Proxy` via a `HintPath` into
> `..\..\packages\Microsoft.CrmSdk.CoreAssemblies.<version>\lib\net462\`. That
> `<version>` is hardcoded to match the template's `packages.config`
> (currently `9.0.2.51`). If you bump `CoreAssemblies` in `packages.config`,
> update the two `HintPath` lines in `Pl.Entity.Action.csproj` to the same
> version, or generated projects will fail with `CS0246` on every SDK type
> (`Entity`, `IPlugin`, `IPluginExecutionContext`, etc.).

### `opentavu-cwa` (planned)

Will scaffold a new Custom Workflow Activity project
(`Wf.<EntityShortName>.<ActionName>`). Used for reusable workflow steps
invocable from Power Automate flows or classic Dataverse workflows.

Generated project will inherit from `OpenTavu.Dataverse.Common.WorkflowActivityBase`
(to be added when the first CWA project is built).

## Installation

Templates must be installed once per development machine before use:

```powershell
cd <path-to-opentavu-repo>
dotnet new install .\templates\opentavu-plugin
# When opentavu-cwa exists:
# dotnet new install .\templates\opentavu-cwa
```

To uninstall:

```powershell
dotnet new uninstall <path-to-opentavu-repo>\templates\opentavu-plugin
```

To list installed templates:

```powershell
dotnet new list opentavu
```

## Workflow after creating a new project

The template creates the project files but does not add the project to the
solution. After running `dotnet new opentavu-plugin`:

1. Open Visual Studio with `OpenTavu.sln`.
2. Right-click the `Plugins` solution folder, **Add**, **Existing Project**.
3. Select the generated `.csproj` file.
4. Restore NuGet packages (Visual Studio will prompt automatically, or run
   `nuget restore` from the project folder).
5. Replace the `TODO` sections in the generated `.cs` file with business logic.
6. Build, register in Plugin Registration Tool, and add to the Dataverse
   unmanaged solution as documented in `src/_Shared/README.md`.

## Why separate templates instead of one parameterized template?

Each Dataverse code asset (Plugin, Custom Workflow Activity) has distinct
inheritance, NuGet dependencies, and registration semantics. Keeping templates
separate avoids conditional logic inside template files, which is brittle and
hard to debug. Microsoft own SDK templates follow this pattern (`worker`,
`webapi`, `classlib` are independent templates, not a parameterized one).

## Adding a new template

1. Create a new subfolder under `templates/` named `opentavu-<asset-type>`.
2. Follow the structure of `opentavu-plugin/` as reference:
   - `.template.config/template.json` with the template manifest
   - Skeleton files at the root of the template folder (no `content/` subfolder)
3. The `template.json` must use:
   - `shortName` matching the CLI invocation
   - `identity` globally unique across all OpenTavu templates
   - `sourceName` with the placeholder string used in filenames/namespaces
   - `symbols` block defining required parameters with `replaces` and `fileRename`
4. Test with a throwaway project name (e.g., `Pl.Test.Trial`) before committing.
5. Update this README with the new template invocation example.

## Why the template files live at template root instead of inside `content/`

The `dotnet new` template engine recognizes the template root folder as the
content root by default. Adding a `content/` subfolder requires an explicit
`sources` block in `template.json` to point at it. Keeping files at the root
is simpler, less error-prone, and matches the structure of most official
Microsoft SDK templates.