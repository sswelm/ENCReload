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
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true
# then copy bin/Release/net8.0/win-x64/publish/glbconv.exe over Tools/glbconv/glbconv.exe
```

**Do NOT add `-p:PublishTrimmed=true`.** SharpGLTF's JSON layer trips trim analysis (IL2026) and trimming
silently *changes* the OBJ/MTL output on some models (verified: 4 of 11 models differed under trimming).
`EnableCompressionInSingleFile` shrinks the exe ~68 MB → ~35 MB **losslessly** (the bundle unpacks at runtime),
so trimming isn't needed anyway.

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

## Reproducibility note (SharpGLTF UV decode) — resolved 2026-07-12

`glbconv.exe` is now pinned to **SharpGLTF.Core 1.0.6**, so rebuilds are reproducible from here on. The prior exe was
built with an older, unrecorded SharpGLTF that emitted **raw tiled UVs** (e.g. `U=19.8`); 1.0.6 pre-folds them into
`[0,1)` (`U=0.8`). The baker folds every UV per-vertex anyway (`u -= floor(u)`), so the two are functionally equivalent
except at tile boundaries. The 2026-07-12 rebuild (which brought in the T5 winding fix) was verified **geometry-identical
across all 11 registry models** (v/vn/f line counts), with only that fold difference — on the Cobra, **3 of 208,198**
verts shift one tile (0.0014%, sub-pixel on a seam). The previous exe is preserved in git history.

**After any future rebuild, re-verify a tiled-UV model (the Cobra camo) in-game** — the change is tiny but touches the
exact class of model the tiled-UV handling was fixed for.
