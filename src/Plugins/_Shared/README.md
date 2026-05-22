# Shared Plugin Resources

This folder contains resources shared across all OpenTavu plugin projects.

## Contents

| File | Purpose |
|------|---------|
| `OpenTavu.snk` | Strong-name key file used to sign all plugin assemblies. Shared so every OpenTavu plugin has the same Public Key Token. |
| `Common/PluginBase.cs` | Abstract base class implementing common plugin plumbing (service extraction, tracing, depth guard, error handling). |
| `Common/LocalPluginContext.cs` | Context wrapper passed to each plugin's `ExecuteInternal`. |

## How shared C# files are consumed by plugin projects

The files in `Common/` are **NOT** referenced via a separate compiled assembly.
Instead, each plugin project includes them via Visual Studio's **"Add as Link"**
feature. This means:

- The physical files exist only here, in `_Shared/Common/`.
- Each plugin project's `.csproj` contains `<Compile Include="..\_Shared\Common\X.cs"><Link>Common\X.cs</Link></Compile>` entries.
- At build time, each plugin compiles its own copy of the shared code into a
  fully self-contained assembly — no runtime dependency on a separate DLL.

### Why Linked Files instead of a shared project / DLL?

1. **Self-contained deployment.** Each plugin assembly can be uploaded to a
   Dataverse environment with no companion DLL.
2. **Lower friction for consumers cloning the OpenTavu repo.** They can deploy
   one plugin without deploying a "common" assembly first.
3. **No ILMerge or assembly-merging tooling needed.**

## How to add a new plugin project to OpenTavu

1. Create a new Class Library (.NET Framework 4.6.2) under `src/Plugins/`,
   following the naming convention `Pl.<Entity>.<Action>`
2. Install NuGet `Microsoft.CrmSdk.CoreAssemblies` (set CopyLocal = False on
   the references, since Dataverse provides these at runtime).
3. Configure signing: project Properties → Signing → "Sign the assembly" → 
   Browse to `..\_Shared\OpenTavu.snk`.
4. Add the linked common files:
   - Right-click project → Add → Existing Item
   - Navigate to `..\_Shared\Common\`
   - Select `PluginBase.cs` and `LocalPluginContext.cs`
   - Click the **dropdown arrow** next to the "Add" button → "Add as Link"
   - Move both linked files into a `Common\` folder inside the project for
     visual organization.

## How to modify shared code

Edit the file directly in `_Shared/Common/`. All plugin projects will pick up
the change on the next build. Verify all dependent plugins compile and pass
their tests before committing.