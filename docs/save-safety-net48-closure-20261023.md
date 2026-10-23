# Captured Net48 NRBF Closure Verification

## Repeatable Regression

The repository now retains this boundary as `checks/SaveSafety/PackagedCodecChecks.csproj`
and includes it in `test-all.ps1`. It uses a clean net48 executable with no package
references, byte-loads the built plugin, verifies all nine embedded resources, probes
the complete resolver closure, checks repeated assembly identity, decodes a native
fixture, and verifies its hash. The current source check passed with the real
`24hour-ready-test.txt` fixture. It prints the exact tested plugin MVID/SHA-256.
The historical captures below retain their original identities and results.

```powershell
& 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe' `
    '.\checks\SaveSafety\PackagedCodecChecks.csproj' /t:Build /p:Configuration=Release
& '.\checks\SaveSafety\bin\PackagedCodec\PackagedCodecChecks.exe' `
    '.\NGUInjector\bin\Release\NGUInjector.dll' `
    'C:\Users\Kevin\AppData\LocalLow\NGU Industries\NGU Idle\24hour-ready-test.txt'
```

This is Windows CLR byte-load coverage, not actual Unity Mono/injector acceptance.

## Latest Capture: Byte-Load Closure Pass

The latest capture passes the complete isolated net48/x64 byte-load smoke,
**exit 0**, with the revised tuple-only canonical runtime binding contract.
All nine dependency resources are embedded, all eight non-tuple codec libraries
load from memory with exact expected identities, and the actual production codec
parses the real fixture as exactly `2283976.2493571` seconds. This supersedes the
earlier missing-resource and strict-tuple-probe failures below, which remain
preserved as historical evidence.

### Passing Artifact

- Source: `C:\Users\Kevin\AppData\Local\Temp\opencode\save-safety-build\bin\dist\injector\NGUInjector.dll`
- Assembly: `NGUInjector, Version=1.0.9792.23405, Culture=neutral, PublicKeyToken=null`
- MVID: `d4599845-3efc-480f-b710-5da592a9dad0`
- SHA256: `4b4defcd82c0348b1f2e4119decb04ba48c8caefc2a7b311faa0c6d70a9a7a82`
- Temp root: `C:\Users\Kevin\AppData\Local\Temp\opencode\nrbf-net48-closure-20261023`
- Clean application base and working directory: `<temp root>\bytes-bin`.

The output directory contains only `ByteClosureSmoke.exe` and its startup config.
The harness references only `mscorlib`, `System`, and `System.Core`; it has no
production source/project, codec, or helper references. Its resolver observer
only logs and returns null. The plugin is initially loaded with
`Assembly.Load(pluginBytes)` and its Register method is explicitly reflected;
Loader/Main/Unity methods are not invoked.

### Passing Results

| Check | Observed Result |
| --- | --- |
| Runtime manifest and independent metadata audit | All 9 expected embedded DLLs; 12 total root manifest resources |
| Plugin byte load | Empty `Location`, `GAC=False` |
| Natural codec dependency loads | All 8 non-tuple libraries have empty `Location`, `GAC=False`, and exact expected FullName |
| Real fixture total playtime | Exactly `2283976.2493571`, double equality and round-trip text |
| Other observed metadata | Rebirth seconds `67661.7487057205`, rebirth `230`, version `1260`, LastTime `1792766209` |
| Production SHA256 | Matches independent fixture hash `073c26b050b8d4c3a41187e8fa104ac7af9d26ecb4aeff71873b4bff37ce84a9` |
| Direct production tuple resolver invocation | Succeeds; returns canonical GAC tuple 4.0.0.0 with the requested simple name and public-key token |
| Type lookups through actual returned tuple assembly | All 11 expected forwarded types resolve to host `mscorlib` |
| Wrong versions for non-tuple dependencies | All 8 rejected with the expected FileLoadException |
| Tuple with wrong public-key token | Rejected with the expected FileLoadException |
| Null requester, foreign harness requester, nonembedded name | All rejected with null |
| Plugin bytes and fixture bytes/last-write timestamp | Unchanged after the complete smoke |
| Game/Unity assemblies loaded by smoke | None |
| Full smoke / metadata audit | Exit 0 / exit 0 |

The eight memory-only libraries remain NRBF, HashCode, Reflection.Metadata,
Collections.Immutable, Memory, Buffers, Unsafe, and Numerics.Vectors, with the
same exact versions listed in the earlier resource table below. Natural loading
and exact-identity checks run before the negative resolver probes.

### Expected Tuple Unification

The direct resolver request is:

`System.ValueTuple, Version=4.0.5.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51`

The actual returned assembly is:

`System.ValueTuple, Version=4.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51`

It is the canonical host assembly at:

`C:\WINDOWS\Microsoft.Net\assembly\GAC_MSIL\System.ValueTuple\v4.0_4.0.0.0__cc7b13ffcd2ddd51\System.ValueTuple.dll`

This is an expected pass, not a claim that the returned object is the embedded
facade or that tuple itself was memory-loaded. The temporary assertion now
checks the actual tuple simple name and public-key token, allowing the runtime's
version selection. Non-tuple identity assertions remain exact. Guard tests now
exercise wrong versions on each of the other eight libraries and a wrong token
on tuple, rather than incorrectly demanding that tuple version unification fail.

All eleven type lookups on the returned canonical assembly resolve to host
`mscorlib`: `System.ValueTuple`, generic arities 1 through 8,
`System.TupleExtensions`, and
`System.Runtime.CompilerServices.TupleElementNamesAttribute`.

Independent metadata inspection of this capture also reconfirms the embedded
tuple resource is version 4.0.5.0, token `cc7b13ffcd2ddd51`, MVID
`c9563f77-d24c-4cc1-96e5-97a7362b6837`, with all eleven facade forwarder records.
Its SHA256 remains
`7cd7ac314a72774b5e2255fd798f876926c63e19f9b8304cae25df012beabbcb`.

### Latest Commands And Scope

The same `$root`, `$package`, `$managed`, `$fixture`, and `$msbuild` values defined
in the historical command block below were used. The smoke working directory
was `$root\bytes-bin`.

```powershell
& $msbuild "$root\ByteClosureSmoke.csproj" /t:Build /p:Configuration=Release /m:1 /nologo /verbosity:minimal /fileLogger "/fileLoggerParameters:LogFile=$root\tuple-canonical-bytes-build.log;Verbosity=normal"
& "$root\bytes-bin\ByteClosureSmoke.exe" "$package\NGUInjector.dll" $fixture "$root\tuple-canonical-bytes-smoke.log"
& "$root\audit\bin\MetadataAudit.exe" $package $managed "$root\tuple-canonical-metadata-audit.log"
```

Only the temporary harness was changed and compiled in this follow-up; its build
exited 0 with zero warnings/errors. The existing separate metadata auditor was
executed without modification. The three new `tuple-canonical-*` logs preserve
all outcomes and actual identities; earlier logs remain untouched. The expected
identity-rejection exceptions are successful negative assertions, not smoke
failures. The only repository edit is this note.

The pre-existing SMI byte/image loading path supplies no plugin filename. Its
dependency-discovery problem is addressed by embedded resources and the plugin's
resolver, demonstrated under CLR by the plugin and eight libraries loading with
empty Location from a directory containing no dependency DLLs. No sibling-path
or writable-runtime-directory discovery is required for those loads.

**Unity Mono remains untested.** This is net48-targeted execution on the installed
Windows CLR (`4.0.30319.42000`, host `mscorlib` file version `4.8.9363.0`), not
execution through Unity Mono or native SMI. Standalone Mono is unavailable. No
production build/edit, game process interaction, Loader/Unity method invocation,
native Mono call, deployment, or injection occurred. The embedded-resource smoke
does not replace actual Unity Mono/BCL compatibility verification.

## Historical Strict Tuple Probe Failure

The following section records MVID `4164bd75-fb4a-4dcc-b572-fc4b4be198a3` before
the tuple-only canonical binding allowance. Its harness failure is superseded by
the passing artifact and contract tests above; original evidence is retained.

After the parent moved the embedding hook to `BeforeTargets="AssignTargetPaths"`
and rebuilt the capture, the **same unmodified verification executables** now
confirm all nine embedded dependency DLLs. The clean-directory byte-loaded codec
successfully reads the real fixture, with all eight codec libraries loaded from
memory. The complete harness still exits 1 at its later explicit tuple-resolver
probe. This is not a missing-resource failure or a codec parse failure.

### Rebuilt Artifact

- Source: `C:\Users\Kevin\AppData\Local\Temp\opencode\save-safety-build\bin\dist\injector\NGUInjector.dll`
- Assembly: `NGUInjector, Version=1.0.9792.22467, Culture=neutral, PublicKeyToken=null`
- MVID: `4164bd75-fb4a-4dcc-b572-fc4b4be198a3`
- SHA256: `8f178b668ae0fc2d4dc59ab5c6f6b3aae06f1f503e8f27706c62e15e8fc692b7`
- Temp root: `C:\Users\Kevin\AppData\Local\Temp\opencode\nrbf-net48-closure-20261023`
- Application base and working directory: `<temp root>\bytes-bin`, containing
  only the existing harness EXE/config, no plugin, dependency, or helper DLLs.

### Rebuild Outcomes

| Check | Exact Outcome |
| --- | --- |
| Runtime manifest plus independent Cecil audit | 9/9 dependency resources; 12 total root manifest resources |
| Initial `Assembly.Load(pluginBytes)` | Plugin `Location` empty, `GAC=False` |
| Reflection invocation of `SaveSafetyDependencies.Register` | Succeeded, without Loader/Main/Unity invocation |
| Eight codec libraries | All naturally loaded through the plugin's resolver, `Location` empty and `GAC=False`; exact expected assembly identities |
| `SaveSafetyCatalog.Read(realFixture, 1260)` | Passed |
| Total playtime | Exactly `2283976.2493571`, double equality and round-trip text |
| Other metadata | Rebirth seconds `67661.7487057205`, rebirth `230`, version `1260`, LastTime `1792766209` |
| Natural tuple binding | Windows GAC `System.ValueTuple` 4.0.0.0, not the embedded facade |
| Forced resolver request for tuple 4.0.5.0, plugin requester | Threw `TargetInvocationException` wrapping the resolver's `FileLoadException` for embedded identity mismatch |
| Runtime facade-forwarder assertions and subsequent resolver-guard assertions | Not reached after the tuple probe exception |
| Complete byte-load harness | Exit 1; `PASS_MANIFEST` and `PASS_CODEC` were reached first |
| Separate metadata audit | Exit 0; all nine resource DLLs decoded as assembly metadata |
| Captured plugin and fixture after smoke | Plugin hash unchanged; fixture bytes and last-write timestamp unchanged |
| Game/Unity assemblies in smoke process | None |

The nine manifest resource suffixes and decoded assembly versions are:

| Resource Suffix After `NGUInjector.Dependencies.` | Assembly Version |
| --- | --- |
| `Microsoft.Bcl.HashCode.dll` | `6.0.0.0` |
| `System.Buffers.dll` | `4.0.5.0` |
| `System.Collections.Immutable.dll` | `10.0.0.13` |
| `System.Formats.Nrbf.dll` | `10.0.0.13` |
| `System.Memory.dll` | `4.0.5.0` |
| `System.Numerics.Vectors.dll` | `4.1.6.0` |
| `System.Reflection.Metadata.dll` | `10.0.0.13` |
| `System.Runtime.CompilerServices.Unsafe.dll` | `6.0.3.0` |
| `System.ValueTuple.dll` | `4.0.5.0` |

### Tuple Probe Qualification

The normal codec call binds tuples to:

`C:\WINDOWS\Microsoft.Net\assembly\GAC_MSIL\System.ValueTuple\v4.0_4.0.0.0__cc7b13ffcd2ddd51\System.ValueTuple.dll`

The unchanged harness then directly invokes the production resolver with:

`System.ValueTuple, Version=4.0.5.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51`

It throws:

```text
System.IO.FileLoadException: Embedded save-safety dependency identity mismatch:
System.ValueTuple, Version=4.0.5.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51
```

No new tuple AssemblyLoad event occurs during that explicit probe. This is
consistent with the CLR reusing/unifying its already loaded GAC tuple assembly
before the resolver's exact-FullName check. It is not evidence that the embedded
resource has the wrong assembly identity: independent metadata inspection shows
the resource is **exactly** tuple 4.0.5.0 with token `cc7b13ffcd2ddd51`, MVID
`c9563f77-d24c-4cc1-96e5-97a7362b6837`, size 16136 bytes, and SHA256
`7cd7ac314a72774b5e2255fd798f876926c63e19f9b8304cae25df012beabbcb`.

The embedded facade has eleven metadata forwarders to `mscorlib` 4.0.0.0:
`TupleElementNamesAttribute`, `TupleExtensions`, and `ValueTuple` plus generic
arities 1 through 8. Those forwarder records are verified statically; runtime
lookup through a successfully returned embedded tuple facade is not established
by this run. Do not relabel the complete harness as exit 0 or claim all nine
dependencies were naturally memory-loaded.

### Recheck Commands And Logs

The same `$root`, `$package`, `$managed`, and `$fixture` paths defined below were
used. The byte smoke's working directory was `$root\bytes-bin`.

```powershell
& "$root\bytes-bin\ByteClosureSmoke.exe" "$package\NGUInjector.dll" $fixture "$root\assign-target-paths-bytes-smoke.log"
& "$root\audit\bin\MetadataAudit.exe" $package $managed "$root\assign-target-paths-metadata-audit.log"
Get-Command mono -ErrorAction SilentlyContinue
```

Only these existing tools were executed; no production or temporary project was
rebuilt or edited in this recheck. New evidence is in
`assign-target-paths-bytes-smoke.log` and
`assign-target-paths-metadata-audit.log`; previous success/failure logs were
preserved. The sole repository edit is this note.

The prior missing-resource blocker is superseded for this MVID. The full harness
remains non-green on the explicit tuple identity probe under Windows CLR.
Standalone Mono is still unavailable, so Unity Mono/BCL execution remains
unverified. No game process interaction, native Mono invocation, injection, or
deployment occurred.

## Historical Byte-Load Failure

This section records the previous `634a8f4a-8341-4ac8-9cd2-f995c6839f57` capture
and its then-current failure. Its missing-resource conclusion is superseded by
the rebuilt artifact above; its original observations and logs are retained.

The follow-up capture contains the new resource-backed resolver and registration
calls, but **does not contain any of the nine required dependency resources**.
Both runtime manifest inspection and an independent Cecil metadata audit found
zero `NGUInjector.Dependencies.*` resources. The isolated byte-load smoke exits 1:
registration succeeds, but the real-fixture read cannot load
`System.Formats.Nrbf, Version=10.0.0.13`.

This is the current result, not a claim that the intended source/build-target
repair was successfully packaged. The earlier path-based pass and its artifact
identities below are preserved as historical evidence, not evidence for this
new capture.

### Current Artifact

- Source: `C:\Users\Kevin\AppData\Local\Temp\opencode\save-safety-build\bin\dist\injector\NGUInjector.dll`
- Assembly: `NGUInjector, Version=1.0.9792.22182, Culture=neutral, PublicKeyToken=null`
- MVID: `634a8f4a-8341-4ac8-9cd2-f995c6839f57`
- SHA256: `576fb3b515d594ae69dc7b59ddf3f30152b586f586c559e76ec0e72971aaa446`
- Temporary root: `C:\Users\Kevin\AppData\Local\Temp\opencode\nrbf-net48-closure-20261023`
- Isolated output AND working directory: `<temporary root>\bytes-bin`
- Fixture: the same read-only `24hour-ready-test.txt` identified below, with
  unchanged SHA256 `073c26b050b8d4c3a41187e8fa104ac7af9d26ecb4aeff71873b4bff37ce84a9`.

### Manifest And Registration

The only manifest resources in this capture are:

```text
NGUInjector.SettingsForm.resources
NGUInjector.ActivityPolicies.json
NGUInjector.PresetDefinitions.json
```

All nine expected resources are missing, not just the tuple facade:

```text
NGUInjector.Dependencies.System.Formats.Nrbf.dll
NGUInjector.Dependencies.Microsoft.Bcl.HashCode.dll
NGUInjector.Dependencies.System.Reflection.Metadata.dll
NGUInjector.Dependencies.System.Collections.Immutable.dll
NGUInjector.Dependencies.System.Memory.dll
NGUInjector.Dependencies.System.Buffers.dll
NGUInjector.Dependencies.System.Runtime.CompilerServices.Unsafe.dll
NGUInjector.Dependencies.System.Numerics.Vectors.dll
NGUInjector.Dependencies.System.ValueTuple.dll
```

The source now declares `EmbedSaveSafetyDependencies` and the explicit
`System.ValueTuple/4.6.2` `lib/net47` facade. Independently, the **captured DLL's
IL** confirms `Loader.Init` starts with `SaveSafetyDependencies.Register`,
`SaveSafetyCatalog`'s static constructor calls Register, and Register subscribes
the new Resolve method. Resolve uses manifest resource bytes, requester scoping,
and an exact FullName comparison. Thus the earlier observation that the plugin
has no resolver is superseded for this artifact. The resource inventory does
not support the separate assertion that the embedding target's intended output
made it into this capture. The reason for the missing resources was not diagnosed
by rebuilding or changing production.

### Isolated Execution

`ByteClosureSmoke.csproj` targets net48/x64. Runtime reference inspection confirms
its only references are `mscorlib`, `System`, and `System.Core`. Its separate
`bytes-bin` directory contains only `ByteClosureSmoke.exe` and its startup config;
no package DLL, plugin DLL, Cecil/helper DLL, or binding redirect is present.
The application base and process working directory both point to that clean
directory. The harness's AssemblyResolve observer only logs and returns null;
it never supplies a library or probes disk.

| Check | Observed Outcome |
| --- | --- |
| `Assembly.Load(pluginBytes)` | Passed; plugin `Location` is empty and `GAC=False` |
| Nine embedded dependency resources | Failed: 0/9 present |
| Explicit reflection call to `SaveSafetyDependencies.Register` | Returned successfully |
| Direct resolver call for exact `System.ValueTuple, Version=4.0.5.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51`, with plugin as requester | Returned null |
| `SaveSafetyCatalog.Read(realFixture, 1260)` | Failed with `TargetInvocationException` wrapping `FileNotFoundException` for `System.Formats.Nrbf, Version=10.0.0.13, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51` |
| Eight codec dependencies loaded from memory | Not reached: none loaded |
| Tuple GAC unification / embedded facade identity / visible forwarders | Not reached: no tuple assembly loaded or embedded facade available |
| New fixture total equals `2283976.2493571` | Not reached; do not attribute the historical successful parse to this artifact |
| Game/Unity assemblies loaded in smoke process | None |
| Captured plugin bytes after failed read | Unchanged |
| Fixture bytes and last-write timestamp after failed read | Unchanged |

Host runtime remains the Windows CLR, `4.0.30319.42000`, with host `mscorlib`
file version `4.8.9363.0`; `Mono=False`. Static inspection of game DLLs is confined
to the separate metadata-audit process, which does not execute those DLLs.

### Follow-Up Commands

Commands below use the same `$root`, `$package`, `$managed`, `$fixture`, and
`$msbuild` values documented in the historical command block. The byte smoke was
run with working directory `$root\bytes-bin`; all paths here are normalized to
absolute form.

```powershell
& $msbuild "$root\ByteClosureSmoke.csproj" /t:Build /p:Configuration=Release /m:1 /nologo /verbosity:minimal /fileLogger "/fileLoggerParameters:LogFile=$root\bytes-build.log;Verbosity=normal"
& "$root\bytes-bin\ByteClosureSmoke.exe" "$package\NGUInjector.dll" $fixture "$root\bytes-smoke.log"

