# Shared Dataverse Code Resources

This folder contains code and resources shared across all OpenTavu projects that
run inside the Dataverse sandbox: Plugins, Custom Workflow Activities, and
similar managed code.

## Contents

| File | Purpose |
|------|---------|
| `OpenTavu.snk` | Strong-name key file used to sign all Dataverse assemblies. Shared so every OpenTavu assembly carries the same Public Key Token. |
| `Common/PluginBase.cs` | Abstract base class for plugins (`IPlugin`). Handles service extraction, tracing, depth guard, error handling. |
| `Common/LocalPluginContext.cs` | Context wrapper passed to each plugin's `ExecuteInternal`. |
| `Common/WorkflowActivityBase.cs` | (Planned) Abstract base class for Custom Workflow Activities (`CodeActivity`). Same plumbing pattern as `PluginBase` adapted to workflow context. |
| `Common/LocalWorkflowContext.cs` | (Planned) Context wrapper for workflow activities. |

## How shared C# files are consumed by projects

The files in `Common/` are **NOT** referenced via a separate compiled assembly.
Instead, each consumer project includes them via Visual Studio's **"Add as Link"**
feature. This means:

- The physical files exist only here, in `src/_Shared/Common/`.
- Each consumer project's `.csproj` contains `<Compile Include="..\..\_Shared\Common\X.cs"><Link>Common\X.cs</Link></Compile>` entries.
- At build time, each consumer compiles its own copy of the shared code into a
  fully self-contained assembly — no runtime dependency on a separate DLL.

### Why Linked Files instead of a shared project / DLL?

1. **Self-contained deployment.** Each plugin or workflow assembly can be
   uploaded to a Dataverse environment with no companion DLL.
2. **Lower friction for consumers cloning the OpenTavu repo.** They can deploy
   one component without deploying a "common" assembly first.
3. **No ILMerge or assembly-merging tooling needed.**

## How to add a new Plugin project

1. Create a new Class Library (.NET Framework 4.6.2) under `src/Plugins/`,
   naming convention `Pl.<Entity>.<Action>`.
2. Install NuGet `Microsoft.CrmSdk.CoreAssemblies` (set CopyLocal = False on
   the references, since Dataverse provides these at runtime).
3. Configure signing: project Properties → Signing → "Sign the assembly" → 
   Browse to `..\..\_Shared\OpenTavu.snk`.
4. Add the linked common files:
   - Right-click project → Add → Existing Item
   - Navigate to `..\..\_Shared\Common\`
   - Select `PluginBase.cs` and `LocalPluginContext.cs`
   - Click the dropdown arrow next to "Add" → "Add as Link"
   - Move both linked files into a `Common\` folder inside the project for
     visual organization.

## How to add a new Custom Workflow Activity project

Same as above, but:
- Create the project under `src/Workflows/` with naming `Wf.<Entity>.<Action>`.
- Install NuGet `Microsoft.CrmSdk.Workflow` (which includes the CodeActivity
  base class).
- Link `WorkflowActivityBase.cs` and `LocalWorkflowContext.cs` instead of (or
  in addition to) the plugin equivalents.

## How to modify shared code

Edit the file directly in `src/_Shared/Common/`. All consumer projects will
pick up the change on the next build. Verify all dependent projects compile
and pass their tests before committing.