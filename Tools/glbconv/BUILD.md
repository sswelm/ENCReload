# Building glbconv

`glbconv` is a small .NET 8 console app. The source is `Program.cs.src` — kept with a `.src` extension so Unity's
compiler ignores it (a bare `.cs` anywhere the editor scans would be compiled into the game assembly). The Factory runs
the built **`glbconv.exe`** (self-contained single-file, so no .NET install is needed on the modder's machine).

## Rebuild

From a scratch directory **outside** `Assets/` (e.g. a temp folder), copy the source to `Program.cs`, drop in the
`glbconv.csproj` below, then:

```sh
cp Program.cs.src Program.cs
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
# then copy bin/Release/net8.0/win-x64/publish/glbconv.exe over Tools/glbconv/glbconv.exe
```

`glbconv.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <AssemblyName>glbconv</AssemblyName>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="SharpGLTF.Core" Version="1.0.6" />
  </ItemGroup>
</Project>
```

## ⚠ Reproducibility caveat (SharpGLTF UV decode)

The **currently-shipped** `glbconv.exe` was built with an older SharpGLTF that emits **raw tiled UVs** (e.g. `U=19.8`).
SharpGLTF 1.0.x pre-folds tiled UVs into `[0,1)` (`U=0.8`). The baker folds every UV per-vertex anyway
(`u -= floor(u)`), so the two are **functionally equivalent except at tile boundaries** — on the Cobra (208,198 verts,
heavily tiled camo UVs) exactly **3 vertices** land one tile over after folding (0.0014%, sub-pixel on a seam).

Consequences:
- The exact original SharpGLTF version is **not pinned** and could not be reproduced (none of 1.0.0–1.0.6 match the
  shipped exe byte-for-byte). Pinning `1.0.6` above makes future rebuilds **reproducible from here on**, at the cost of
  that one-time raw→pre-folded UV shift.
- **After any rebuild, re-verify a tiled-UV model (the Cobra camo) in-game** before shipping — the change is tiny but
  it touches the exact class of model we fixed the tiled-UV handling for.

## Note: the shipped exe can lag the source

`Program.cs.src` is the source of truth; `glbconv.exe` is a build artifact that is only refreshed by a deliberate
rebuild. If the two are out of sync (e.g. a source fix landed but the exe wasn't rebuilt to avoid the UV shift above),
that is intentional and noted in the commit that changed the source.