& $msbuild "$root\audit\MetadataAudit.csproj" /t:Build /m:1 /nologo /verbosity:minimal /fileLogger "/fileLoggerParameters:LogFile=$root\embedded-audit-build.log;Verbosity=normal"
& "$root\audit\bin\MetadataAudit.exe" $package $managed "$root\embedded-metadata-audit.log"

Get-Command mono -ErrorAction SilentlyContinue
```

- Final temporary harness build: exit 0, zero warnings/errors.
- Final temporary metadata-audit build: exit 0, zero warnings/errors.
- Byte-load smoke: exit 1 with the missing-NRBF exception above, logged explicitly.
- Metadata audit: exit 0; this means metadata inspection completed, not closure
  acceptance. It independently reports `EMBEDDED_ASSEMBLY_COUNT 0` and the three
  actual resource names, plus the repaired registration/resolver IL.
- Initial harness compile encountered CS1061 because net48 does not expose
  `Assembly.GetForwardedTypes`. The harness was corrected to use facade
  `GetType` lookups and metadata-table inspection, with no helper references.
  Those forwarder checks remain unexecuted because this capture lacks resources.
- Standalone `mono`: still no command found; no Mono execution was attempted.
- New logs: `bytes-build.log`, `bytes-smoke.log`, `embedded-audit-build.log`, and
  `embedded-metadata-audit.log`. Earlier `natural.log` and `metadata-audit.log`
  were not overwritten.

No production source/project was edited or rebuilt in this follow-up. Only the
temporary tools and this note changed. No live game interaction, Loader/Unity
method invocation, deployment, injection, or native Mono call occurred. The
immediate blocker is missing embedded resources in the captured DLL. Even after
that blocker is repaired and the byte-load CLR test passes, standalone/Unity
Mono execution remains a separate unverified gap.

## Historical Verdict

The remaining sections describe the earlier MVID
`5085f8f7-f3bf-46df-840a-a435e5d09f0e` capture and are retained unchanged as
historical evidence. Statements about no embedded resolver or framework-provided
tuple closure in those sections are not conclusions about the follow-up source
repair or a successful new byte-load test.

The actual captured net48 plugin successfully parsed the real save fixture under
the installed Windows CLR. All eight packaged codec dependencies loaded from the
captured injector directory, without custom resolver assistance or application
binding redirects. This is **not Unity Mono or SMI deployment acceptance**.

Two unresolved deployment issues matter:

1. NRBF references `System.ValueTuple, Version=4.0.5.0`. No
   `System.ValueTuple.dll` is packaged or present in the installed game tree. The
   CLR smoke passed using the Windows GAC's `System.ValueTuple` 4.0.0.0. Game
   `mscorlib.dll` defines tuple types, but that does not establish resolution of
   the separate assembly identity under Unity Mono.
2. The bundled SMI does not load the plugin using `mono_assembly_open(path)`.
   Static IL shows `File.ReadAllBytes(-a)` followed by
   `mono_image_open_from_data` and `mono_assembly_load_from_full` with an empty
   filename. CLR `Assembly.LoadFrom` sibling-directory probing cannot be assumed
   for this byte/image loading path. The captured plugin has no direct
   `add_AssemblyResolve`, `LoadFrom`, or `LoadFile` calls in its IL.

No runtime Mono failure was reproduced or claimed. A file-based
`mono_assembly_open` loader would be a different loading path, not the bundled
SMI's observed path. Neither the absent tuple facade nor sibling probing was
repaired in this verification task.

## Tested Artifacts

- Package: `C:\Users\Kevin\AppData\Local\Temp\opencode\save-safety-build\bin\dist\injector`
- Plugin MVID: `5085f8f7-f3bf-46df-840a-a435e5d09f0e`
- Plugin SHA256: `4eef8acc511ef7dbd86055f6dfd69cd1cf8dd5c0850262c612bc3fa14112da74`
- Plugin target: `.NETFramework,Version=v4.8`
- Fixture, read-only: `C:\Users\Kevin\AppData\LocalLow\NGU Industries\NGU Idle\24hour-ready-test.txt`
- Fixture SHA256: `073c26b050b8d4c3a41187e8fa104ac7af9d26ecb4aeff71873b4bff37ce84a9`
- Managed directory, read-only: `C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE\NGUIdle_Data\Managed`
- Temporary projects/logs: `C:\Users\Kevin\AppData\Local\Temp\opencode\nrbf-net48-closure-20261023`

## Runtime Results

The isolated x64 console targets net48 and references only Framework `System` and
`System.Core`. It has no package references, production project references,
source-linked production code, or copied codec DLLs in its output directory.
The output directory contains only `ClosureSmoke.exe` and its startup config.

Execution initially calls only `Assembly.LoadFrom` on the captured
`NGUInjector.dll`. It reflects `NGUInjector.SaveSafetyCatalog.Read(string, int)`
with maximum version 1260 and reads returned metadata properties. It never calls
`Loader.Init`, injector `Main`, game startup, native codecs, or Unity methods.
Its resolver permits game/Unity dependencies from Managed if needed, but denies
package-library fallback in the tested `natural` mode. No resolver event occurred;
no game or Unity assembly loaded in this smoke process.

Host: Windows CLR `v4.0.30319`, `Environment.Version=4.0.30319.42000`, x64,
`Mono=False`. Host `mscorlib` file version is `4.8.9363.0`, build label
`NET481REL1LAST_25H2_B`; this is a net48-targeted test on the installed 4.8.1-family
runtime, not execution under the game's BCL.

| Parsed Field | Observed Value |
| --- | --- |
| Total playtime seconds | `2283976.2493571`, exact double equality and round-trip text |
| Rebirth seconds | `67661.7487057205` |
| Rebirth number | `230` |
| Version | `1260` |
| LastTime | `1792766209` |

The production SHA256 result matched an independent hash of the fixture. Fixture
bytes and last-write timestamp were unchanged after parsing. Every packaged DLL's
bytes were hash-checked before and after and remained unchanged.

## Actual Resolution Paths

Each library below loaded from the package directory above, not NuGet, the
harness output, GAC, or game Managed. Versions are assembly versions.

| Library | Version |
| --- | --- |
| `System.Formats.Nrbf.dll` | `10.0.0.13` |
| `Microsoft.Bcl.HashCode.dll` | `6.0.0.0` |
| `System.Reflection.Metadata.dll` | `10.0.0.13` |
| `System.Collections.Immutable.dll` | `10.0.0.13` |
| `System.Memory.dll` | `4.0.5.0` |
| `System.Buffers.dll` | `4.0.5.0` |
| `System.Runtime.CompilerServices.Unsafe.dll` | `6.0.3.0` |
| `System.Numerics.Vectors.dll` | `4.1.6.0` |

`Microsoft.Bcl.HashCode` was actually exercised and is not missing from this
capture. The important extra framework dependency resolved to:

`C:\WINDOWS\Microsoft.Net\assembly\GAC_MSIL\System.ValueTuple\v4.0_4.0.0.0__cc7b13ffcd2ddd51\System.ValueTuple.dll`

Framework `mscorlib` loaded from
`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\mscorlib.dll`;
`System.dll` and `System.Core.dll` loaded from Windows GAC_MSIL. Complete full
paths, identities, package hashes, and reference edges are in `natural.log`.

The captured production NuGet assets select `System.ValueTuple/4.6.2` as
`lib/net471/_._` for net48, treating tuples as framework-supplied rather than
copying a runtime DLL. The metadata audit confirms NRBF's ``System.ValueTuple`2``
type reference is scoped to the separate `System.ValueTuple` assembly. The game
BCL defining tuple types is not, by itself, a facade/assembly-binding proof.

## Commands And Logs

The following are the executed commands with paths normalized to absolute form.
Only the two temporary projects were built, with no restore or network package
source. Neither the production project nor solution was built.

```powershell
$root = 'C:\Users\Kevin\AppData\Local\Temp\opencode\nrbf-net48-closure-20261023'
$package = 'C:\Users\Kevin\AppData\Local\Temp\opencode\save-safety-build\bin\dist\injector'
$managed = 'C:\Gaming\Launchers\Steam\Steam\steamapps\common\NGU IDLE\NGUIdle_Data\Managed'
$fixture = 'C:\Users\Kevin\AppData\LocalLow\NGU Industries\NGU Idle\24hour-ready-test.txt'
$msbuild = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe'

& $msbuild "$root\ClosureSmoke.csproj" /t:Build /p:Configuration=Release /m:1 /nologo /verbosity:minimal /fileLogger "/fileLoggerParameters:LogFile=$root\build.log;Verbosity=normal"
& "$root\bin\ClosureSmoke.exe" $package $managed $fixture "$root\natural.log" natural

& $msbuild "$root\audit\MetadataAudit.csproj" /t:Build /m:1 /nologo /verbosity:minimal /fileLogger "/fileLoggerParameters:LogFile=$root\audit-build.log;Verbosity=normal"
& "$root\audit\bin\MetadataAudit.exe" $package $managed "$root\metadata-audit.log"

Get-Command mono -ErrorAction SilentlyContinue
```

- MSBuild version: `17.13.19.12613`.
- Both temporary builds: exit 0, zero warnings, zero errors.
- Real-fixture reflection smoke: exit 0, exact metadata assertions passed.
- Cecil metadata audit: exit 0. This reads package DLLs, bundled SMI, and game
  Managed DLLs without reflection-loading or executing any inspected target.
- Standalone `mono`: no command found, so no Mono execution was attempted.
- Logs: `build.log`, `natural.log`, `audit-build.log`, `metadata-audit.log` in
  the temporary root. Audit findings include SMI method IL, missing tuple
  assembly status, NRBF tuple reference scope, and game tuple definitions.

## Safety And Remaining Gap

No game process was started, stopped, contacted, or injected into. No SMI/native
Mono export was invoked. No deployment, save rewrite, production build, package
restore, or production-code edit occurred. All executable/source/log artifacts
were created in the approved temp directory; this verification note is the only
repository file added by this task. Existing worktree/index changes were left
untouched.

This proves the captured codec's real-fixture path and packaged library closure
under Windows CLR with its framework-provided tuple assembly. It does not prove
Unity Mono's API/BCL compatibility, tuple facade binding, or dependency discovery
through the actual bundled SMI byte-loading path. Those remain deployment gates.
