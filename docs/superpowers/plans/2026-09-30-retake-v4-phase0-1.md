# RetakeV4 — Phases 0 & 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Lever le risque technique du HUD (phase 0, prototype jetable) puis poser les fondations du plugin RetakeV4 : solution, Domain testé, bus d'événements, système de modules, configs JSON, localisation et module Core qui suit le cycle des rounds (phase 1).

**Architecture:** Hexagonale. `RetakeV4.Domain` contient toute la logique pure (géométrie HUD, machine à états du round, watchdog warmup, bus, planification des modules, garde d'erreurs, pipeline de préparation) et ne référence pas CounterStrikeSharp. `RetakeV4` (plugin CSSharp) contient uniquement des adaptateurs fins : bootstrap, hôte de modules, config JSON, localisation, module Core. Le prototype `spikes/HudProbe` est jetable et vit hors de la solution.

**Tech Stack:** C# / .NET 10, CounterStrikeSharp.API 1.0.370, System.Text.Json, xUnit 2.9.3, coverlet.msbuild 6.0.4.

**Spec:** `docs/superpowers/specs/2026-09-30-retake-v4-design.md`

## Global Constraints

- CounterStrikeSharp.API **1.0.370**, `[MinimumApiVersion(370)]`, TargetFramework `net10.0`.
- `Nullable` enable, `TreatWarningsAsErrors` true, `ImplicitUsings` enable (via `Directory.Build.props`).
- `RetakeV4.Domain` ne référence **jamais** CounterStrikeSharp.
- Fonctions < 50 lignes, fichiers < 800 lignes, imbrication ≤ 4 niveaux, objets de domaine immuables (records) sauf trackers runtime explicitement nommés.
- Couverture de `RetakeV4.Domain` ≥ **80 %** (lignes).
- Textes joueurs uniquement via `lang/*.json` (clés `module.section.key`), logs serveur en anglais.
- Aucun contenu joueur ne sert de template `ILogger` : toujours des templates constants avec arguments nommés.
- Commits conventionnels (`feat:`, `test:`, `chore:`, `docs:`), jamais `--no-verify`.
- Shell : Git Bash sous Windows ; les commandes `dotnet` sont lancées depuis la racine `CS2RetakeV4/`.

## Review Focus

1. **Hot reload en plein round live** : le plugin rechargé ne doit pas croire qu'on est en warmup (état initial `PostRound` si les gamerules disent « pas warmup »). Test : `RoundTrackerTests.InitialStateFor_NotWarmup_IsPostRound` (Task 8).
2. **Config éditée à la main avec une faute** (enum inconnue, virgule finale, JSON tronqué) : valeurs par défaut + avertissement, et le fichier n'est **jamais** écrasé. Tests : `JsonConfigStoreTests` (Task 9).
3. **`round_start` sans `round_end` préalable** (`mp_restartgame`, admin qui force) : nouveau round `Preparing` avec numéro +1, pipeline exécuté une seule fois. Test : `RoundTrackerTests.RoundStartedDuringLive_StartsNewPreparation` (Task 8).
4. **Mode compétitif (`WarmupPeriodEnd = 0`) et changement de map** : le secours se déclenche une seule fois par map, et est réarmé à chaque map. Tests : `WarmupTrackerTests` (Task 6).
5. **Chemin `ExecConfig` malveillant ou erroné dans `core.json`** (`../../server.cfg; quit`) : refusé, valeur par défaut utilisée. Test : `CoreConfigValidatorTests` (Task 12).

---

## File Structure

```
CS2RetakeV4/
├─ global.json                                   SDK pin
├─ Directory.Build.props                         réglages communs (net10, nullable, warnings)
├─ RetakeV4.sln
├─ CLAUDE.md                                     guide du dépôt (Task 13)
├─ scripts/package-dev.ps1                       package de test serveur (Task 13)
├─ docs/CHECKLIST-INGAME.md                      checklist manuelle (Task 13)
├─ docs/spikes/hud-probe-findings.md             résultats du prototype (Task 4)
├─ spikes/HudProbe/                              prototype JETABLE (Task 4), hors solution
│  ├─ HudProbe.csproj
│  └─ HudProbePlugin.cs
├─ src/RetakeV4.Domain/
│  ├─ RetakeV4.Domain.csproj
│  ├─ DomainInfo.cs
│  ├─ Geometry/Vec3.cs, ViewAngles.cs, AngleMath.cs, ViewGeometry.cs          (Task 2)
│  ├─ Hud/AimMenuLayout.cs, AimMenuGeometry.cs, AimLineResolver.cs           (Task 3)
│  ├─ Rounds/RoundPhase.cs, RoundSignal.cs, RoundState.cs, RoundStateMachine.cs (Task 5)
│  ├─ Rounds/WarmupTracker.cs                                                (Task 6)
│  ├─ Events/IEventBus.cs, EventBus.cs, BusError.cs                          (Task 7)
│  ├─ Events/RoundEvents.cs                                                  (Task 8)
│  ├─ Modules/ModuleDescriptor.cs, ModuleLoadPlanner.cs, ErrorBudget.cs, ModuleGuard.cs (Task 7)
│  └─ Rounds/PreparationContext.cs, IPreparationStep.cs, PreparationPipeline.cs, RoundTracker.cs (Task 8)
├─ src/RetakeV4/
│  ├─ RetakeV4.csproj
│  ├─ RetakeV4Plugin.cs                                                      (Task 11)
│  ├─ Configuration/ModuleConfig.cs, ConfigIssue.cs, IConfigValidator.cs, JsonConfigStore.cs, ConfigLogging.cs (Task 9)
│  ├─ Localization/ITextService.cs, TextService.cs                           (Task 10)
│  ├─ lang/en.json, lang/fr.json                                             (Task 10)
│  ├─ Modules/IRetakeModule.cs, ModuleContext.cs, ModuleHost.cs              (Task 11)
│  ├─ Modules/Core/CoreConfig.cs, CoreConfigValidator.cs, GameRulesAccessor.cs, CoreModule.cs (Task 12)
│  └─ cfg/RetakeV4/retake.cfg                                                (Task 12)
└─ tests/
   ├─ RetakeV4.Domain.Tests/        (Tasks 1-3, 5-8)
   └─ RetakeV4.Integration.Tests/   (Tasks 9-12)
```

---

# PHASE 0 — Prototype HUD

### Task 1: Scaffold de la solution et du projet Domain

**Files:**
- Create: `global.json`, `Directory.Build.props`, `RetakeV4.sln`
- Create: `src/RetakeV4.Domain/RetakeV4.Domain.csproj`, `src/RetakeV4.Domain/DomainInfo.cs`
- Create: `tests/RetakeV4.Domain.Tests/RetakeV4.Domain.Tests.csproj`, `tests/RetakeV4.Domain.Tests/DomainInfoTests.cs`

**Interfaces:**
- Produces: projet `RetakeV4.Domain` (namespace racine `RetakeV4.Domain`), projet de tests `RetakeV4.Domain.Tests` avec `using Xunit` global.

- [ ] **Step 1: Créer les fichiers de build communs**

`global.json`
```json
{
  "sdk": {
    "version": "10.0.300",
    "rollForward": "latestFeature"
  }
}
```

`Directory.Build.props`
```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Deterministic>true</Deterministic>
  </PropertyGroup>
</Project>
```

`src/RetakeV4.Domain/RetakeV4.Domain.csproj`
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>RetakeV4.Domain</RootNamespace>
  </PropertyGroup>
</Project>
```

`tests/RetakeV4.Domain.Tests/RetakeV4.Domain.Tests.csproj`
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="coverlet.msbuild" Version="6.0.4" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\RetakeV4.Domain\RetakeV4.Domain.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Écrire le test qui échoue**

`tests/RetakeV4.Domain.Tests/DomainInfoTests.cs`
```csharp
using RetakeV4.Domain;

namespace RetakeV4.Domain.Tests;

public class DomainInfoTests
{
    [Fact]
    public void AssemblyName_MatchesDeclaredName()
    {
        Assert.Equal(DomainInfo.AssemblyName, typeof(DomainInfo).Assembly.GetName().Name);
    }
}
```

- [ ] **Step 3: Créer la solution et vérifier l'échec**

```bash
dotnet new sln -n RetakeV4 --format sln
dotnet sln RetakeV4.sln add src/RetakeV4.Domain/RetakeV4.Domain.csproj tests/RetakeV4.Domain.Tests/RetakeV4.Domain.Tests.csproj
dotnet test tests/RetakeV4.Domain.Tests --nologo
```
Expected: échec de compilation `CS0103: The name 'DomainInfo' does not exist`.

- [ ] **Step 4: Implémentation minimale**

`src/RetakeV4.Domain/DomainInfo.cs`
```csharp
namespace RetakeV4.Domain;

public static class DomainInfo
{
    public const string AssemblyName = "RetakeV4.Domain";
}
```

- [ ] **Step 5: Vérifier le succès**

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: `Passed! - Failed: 0, Passed: 1`

- [ ] **Step 6: Commit**

```bash
git add global.json Directory.Build.props RetakeV4.sln src/RetakeV4.Domain tests/RetakeV4.Domain.Tests
git commit -m "chore: scaffold solution RetakeV4 et projet Domain"
```

---

### Task 2: Géométrie de vue (Domain)

**Files:**
- Create: `src/RetakeV4.Domain/Geometry/Vec3.cs`, `ViewAngles.cs`, `AngleMath.cs`, `ViewGeometry.cs`
- Test: `tests/RetakeV4.Domain.Tests/Geometry/ViewGeometryTests.cs`, `tests/RetakeV4.Domain.Tests/Geometry/AngleMathTests.cs`

**Interfaces:**
- Produces:
  - `readonly record struct Vec3(float X, float Y, float Z)` avec `+`, `-`, `* float`, `float Length`, `static float Dot(Vec3, Vec3)`
  - `readonly record struct ViewAngles(float Pitch, float Yaw, float Roll = 0f)`
  - `static class AngleMath { float NormalizeDegrees(float); float ToRadians(float); float ToDegrees(float); }`
  - `static class ViewGeometry { Vec3 Forward(ViewAngles); Vec3 Right(ViewAngles); Vec3 Up(ViewAngles); Vec3 Offset(Vec3 origin, ViewAngles angles, float forward, float right, float up); ViewAngles AnglesOf(Vec3 direction); }`
- Convention Source : pitch positif = regarder vers le bas ; yaw 0 = +X, yaw 90 = +Y ; le roll est ignoré.

- [ ] **Step 1: Écrire les tests qui échouent**

`tests/RetakeV4.Domain.Tests/Geometry/AngleMathTests.cs`
```csharp
using RetakeV4.Domain.Geometry;

namespace RetakeV4.Domain.Tests.Geometry;

public class AngleMathTests
{
    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(190f, -170f)]
    [InlineData(-190f, 170f)]
    [InlineData(180f, 180f)]
    [InlineData(-180f, 180f)]
    [InlineData(540f, 180f)]
    [InlineData(725f, 5f)]
    public void NormalizeDegrees_WrapsIntoMinus180Exclusive180Inclusive(float input, float expected)
    {
        Assert.Equal(expected, AngleMath.NormalizeDegrees(input), 3);
    }

    [Fact]
    public void RadiansRoundTrip()
    {
        Assert.Equal(37.5f, AngleMath.ToDegrees(AngleMath.ToRadians(37.5f)), 3);
    }
}
```

`tests/RetakeV4.Domain.Tests/Geometry/ViewGeometryTests.cs`
```csharp
using RetakeV4.Domain.Geometry;

namespace RetakeV4.Domain.Tests.Geometry;

public class ViewGeometryTests
{
    private static void AssertVec(Vec3 expected, Vec3 actual)
    {
        Assert.Equal(expected.X, actual.X, 3);
        Assert.Equal(expected.Y, actual.Y, 3);
        Assert.Equal(expected.Z, actual.Z, 3);
    }

    [Fact]
    public void Forward_Yaw0_PointsAlongPositiveX() =>
        AssertVec(new Vec3(1, 0, 0), ViewGeometry.Forward(new ViewAngles(0, 0)));

    [Fact]
    public void Forward_Yaw90_PointsAlongPositiveY() =>
        AssertVec(new Vec3(0, 1, 0), ViewGeometry.Forward(new ViewAngles(0, 90)));

    [Fact]
    public void Forward_PositivePitch_LooksDown() =>
        AssertVec(new Vec3(0, 0, -1), ViewGeometry.Forward(new ViewAngles(90, 0)));

    [Fact]
    public void Forward_NegativePitch_LooksUp() =>
        AssertVec(new Vec3(0, 0, 1), ViewGeometry.Forward(new ViewAngles(-90, 0)));

    [Fact]
    public void Right_Yaw0_PointsAlongNegativeY() =>
        AssertVec(new Vec3(0, -1, 0), ViewGeometry.Right(new ViewAngles(0, 0)));

    [Fact]
    public void Up_Pitch0_PointsAlongPositiveZ() =>
        AssertVec(new Vec3(0, 0, 1), ViewGeometry.Up(new ViewAngles(0, 45)));

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(30f, 45f)]
    [InlineData(-60f, 170f)]
    [InlineData(85f, -120f)]
    public void Basis_IsOrthonormal(float pitch, float yaw)
    {
        var angles = new ViewAngles(pitch, yaw);
        var f = ViewGeometry.Forward(angles);
        var r = ViewGeometry.Right(angles);
        var u = ViewGeometry.Up(angles);
        Assert.Equal(0f, Vec3.Dot(f, r), 3);
        Assert.Equal(0f, Vec3.Dot(f, u), 3);
        Assert.Equal(0f, Vec3.Dot(r, u), 3);
        Assert.Equal(1f, f.Length, 3);
        Assert.Equal(1f, u.Length, 3);
    }

    [Fact]
    public void Offset_CombinesForwardRightUp()
    {
        var result = ViewGeometry.Offset(new Vec3(10, 20, 30), new ViewAngles(0, 0), forward: 5, right: 2, up: 3);
        AssertVec(new Vec3(15, 18, 33), result);
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(25f, 60f)]
    [InlineData(-40f, -150f)]
    public void AnglesOf_InvertsForward(float pitch, float yaw)
    {
        var angles = ViewGeometry.AnglesOf(ViewGeometry.Forward(new ViewAngles(pitch, yaw)) * 42f);
        Assert.Equal(pitch, angles.Pitch, 2);
        Assert.Equal(yaw, angles.Yaw, 2);
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: échec de compilation (types `Vec3`, `ViewAngles`, `AngleMath`, `ViewGeometry` introuvables).

- [ ] **Step 3: Implémentation**

`src/RetakeV4.Domain/Geometry/Vec3.cs`
```csharp
namespace RetakeV4.Domain.Geometry;

public readonly record struct Vec3(float X, float Y, float Z)
{
    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator *(Vec3 v, float scale) => new(v.X * scale, v.Y * scale, v.Z * scale);

    public float Length => MathF.Sqrt(Dot(this, this));

    public static float Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
}
```

`src/RetakeV4.Domain/Geometry/ViewAngles.cs`
```csharp
namespace RetakeV4.Domain.Geometry;

public readonly record struct ViewAngles(float Pitch, float Yaw, float Roll = 0f);
```

`src/RetakeV4.Domain/Geometry/AngleMath.cs`
```csharp
namespace RetakeV4.Domain.Geometry;

public static class AngleMath
{
    public static float NormalizeDegrees(float degrees)
    {
        var wrapped = degrees % 360f;
        if (wrapped > 180f)
        {
            wrapped -= 360f;
        }
        if (wrapped <= -180f)
        {
            wrapped += 360f;
        }
        return wrapped;
    }

    public static float ToRadians(float degrees) => degrees * MathF.PI / 180f;

    public static float ToDegrees(float radians) => radians * 180f / MathF.PI;
}
```

`src/RetakeV4.Domain/Geometry/ViewGeometry.cs`
```csharp
namespace RetakeV4.Domain.Geometry;

// Source engine convention: positive pitch looks down, yaw 0 = +X, yaw 90 = +Y. Roll is ignored.
public static class ViewGeometry
{
    public static Vec3 Forward(ViewAngles angles)
    {
        var (sp, cp, sy, cy) = Trig(angles);
        return new Vec3(cp * cy, cp * sy, -sp);
    }

    public static Vec3 Right(ViewAngles angles)
    {
        var (_, _, sy, cy) = Trig(angles);
        return new Vec3(sy, -cy, 0f);
    }

    public static Vec3 Up(ViewAngles angles)
    {
        var (sp, cp, sy, cy) = Trig(angles);
        return new Vec3(sp * cy, sp * sy, cp);
    }

    public static Vec3 Offset(Vec3 origin, ViewAngles angles, float forward, float right, float up) =>
        origin + Forward(angles) * forward + Right(angles) * right + Up(angles) * up;

    public static ViewAngles AnglesOf(Vec3 direction)
    {
        var length = direction.Length;
        if (length <= float.Epsilon)
        {
            return new ViewAngles(0f, 0f);
        }
        var pitch = -AngleMath.ToDegrees(MathF.Asin(direction.Z / length));
        var yaw = AngleMath.ToDegrees(MathF.Atan2(direction.Y, direction.X));
        return new ViewAngles(pitch, yaw);
    }

    private static (float Sp, float Cp, float Sy, float Cy) Trig(ViewAngles angles)
    {
        var p = AngleMath.ToRadians(angles.Pitch);
        var y = AngleMath.ToRadians(angles.Yaw);
        return (MathF.Sin(p), MathF.Cos(p), MathF.Sin(y), MathF.Cos(y));
    }
}
```

- [ ] **Step 4: Vérifier le succès**

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: `Failed: 0`, tous les tests de `AngleMathTests` et `ViewGeometryTests` passent.

- [ ] **Step 5: Commit**

```bash
git add src/RetakeV4.Domain/Geometry tests/RetakeV4.Domain.Tests/Geometry
git commit -m "feat: géométrie de vue Source (Domain)"
```

---

### Task 3: Résolution de la ligne visée (Domain)

**Files:**
- Create: `src/RetakeV4.Domain/Hud/AimMenuLayout.cs`, `AimMenuGeometry.cs`, `AimLineResolver.cs`
- Test: `tests/RetakeV4.Domain.Tests/Hud/AimLineResolverTests.cs`

**Interfaces:**
- Consumes: `Vec3`, `ViewAngles`, `AngleMath`, `ViewGeometry` (Task 2).
- Produces:
  - `sealed record AimMenuLayout(int LineCount, float DistanceUnits, float LineHeightUnits, float FirstLineUpUnits, float HalfWidthUnits)` — le constructeur lève `ArgumentOutOfRangeException` si `LineCount < 1` ou si une distance/hauteur/largeur ≤ 0.
  - `static class AimMenuGeometry { Vec3 LinePosition(AimMenuLayout layout, Vec3 eye, ViewAngles opened, int lineIndex); }`
  - `static class AimLineResolver { int? Resolve(AimMenuLayout layout, ViewAngles opened, ViewAngles current); }` — `null` = le joueur ne vise aucune ligne.
- Modèle : le menu est **ancré sur l'orientation d'ouverture** (`opened`) ; la ligne `i` est à `FirstLineUpUnits - i * LineHeightUnits` unités au-dessus du point situé à `DistanceUnits` devant l'œil.

- [ ] **Step 1: Écrire les tests qui échouent**

`tests/RetakeV4.Domain.Tests/Hud/AimLineResolverTests.cs`
```csharp
using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.Hud;

namespace RetakeV4.Domain.Tests.Hud;

public class AimLineResolverTests
{
    private static readonly AimMenuLayout Layout = new(
        LineCount: 6, DistanceUnits: 50f, LineHeightUnits: 4f, FirstLineUpUnits: 10f, HalfWidthUnits: 15f);

    private static float PitchForUp(float up) => -AngleMath.ToDegrees(MathF.Atan(up / Layout.DistanceUnits));

    private static float YawForSideways(float units) => AngleMath.ToDegrees(MathF.Atan(units / Layout.DistanceUnits));

    [Fact]
    public void AimingAtFirstLine_ReturnsZero()
    {
        var current = new ViewAngles(PitchForUp(10f), 0f);
        Assert.Equal(0, AimLineResolver.Resolve(Layout, new ViewAngles(0f, 0f), current));
    }

    [Fact]
    public void AimingAtFourthLine_ReturnsThree()
    {
        var current = new ViewAngles(PitchForUp(10f - 3 * 4f), 0f);
        Assert.Equal(3, AimLineResolver.Resolve(Layout, new ViewAngles(0f, 0f), current));
    }

    [Theory]
    [InlineData(12f)]
    [InlineData(20f)]
    public void AimingAboveMenu_ReturnsNull(float up)
    {
        Assert.Null(AimLineResolver.Resolve(Layout, new ViewAngles(0f, 0f), new ViewAngles(PitchForUp(up), 0f)));
    }

    [Fact]
    public void AimingBelowLastLine_ReturnsNull()
    {
        var current = new ViewAngles(PitchForUp(10f - 6 * 4f), 0f);
        Assert.Null(AimLineResolver.Resolve(Layout, new ViewAngles(0f, 0f), current));
    }

    [Fact]
    public void AimingTooFarSideways_ReturnsNull()
    {
        var current = new ViewAngles(PitchForUp(10f), YawForSideways(20f));
        Assert.Null(AimLineResolver.Resolve(Layout, new ViewAngles(0f, 0f), current));
    }

    [Fact]
    public void AimingSlightlySideways_StillSelects()
    {
        var current = new ViewAngles(PitchForUp(10f), YawForSideways(10f));
        Assert.Equal(0, AimLineResolver.Resolve(Layout, new ViewAngles(0f, 0f), current));
    }

    [Fact]
    public void YawWrapAround_IsHandled()
    {
        var opened = new ViewAngles(0f, 179f);
        var current = new ViewAngles(PitchForUp(10f), -179f);
        Assert.Equal(0, AimLineResolver.Resolve(Layout, opened, current));
    }

    [Fact]
    public void ExtremePitchDelta_ReturnsNull()
    {
        Assert.Null(AimLineResolver.Resolve(Layout, new ViewAngles(0f, 0f), new ViewAngles(85f, 0f)));
    }

    [Theory]
    [InlineData(20f, 45f, 2)]
    [InlineData(-35f, -120f, 5)]
    [InlineData(0f, 90f, 0)]
    public void AimingAtRenderedLinePosition_SelectsThatLine(float openPitch, float openYaw, int line)
    {
        var eye = new Vec3(100f, -50f, 64f);
        var opened = new ViewAngles(openPitch, openYaw);
        var target = AimMenuGeometry.LinePosition(Layout, eye, opened, line);
        var current = ViewGeometry.AnglesOf(target - eye);
        Assert.Equal(line, AimLineResolver.Resolve(Layout, opened, current));
    }

    [Fact]
    public void LinePosition_StacksLinesDownward()
    {
        var eye = new Vec3(0f, 0f, 0f);
        var opened = new ViewAngles(0f, 0f);
        var line0 = AimMenuGeometry.LinePosition(Layout, eye, opened, 0);
        var line1 = AimMenuGeometry.LinePosition(Layout, eye, opened, 1);
        Assert.Equal(50f, line0.X, 3);
        Assert.Equal(10f, line0.Z, 3);
        Assert.Equal(6f, line1.Z, 3);
    }

    [Theory]
    [InlineData(0, 50f, 4f, 15f)]
    [InlineData(3, 0f, 4f, 15f)]
    [InlineData(3, 50f, -1f, 15f)]
    [InlineData(3, 50f, 4f, 0f)]
    public void Layout_RejectsInvalidValues(int lines, float distance, float lineHeight, float halfWidth)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AimMenuLayout(lines, distance, lineHeight, 0f, halfWidth));
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: échec de compilation (`AimMenuLayout`, `AimMenuGeometry`, `AimLineResolver` introuvables).

- [ ] **Step 3: Implémentation**

`src/RetakeV4.Domain/Hud/AimMenuLayout.cs`
```csharp
namespace RetakeV4.Domain.Hud;

public sealed record AimMenuLayout
{
    public AimMenuLayout(int LineCount, float DistanceUnits, float LineHeightUnits, float FirstLineUpUnits, float HalfWidthUnits)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(LineCount, 1);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(DistanceUnits);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(LineHeightUnits);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(HalfWidthUnits);
        this.LineCount = LineCount;
        this.DistanceUnits = DistanceUnits;
        this.LineHeightUnits = LineHeightUnits;
        this.FirstLineUpUnits = FirstLineUpUnits;
        this.HalfWidthUnits = HalfWidthUnits;
    }

    public int LineCount { get; }
    public float DistanceUnits { get; }
    public float LineHeightUnits { get; }
    public float FirstLineUpUnits { get; }
    public float HalfWidthUnits { get; }
}
```

`src/RetakeV4.Domain/Hud/AimMenuGeometry.cs`
```csharp
using RetakeV4.Domain.Geometry;

namespace RetakeV4.Domain.Hud;

public static class AimMenuGeometry
{
    public static Vec3 LinePosition(AimMenuLayout layout, Vec3 eye, ViewAngles opened, int lineIndex)
    {
        var up = layout.FirstLineUpUnits - lineIndex * layout.LineHeightUnits;
        return ViewGeometry.Offset(eye, opened, layout.DistanceUnits, 0f, up);
    }
}
```

`src/RetakeV4.Domain/Hud/AimLineResolver.cs`
```csharp
using RetakeV4.Domain.Geometry;

namespace RetakeV4.Domain.Hud;

public static class AimLineResolver
{
    private const float MaxAbsDeltaDegrees = 80f;

    public static int? Resolve(AimMenuLayout layout, ViewAngles opened, ViewAngles current)
    {
        var deltaPitch = AngleMath.NormalizeDegrees(current.Pitch - opened.Pitch);
        var deltaYaw = AngleMath.NormalizeDegrees(current.Yaw - opened.Yaw);
        if (MathF.Abs(deltaPitch) > MaxAbsDeltaDegrees || MathF.Abs(deltaYaw) > MaxAbsDeltaDegrees)
        {
            return null;
        }

        var sideways = MathF.Tan(AngleMath.ToRadians(deltaYaw)) * layout.DistanceUnits;
        if (MathF.Abs(sideways) > layout.HalfWidthUnits)
        {
            return null;
        }

        var aimedUp = -MathF.Tan(AngleMath.ToRadians(deltaPitch)) * layout.DistanceUnits;
        var raw = (layout.FirstLineUpUnits - aimedUp) / layout.LineHeightUnits;
        var index = (int)MathF.Round(raw, MidpointRounding.AwayFromZero);
        return index >= 0 && index < layout.LineCount ? index : null;
    }
}
```

- [ ] **Step 4: Vérifier le succès**

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: `Failed: 0`.

- [ ] **Step 5: Commit**

```bash
git add src/RetakeV4.Domain/Hud tests/RetakeV4.Domain.Tests/Hud
git commit -m "feat: résolution de la ligne de menu visée au viseur (Domain)"
```

---

### Task 4: Prototype jetable HudProbe + checkpoint humain

> **JETABLE.** Ce code n'est pas maintenu : il sert uniquement à répondre aux questions du fichier de résultats. Il sera supprimé au début de la phase 3. Il reste hors de `RetakeV4.sln`.

**Files:**
- Create: `spikes/HudProbe/HudProbe.csproj`, `spikes/HudProbe/HudProbePlugin.cs`
- Create: `docs/spikes/hud-probe-findings.md`

**Interfaces:**
- Consumes: `AimMenuLayout`, `AimMenuGeometry.LinePosition`, `AimLineResolver.Resolve`, `Vec3`, `ViewAngles` (Tasks 2-3).
- Produces: `docs/spikes/hud-probe-findings.md` rempli (décision GO/NO-GO viseur, formule d'orientation retenue, mode de positionnement retenu, valeurs de layout). Les phases suivantes s'appuient sur ce document.

- [ ] **Step 1: Projet du prototype**

`spikes/HudProbe/HudProbe.csproj`
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>HudProbe</RootNamespace>
    <GenerateRuntimeConfigurationFiles>true</GenerateRuntimeConfigurationFiles>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="CounterStrikeSharp.API" Version="1.0.370">
      <ExcludeAssets>runtime</ExcludeAssets>
    </PackageReference>
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\RetakeV4.Domain\RetakeV4.Domain.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Plugin du prototype**

`spikes/HudProbe/HudProbePlugin.cs`
```csharp
using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using RetakeV4.Domain.Geometry;
using RetakeV4.Domain.Hud;

namespace HudProbe;

[MinimumApiVersion(370)]
public sealed class HudProbePlugin : BasePlugin
{
    public override string ModuleName => "HudProbe (RetakeV4 spike - throwaway)";
    public override string ModuleVersion => "0.0.1";

    private static readonly string[] Labels = { "AK-47", "M4A1-S", "AWP : ON", "Deagle", "Grenades", "Fermer" };
    private static readonly Color Accent = Color.FromArgb(255, 79, 195, 247);

    private readonly Dictionary<int, ProbeMenu> _menus = new();
    private AimMenuLayout _layout = new(Labels.Length, 60f, 4f, 8f, 18f);
    private int _orientMode;
    private bool _parentMode;

    private sealed class ProbeMenu
    {
        public required CCSPlayerController Owner { get; init; }
        public required ViewAngles Opened { get; init; }
        public List<CPointWorldText> Lines { get; } = new();
        public int Cursor { get; set; }
        public int? Aimed { get; set; }
    }

    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnTick>(OnTick);
        RegisterListener<Listeners.CheckTransmit>(OnCheckTransmit);
        RegisterListener<Listeners.OnPlayerButtonsChanged>(OnButtons);
        for (var i = 1; i <= 9; i++)
        {
            var slot = i;
            AddCommandListener($"slot{slot}", (player, _) => OnSlot(player, slot), HookMode.Pre);
        }
    }

    [GameEventHandler]
    public HookResult OnRoundPrestart(EventRoundPrestart @event, GameEventInfo info)
    {
        foreach (var menu in _menus.Values.ToList())
        {
            Close(menu.Owner);
        }
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        if (@event.Userid is { } player)
        {
            Close(player);
        }
        return HookResult.Continue;
    }

    [ConsoleCommand("css_hudprobe", "Open/close the probe menu")]
    public void OnProbe(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null || !player.IsValid)
        {
            return;
        }
        if (_menus.ContainsKey(player.Slot))
        {
            Close(player);
            return;
        }
        Open(player);
    }

    [ConsoleCommand("css_hudprobe_orient", "0|1|2 : text facing formula")]
    public void OnOrient(CCSPlayerController? player, CommandInfo command)
    {
        _orientMode = int.TryParse(command.GetArg(1), out var mode) ? Math.Clamp(mode, 0, 2) : 0;
        command.ReplyToCommand($"[HudProbe] orient mode = {_orientMode}");
    }

    [ConsoleCommand("css_hudprobe_dist", "<units> : menu distance")]
    public void OnDistance(CCSPlayerController? player, CommandInfo command)
    {
        if (float.TryParse(command.GetArg(1), out var distance) && distance > 5f)
        {
            _layout = new AimMenuLayout(_layout.LineCount, distance, _layout.LineHeightUnits, _layout.FirstLineUpUnits, _layout.HalfWidthUnits);
        }
        command.ReplyToCommand($"[HudProbe] distance = {_layout.DistanceUnits}");
    }

    [ConsoleCommand("css_hudprobe_parent", "Toggle SetParent-to-pawn mode (applies to next open)")]
    public void OnParent(CCSPlayerController? player, CommandInfo command)
    {
        _parentMode = !_parentMode;
        command.ReplyToCommand($"[HudProbe] parent mode = {_parentMode}");
    }

    private void Open(CCSPlayerController player)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn is null || !pawn.IsValid)
        {
            return;
        }
        var menu = new ProbeMenu { Owner = player, Opened = new ViewAngles(pawn.EyeAngles.X, pawn.EyeAngles.Y) };
        foreach (var label in Labels)
        {
            var line = CreateLine(label);
            if (line is null)
            {
                DestroyLines(menu);
                player.PrintToChat(" [HudProbe] point_worldtext creation failed");
                return;
            }
            menu.Lines.Add(line);
        }
        _menus[player.Slot] = menu;
        Position(menu, pawn);
        if (_parentMode)
        {
            menu.Lines.ForEach(line => line.AcceptInput("SetParent", pawn, pawn, "!activator", 0));
        }
    }

    private static CPointWorldText? CreateLine(string text)
    {
        var line = Utilities.CreateEntityByName<CPointWorldText>("point_worldtext");
        if (line is null || !line.IsValid)
        {
            return null;
        }
        line.MessageText = text;
        line.Enabled = true;
        line.FontSize = 24f;
        line.WorldUnitsPerPx = 0.1f;
        line.Fullbright = true;
        line.Color = Color.White;
        line.JustifyHorizontal = PointWorldTextJustifyHorizontal_t.POINT_WORLD_TEXT_JUSTIFY_HORIZONTAL_CENTER;
        line.JustifyVertical = PointWorldTextJustifyVertical_t.POINT_WORLD_TEXT_JUSTIFY_VERTICAL_CENTER;
        line.ReorientMode = PointWorldTextReorientMode_t.POINT_WORLD_TEXT_REORIENT_NONE;
        line.DispatchSpawn();
        return line;
    }

    private void OnTick()
    {
        foreach (var menu in _menus.Values.ToList())
        {
            var pawn = menu.Owner.IsValid ? menu.Owner.PlayerPawn.Value : null;
            if (pawn is null || !pawn.IsValid)
            {
                Close(menu.Owner);
                continue;
            }
            if (!_parentMode)
            {
                Position(menu, pawn);
            }
            UpdateSelection(menu, pawn);
        }
    }

    private void Position(ProbeMenu menu, CCSPlayerPawn pawn)
    {
        if (pawn.AbsOrigin is null)
        {
            return;
        }
        var eye = new Vec3(pawn.AbsOrigin.X, pawn.AbsOrigin.Y, pawn.AbsOrigin.Z + pawn.ViewOffset.Z);
        var facing = FacingAngles(menu.Opened);
        for (var i = 0; i < menu.Lines.Count; i++)
        {
            var p = AimMenuGeometry.LinePosition(_layout, eye, menu.Opened, i);
            menu.Lines[i].Teleport(new Vector(p.X, p.Y, p.Z), facing, new Vector(0f, 0f, 0f));
        }
    }

    private QAngle FacingAngles(ViewAngles opened) => _orientMode switch
    {
        1 => new QAngle(0f, opened.Yaw - 90f, 90f),
        2 => new QAngle(opened.Pitch, opened.Yaw + 180f, 0f),
        _ => new QAngle(0f, opened.Yaw + 270f, 90f - opened.Pitch),
    };

    private void UpdateSelection(ProbeMenu menu, CCSPlayerPawn pawn)
    {
        var current = new ViewAngles(pawn.EyeAngles.X, pawn.EyeAngles.Y);
        menu.Aimed = AimLineResolver.Resolve(_layout, menu.Opened, current);
        var highlighted = menu.Aimed ?? menu.Cursor;
        for (var i = 0; i < menu.Lines.Count; i++)
        {
            var color = i == highlighted ? Accent : Color.White;
            if (menu.Lines[i].Color != color)
            {
                menu.Lines[i].Color = color;
                Utilities.SetStateChanged(menu.Lines[i], "CPointWorldText", "m_Color");
            }
        }
        if (menu.Aimed is not null)
        {
            BlockAttack(pawn);
        }
    }

    private static void BlockAttack(CCSPlayerPawn pawn)
    {
        var weapon = pawn.WeaponServices?.ActiveWeapon.Value;
        if (weapon is null || !weapon.IsValid)
        {
            return;
        }
        weapon.NextPrimaryAttackTick = Server.TickCount + 2;
        weapon.NextSecondaryAttackTick = Server.TickCount + 2;
        Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_nNextPrimaryAttackTick");
        Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_nNextSecondaryAttackTick");
    }

    private void OnButtons(CCSPlayerController player, PlayerButtons pressed, PlayerButtons released)
    {
        if (!_menus.TryGetValue(player.Slot, out var menu))
        {
            return;
        }
        if ((pressed & PlayerButtons.Attack) != 0 && menu.Aimed is { } aimed)
        {
            Select(menu, aimed, "aim+click");
        }
        else if ((pressed & PlayerButtons.Forward) != 0)
        {
            menu.Cursor = Math.Max(0, menu.Cursor - 1);
        }
        else if ((pressed & PlayerButtons.Back) != 0)
        {
            menu.Cursor = Math.Min(Labels.Length - 1, menu.Cursor + 1);
        }
        else if ((pressed & PlayerButtons.Use) != 0)
        {
            Select(menu, menu.Cursor, "E");
        }
    }

    private HookResult OnSlot(CCSPlayerController? player, int slot)
    {
        if (player is null || !_menus.TryGetValue(player.Slot, out var menu))
        {
            return HookResult.Continue;
        }
        if (slot <= Labels.Length)
        {
            Select(menu, slot - 1, $"key {slot}");
        }
        return HookResult.Handled;
    }

    private void Select(ProbeMenu menu, int index, string how)
    {
        menu.Owner.PrintToChat($" [HudProbe] selected '{Labels[index]}' via {how}");
        Logger.LogInformation("HudProbe: {Player} selected {Label} via {How}", menu.Owner.PlayerName, Labels[index], how);
        if (Labels[index] == "Fermer")
        {
            Close(menu.Owner);
        }
    }

    private void OnCheckTransmit(CCheckTransmitInfoList infoList)
    {
        if (_menus.Count == 0)
        {
            return;
        }
        foreach ((CCheckTransmitInfo info, CCSPlayerController? viewer) in infoList)
        {
            if (viewer is null)
            {
                continue;
            }
            foreach (var menu in _menus.Values.Where(m => m.Owner.Slot != viewer.Slot))
            {
                menu.Lines.Where(line => line.IsValid).ToList().ForEach(line => info.TransmitEntities.Remove(line));
            }
        }
    }

    private void Close(CCSPlayerController player)
    {
        if (_menus.Remove(player.Slot, out var menu))
        {
            DestroyLines(menu);
        }
    }

    private static void DestroyLines(ProbeMenu menu)
    {
        menu.Lines.Where(line => line.IsValid).ToList().ForEach(line => line.Remove());
        menu.Lines.Clear();
    }
}
```


- [ ] **Step 3: Compiler le prototype**

Run: `dotnet build spikes/HudProbe/HudProbe.csproj -c Release --nologo`
Expected: `0 Avertissement(s)`, `0 Erreur(s)`. Si CSSharp 1.0.370 refuse une signature (par ex. `ActiveWeapon.Value` ou l'égalité de `Color`), corriger en gardant le comportement et noter l'écart dans le fichier de résultats.

- [ ] **Step 4: Écrire le fichier de résultats (template)**

`docs/spikes/hud-probe-findings.md`
```markdown
# HudProbe — résultats du prototype HUD (phase 0)

Build testé : commit `<hash>` — Serveur : `<Dathost / local>` — CSSharp : `<version>` — Date : `<date>`

## Protocole
1. Copier `spikes/HudProbe/bin/Release/net10.0/HudProbe.dll` et `RetakeV4.Domain.dll` dans `addons/counterstrikesharp/plugins/HudProbe/`.
2. `css_plugins load HudProbe` (ou redémarrer la map).
3. En jeu, pendant le freeze time puis en round live : `css_hudprobe`.

## Questions (répondre OUI/NON + remarques)
| # | Question | Réponse |
|---|---|---|
| 1 | Quelle formule d'orientation (`css_hudprobe_orient 0/1/2`) rend le texte lisible face au joueur ? | |
| 2 | Le menu ancré à l'ouverture suit-il la position du joueur sans tremblement gênant en marchant ? | |
| 3 | Précision viseur : sur 20 essais par ligne, combien de mauvaises sélections ? Distance confortable (`css_hudprobe_dist`) ? | |
| 4 | Le clic gauche sur une ligne visée ne tire PAS (freeze time) ? | |
| 5 | Le clic gauche sur une ligne visée ne tire PAS (round live, arme en main) ? | |
| 6 | Les touches 1-9 sélectionnent sans changer d'arme ? | |
| 7 | W/S déplacent le curseur et E valide pendant le freeze time ? | |
| 8 | Un second joueur ne voit PAS le menu (CheckTransmit) ? | |
| 9 | Le changement de couleur de la ligne visée est visible immédiatement ? | |
| 10 | `css_hudprobe_parent` puis ouverture : le menu suit-il le pawn de façon plus fluide que le repositionnement par tick ? | |
| 11 | Fin de round / restart avec menu ouvert : aucune erreur console, aucun crash ? | |
| 12 | Impact perf perçu (fps serveur/client) avec 2+ menus ouverts ? | |

## Décisions
- Sélection au viseur : **GO / NO-GO**
- Formule d'orientation retenue :
- Mode de positionnement retenu (tick / parent pawn) :
- Layout retenu (distance, hauteur de ligne, police, WorldUnitsPerPx) :
- Écarts d'API constatés :
```

- [ ] **Step 5: Commit**

```bash
git add spikes/HudProbe/HudProbe.csproj spikes/HudProbe/HudProbePlugin.cs docs/spikes/hud-probe-findings.md
git commit -m "chore: prototype jetable HudProbe (phase 0)"
```

- [ ] **Step 6: CHECKPOINT HUMAIN — tester en jeu**

S'arrêter et demander à l'utilisateur de déployer HudProbe sur un serveur de test, de dérouler le protocole et de remplir `docs/spikes/hud-probe-findings.md`. **Ne pas continuer les tâches dépendantes du HUD** (phase 3) avant ce retour. Les tâches 5 à 13 (phase 1) ne dépendent pas du résultat et peuvent continuer. Une fois le document rempli, le committer : `git commit -am "docs: résultats du prototype HUD"`.

---

# PHASE 1 — Fondations

### Task 5: Machine à états du round (Domain)

**Files:**
- Create: `src/RetakeV4.Domain/Rounds/RoundPhase.cs`, `RoundSignal.cs`, `RoundState.cs`, `RoundStateMachine.cs`
- Test: `tests/RetakeV4.Domain.Tests/Rounds/RoundStateMachineTests.cs`

**Interfaces:**
- Produces:
  - `enum RoundPhase { Warmup, Preparing, FreezeTime, Live, PostRound }`
  - `enum RoundSignal { WarmupStarted, WarmupEnded, RoundStarted, PreparationCompleted, FreezeEnded, RoundEnded }`
  - `sealed record RoundState(RoundPhase Phase, int RoundNumber) { static RoundState Initial /* Warmup, 0 */ }`
  - `static class RoundStateMachine { RoundState Apply(RoundState state, RoundSignal signal); }` — renvoie l'état inchangé (égal par valeur) si le signal n'a pas d'effet.

- [ ] **Step 1: Écrire les tests qui échouent**

`tests/RetakeV4.Domain.Tests/Rounds/RoundStateMachineTests.cs`
```csharp
using RetakeV4.Domain.Rounds;

namespace RetakeV4.Domain.Tests.Rounds;

public class RoundStateMachineTests
{
    [Theory]
    [InlineData(RoundPhase.Warmup, RoundSignal.RoundStarted, RoundPhase.Warmup, 0)]
    [InlineData(RoundPhase.Warmup, RoundSignal.WarmupEnded, RoundPhase.PostRound, 0)]
    [InlineData(RoundPhase.PostRound, RoundSignal.RoundStarted, RoundPhase.Preparing, 1)]
    [InlineData(RoundPhase.Preparing, RoundSignal.PreparationCompleted, RoundPhase.FreezeTime, 0)]
    [InlineData(RoundPhase.FreezeTime, RoundSignal.FreezeEnded, RoundPhase.Live, 0)]
    [InlineData(RoundPhase.Preparing, RoundSignal.FreezeEnded, RoundPhase.Live, 0)]
    [InlineData(RoundPhase.Live, RoundSignal.RoundEnded, RoundPhase.PostRound, 0)]
    [InlineData(RoundPhase.FreezeTime, RoundSignal.RoundEnded, RoundPhase.PostRound, 0)]
    [InlineData(RoundPhase.Live, RoundSignal.RoundStarted, RoundPhase.Preparing, 1)]
    [InlineData(RoundPhase.Live, RoundSignal.WarmupStarted, RoundPhase.Warmup, 0)]
    public void Apply_Transitions(RoundPhase from, RoundSignal signal, RoundPhase expectedPhase, int expectedRoundNumber)
    {
        var result = RoundStateMachine.Apply(new RoundState(from, 0), signal);
        Assert.Equal(new RoundState(expectedPhase, expectedRoundNumber), result);
    }

    [Theory]
    [InlineData(RoundPhase.Warmup, RoundSignal.FreezeEnded)]
    [InlineData(RoundPhase.Warmup, RoundSignal.RoundEnded)]
    [InlineData(RoundPhase.Warmup, RoundSignal.PreparationCompleted)]
    [InlineData(RoundPhase.Live, RoundSignal.WarmupEnded)]
    [InlineData(RoundPhase.PostRound, RoundSignal.RoundEnded)]
    [InlineData(RoundPhase.Live, RoundSignal.PreparationCompleted)]
    [InlineData(RoundPhase.Live, RoundSignal.FreezeEnded)]
    public void Apply_IgnoresIrrelevantSignals(RoundPhase from, RoundSignal signal)
    {
        var state = new RoundState(from, 4);
        Assert.Equal(state, RoundStateMachine.Apply(state, signal));
    }

    [Fact]
    public void RoundStarted_IncrementsRoundNumber()
    {
        var state = new RoundState(RoundPhase.PostRound, 7);
        Assert.Equal(8, RoundStateMachine.Apply(state, RoundSignal.RoundStarted).RoundNumber);
    }

    [Fact]
    public void Initial_IsWarmupRoundZero()
    {
        Assert.Equal(new RoundState(RoundPhase.Warmup, 0), RoundState.Initial);
    }

    [Fact]
    public void UnknownSignal_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RoundStateMachine.Apply(RoundState.Initial, (RoundSignal)99));
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo --filter "FullyQualifiedName~RoundStateMachineTests"`
Expected: échec de compilation (types `Rounds` introuvables).

- [ ] **Step 3: Implémentation**

`src/RetakeV4.Domain/Rounds/RoundPhase.cs`
```csharp
namespace RetakeV4.Domain.Rounds;

public enum RoundPhase
{
    Warmup,
    Preparing,
    FreezeTime,
    Live,
    PostRound,
}
```

`src/RetakeV4.Domain/Rounds/RoundSignal.cs`
```csharp
namespace RetakeV4.Domain.Rounds;

public enum RoundSignal
{
    WarmupStarted,
    WarmupEnded,
    RoundStarted,
    PreparationCompleted,
    FreezeEnded,
    RoundEnded,
}
```

`src/RetakeV4.Domain/Rounds/RoundState.cs`
```csharp
namespace RetakeV4.Domain.Rounds;

public sealed record RoundState(RoundPhase Phase, int RoundNumber)
{
    public static RoundState Initial { get; } = new(RoundPhase.Warmup, 0);
}
```

`src/RetakeV4.Domain/Rounds/RoundStateMachine.cs`
```csharp
namespace RetakeV4.Domain.Rounds;

public static class RoundStateMachine
{
    public static RoundState Apply(RoundState state, RoundSignal signal) => signal switch
    {
        RoundSignal.WarmupStarted => state with { Phase = RoundPhase.Warmup },
        RoundSignal.WarmupEnded => state.Phase == RoundPhase.Warmup ? state with { Phase = RoundPhase.PostRound } : state,
        RoundSignal.RoundStarted => state.Phase == RoundPhase.Warmup
            ? state
            : new RoundState(RoundPhase.Preparing, state.RoundNumber + 1),
        RoundSignal.PreparationCompleted => state.Phase == RoundPhase.Preparing ? state with { Phase = RoundPhase.FreezeTime } : state,
        RoundSignal.FreezeEnded => state.Phase is RoundPhase.Preparing or RoundPhase.FreezeTime
            ? state with { Phase = RoundPhase.Live }
            : state,
        RoundSignal.RoundEnded => state.Phase is RoundPhase.Preparing or RoundPhase.FreezeTime or RoundPhase.Live
            ? state with { Phase = RoundPhase.PostRound }
            : state,
        _ => throw new ArgumentOutOfRangeException(nameof(signal), signal, "Unknown round signal"),
    };
}
```

- [ ] **Step 4: Vérifier le succès**

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo --filter "FullyQualifiedName~RoundStateMachineTests"`
Expected: `Failed: 0`.

- [ ] **Step 5: Commit**

```bash
git add src/RetakeV4.Domain/Rounds tests/RetakeV4.Domain.Tests/Rounds
git commit -m "feat: machine à états du round (Domain)"
```

---

### Task 6: Watchdog de fin de warmup (Domain)

**Files:**
- Create: `src/RetakeV4.Domain/Rounds/WarmupTracker.cs`
- Test: `tests/RetakeV4.Domain.Tests/Rounds/WarmupTrackerTests.cs`

**Interfaces:**
- Produces:
  - `sealed record WarmupSnapshot(bool IsWarmup, float WarmupPeriodEnd, float Now)`
  - `sealed record WarmupTracker(float FallbackSeconds, float MapStartedAt, bool ForcedThisMap)` avec :
    - `static WarmupTracker Start(float fallbackSeconds, float now)` — lève `ArgumentOutOfRangeException` si `fallbackSeconds < 0`.
    - `(WarmupTracker Next, bool ForceEnd) Evaluate(WarmupSnapshot snapshot)`
- Règle (reprise d'agora) : on force la fin du warmup une seule fois par map, si (a) `WarmupPeriodEnd > 0` et `Now >= WarmupPeriodEnd`, ou (b) `WarmupPeriodEnd <= 0` (mode compétitif) et `Now - MapStartedAt >= FallbackSeconds` avec `FallbackSeconds > 0`.

- [ ] **Step 1: Écrire les tests qui échouent**

`tests/RetakeV4.Domain.Tests/Rounds/WarmupTrackerTests.cs`
```csharp
using RetakeV4.Domain.Rounds;

namespace RetakeV4.Domain.Tests.Rounds;

public class WarmupTrackerTests
{
    [Fact]
    public void NotWarmup_NeverForces()
    {
        var (_, force) = WarmupTracker.Start(16f, 0f).Evaluate(new WarmupSnapshot(false, 0f, 999f));
        Assert.False(force);
    }

    [Fact]
    public void TimedWarmup_ForcesWhenTimerElapsed()
    {
        var (_, force) = WarmupTracker.Start(16f, 0f).Evaluate(new WarmupSnapshot(true, 30f, 30f));
        Assert.True(force);
    }

    [Fact]
    public void TimedWarmup_DoesNotForceBeforeTimer()
    {
        var (_, force) = WarmupTracker.Start(16f, 0f).Evaluate(new WarmupSnapshot(true, 30f, 29.9f));
        Assert.False(force);
    }

    [Fact]
    public void CompetitiveWarmup_UsesFallbackFromMapStart()
    {
        var tracker = WarmupTracker.Start(16f, 100f);
        Assert.False(tracker.Evaluate(new WarmupSnapshot(true, 0f, 115.9f)).ForceEnd);
        Assert.True(tracker.Evaluate(new WarmupSnapshot(true, 0f, 116f)).ForceEnd);
    }

    [Fact]
    public void ForcesOnlyOncePerMap()
    {
        var (next, first) = WarmupTracker.Start(16f, 0f).Evaluate(new WarmupSnapshot(true, 0f, 20f));
        var (_, second) = next.Evaluate(new WarmupSnapshot(true, 0f, 21f));
        Assert.True(first);
        Assert.False(second);
    }

    [Fact]
    public void NewMap_RearmsTheWatchdog()
    {
        var (afterFirstMap, _) = WarmupTracker.Start(16f, 0f).Evaluate(new WarmupSnapshot(true, 0f, 20f));
        Assert.True(afterFirstMap.ForcedThisMap);
        var secondMap = WarmupTracker.Start(afterFirstMap.FallbackSeconds, 500f);
        Assert.True(secondMap.Evaluate(new WarmupSnapshot(true, 0f, 516f)).ForceEnd);
    }

    [Fact]
    public void ZeroFallback_DisablesCompetitiveFallback()
    {
        Assert.False(WarmupTracker.Start(0f, 0f).Evaluate(new WarmupSnapshot(true, 0f, 10_000f)).ForceEnd);
    }

    [Fact]
    public void NegativeFallback_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WarmupTracker.Start(-1f, 0f));
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo --filter "FullyQualifiedName~WarmupTrackerTests"`
Expected: échec de compilation (`WarmupTracker` introuvable).

- [ ] **Step 3: Implémentation**

`src/RetakeV4.Domain/Rounds/WarmupTracker.cs`
```csharp
namespace RetakeV4.Domain.Rounds;

public sealed record WarmupSnapshot(bool IsWarmup, float WarmupPeriodEnd, float Now);

public sealed record WarmupTracker(float FallbackSeconds, float MapStartedAt, bool ForcedThisMap)
{
    public static WarmupTracker Start(float fallbackSeconds, float now)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fallbackSeconds);
        return new WarmupTracker(fallbackSeconds, now, false);
    }

    public (WarmupTracker Next, bool ForceEnd) Evaluate(WarmupSnapshot snapshot)
    {
        if (ForcedThisMap || !snapshot.IsWarmup)
        {
            return (this, false);
        }

        var timerElapsed = snapshot.WarmupPeriodEnd > 0f && snapshot.Now >= snapshot.WarmupPeriodEnd;
        var fallbackElapsed = snapshot.WarmupPeriodEnd <= 0f
            && FallbackSeconds > 0f
            && snapshot.Now - MapStartedAt >= FallbackSeconds;

        return timerElapsed || fallbackElapsed
            ? (this with { ForcedThisMap = true }, true)
            : (this, false);
    }
}
```

- [ ] **Step 4: Vérifier le succès**

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo --filter "FullyQualifiedName~WarmupTrackerTests"`
Expected: `Failed: 0`.

- [ ] **Step 5: Commit**

```bash
git add src/RetakeV4.Domain/Rounds/WarmupTracker.cs tests/RetakeV4.Domain.Tests/Rounds/WarmupTrackerTests.cs
git commit -m "feat: watchdog de fin de warmup (correctif warmup infini)"
```

---

### Task 7: Bus d'événements, planification des modules et garde d'erreurs (Domain)

**Files:**
- Create: `src/RetakeV4.Domain/Events/BusError.cs`, `IEventBus.cs`, `EventBus.cs`
- Create: `src/RetakeV4.Domain/Modules/ModuleDescriptor.cs`, `ModuleLoadPlanner.cs`, `ErrorBudget.cs`, `ModuleGuard.cs`
- Test: `tests/RetakeV4.Domain.Tests/Events/EventBusTests.cs`, `tests/RetakeV4.Domain.Tests/Modules/ModuleLoadPlannerTests.cs`, `tests/RetakeV4.Domain.Tests/Modules/ModuleGuardTests.cs`

**Interfaces:**
- Produces:
  - `sealed record BusError(string Subscriber, Type EventType, Exception Exception)`
  - `interface IEventBus { IDisposable Subscribe<TEvent>(string subscriber, Action<TEvent> handler) where TEvent : notnull; void Publish<TEvent>(TEvent evt) where TEvent : notnull; }`
  - `sealed class EventBus(Action<BusError> onError) : IEventBus` — dispatch sur le **type exact**, dans l'ordre d'abonnement ; une exception d'un abonné est remontée à `onError` et n'empêche pas les suivants ; un abonnement ajouté pendant un `Publish` ne reçoit pas l'événement en cours ; pas thread-safe (thread de jeu uniquement).
  - `sealed record ModuleDescriptor(string Name, IReadOnlyList<string> DependsOn, bool Enabled)`
  - `enum SkipReason { Disabled, MissingDependency, DependencyCycle }`
  - `sealed record SkippedModule(string Name, SkipReason Reason, string Detail)`
  - `sealed record ModuleLoadPlan(IReadOnlyList<string> Order, IReadOnlyList<SkippedModule> Skipped)`
  - `static class ModuleLoadPlanner { ModuleLoadPlan Plan(IReadOnlyList<ModuleDescriptor> modules); }` — lève `ArgumentException` sur un nom dupliqué.
  - `sealed record ErrorBudget(int MaxErrorsPerRound, ImmutableDictionary<string,int> Counts, ImmutableHashSet<string> Disabled)` avec `static Create(int)`, `bool IsDisabled(string)`, `ErrorBudget RecordError(string)`, `ErrorBudget ResetRound()` (remet les compteurs à zéro mais **garde** les modules désactivés).
  - `sealed record GuardFailure(string Module, string Stage, Exception Exception, bool ModuleDisabled)`
  - `sealed class ModuleGuard(int maxErrorsPerRound, Action<GuardFailure> onFailure)` avec `bool IsDisabled(string)`, `void Run(string module, string stage, Action action)`, `T Run<T>(string module, string stage, Func<T> action, T fallback)`, `void ResetRound()`.

- [ ] **Step 1: Écrire les tests du bus**

`tests/RetakeV4.Domain.Tests/Events/EventBusTests.cs`
```csharp
using RetakeV4.Domain.Events;

namespace RetakeV4.Domain.Tests.Events;

public class EventBusTests
{
    private record Ping(int Value);
    private record DerivedPing(int Value) : Ping(Value);

    private readonly List<BusError> _errors = new();
    private readonly EventBus _bus;

    public EventBusTests() => _bus = new EventBus(_errors.Add);

    [Fact]
    public void Publish_InvokesSubscribersInSubscriptionOrder()
    {
        var calls = new List<string>();
        _bus.Subscribe<Ping>("a", _ => calls.Add("a"));
        _bus.Subscribe<Ping>("b", _ => calls.Add("b"));
        _bus.Publish(new Ping(1));
        Assert.Equal(new[] { "a", "b" }, calls);
    }

    [Fact]
    public void ThrowingSubscriber_IsIsolatedAndReported()
    {
        var received = 0;
        _bus.Subscribe<Ping>("broken", _ => throw new InvalidOperationException("boom"));
        _bus.Subscribe<Ping>("healthy", _ => received++);
        _bus.Publish(new Ping(1));
        Assert.Equal(1, received);
        var error = Assert.Single(_errors);
        Assert.Equal("broken", error.Subscriber);
        Assert.Equal(typeof(Ping), error.EventType);
    }

    [Fact]
    public void DisposedSubscription_NoLongerReceives()
    {
        var received = 0;
        var subscription = _bus.Subscribe<Ping>("a", _ => received++);
        subscription.Dispose();
        subscription.Dispose();
        _bus.Publish(new Ping(1));
        Assert.Equal(0, received);
    }

    [Fact]
    public void SubscriptionAddedDuringPublish_DoesNotReceiveCurrentEvent()
    {
        var lateCalls = 0;
        _bus.Subscribe<Ping>("adder", _ => _bus.Subscribe<Ping>("late", _ => lateCalls++));
        _bus.Publish(new Ping(1));
        Assert.Equal(0, lateCalls);
        _bus.Publish(new Ping(2));
        Assert.Equal(1, lateCalls);
    }

    [Fact]
    public void Dispatch_IsByExactType()
    {
        var baseCalls = 0;
        _bus.Subscribe<Ping>("base", _ => baseCalls++);
        _bus.Publish(new DerivedPing(1));
        Assert.Equal(0, baseCalls);
    }

    [Fact]
    public void NestedPublish_IsDeliveredSynchronously()
    {
        var order = new List<string>();
        _bus.Subscribe<Ping>("outer", p =>
        {
            order.Add($"outer{p.Value}");
            if (p.Value == 1)
            {
                _bus.Publish(new Ping(2));
            }
        });
        _bus.Publish(new Ping(1));
        Assert.Equal(new[] { "outer1", "outer2" }, order);
    }

    [Fact]
    public void Subscribe_RejectsBlankSubscriberName()
    {
        Assert.Throws<ArgumentException>(() => _bus.Subscribe<Ping>(" ", _ => { }));
    }
}
```

- [ ] **Step 2: Écrire les tests de planification et de garde**

`tests/RetakeV4.Domain.Tests/Modules/ModuleLoadPlannerTests.cs`
```csharp
using RetakeV4.Domain.Modules;

namespace RetakeV4.Domain.Tests.Modules;

public class ModuleLoadPlannerTests
{
    private static ModuleDescriptor M(string name, bool enabled = true, params string[] deps) => new(name, deps, enabled);

    [Fact]
    public void Plan_OrdersDependenciesFirst_StableOtherwise()
    {
        var plan = ModuleLoadPlanner.Plan(new[] { M("Hud", true, "Core"), M("Core"), M("Links") });
        Assert.Equal(new[] { "Core", "Hud", "Links" }, plan.Order);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void DisabledModule_IsSkipped_AndItsDependentsToo()
    {
        var plan = ModuleLoadPlanner.Plan(new[] { M("Core"), M("Spawns", false, "Core"), M("Plant", true, "Spawns") });
        Assert.Equal(new[] { "Core" }, plan.Order);
        Assert.Contains(plan.Skipped, s => s is { Name: "Spawns", Reason: SkipReason.Disabled });
        Assert.Contains(plan.Skipped, s => s is { Name: "Plant", Reason: SkipReason.MissingDependency });
    }

    [Fact]
    public void UnknownDependency_IsReported()
    {
        var plan = ModuleLoadPlanner.Plan(new[] { M("Hud", true, "Ghost") });
        var skipped = Assert.Single(plan.Skipped);
        Assert.Equal(SkipReason.MissingDependency, skipped.Reason);
        Assert.Contains("Ghost", skipped.Detail);
    }

    [Fact]
    public void Cycle_SkipsAllCyclicModules()
    {
        var plan = ModuleLoadPlanner.Plan(new[] { M("A", true, "B"), M("B", true, "A"), M("C") });
        Assert.Equal(new[] { "C" }, plan.Order);
        Assert.Equal(2, plan.Skipped.Count(s => s.Reason == SkipReason.DependencyCycle));
    }

    [Fact]
    public void DuplicateNames_Throw()
    {
        Assert.Throws<ArgumentException>(() => ModuleLoadPlanner.Plan(new[] { M("Core"), M("Core") }));
    }
}
```

`tests/RetakeV4.Domain.Tests/Modules/ModuleGuardTests.cs`
```csharp
using RetakeV4.Domain.Modules;

namespace RetakeV4.Domain.Tests.Modules;

public class ModuleGuardTests
{
    private readonly List<GuardFailure> _failures = new();

    [Fact]
    public void Run_ReturnsActionResult()
    {
        var guard = new ModuleGuard(3, _failures.Add);
        Assert.Equal(42, guard.Run("Core", "stage", () => 42, -1));
        Assert.Empty(_failures);
    }

    [Fact]
    public void Run_CatchesAndReturnsFallback()
    {
        var guard = new ModuleGuard(3, _failures.Add);
        var result = guard.Run<int>("Core", "round_start", () => throw new InvalidOperationException(), -1);
        Assert.Equal(-1, result);
        var failure = Assert.Single(_failures);
        Assert.Equal("round_start", failure.Stage);
        Assert.False(failure.ModuleDisabled);
    }

    [Fact]
    public void ReachingBudget_DisablesModule_AndSkipsFurtherRuns()
    {
        var guard = new ModuleGuard(2, _failures.Add);
        var executions = 0;
        void Failing() { executions++; throw new InvalidOperationException(); }
        guard.Run("Plant", "s", Failing);
        guard.Run("Plant", "s", Failing);
        guard.Run("Plant", "s", Failing);
        Assert.Equal(2, executions);
        Assert.True(guard.IsDisabled("Plant"));
        Assert.True(_failures[^1].ModuleDisabled);
    }

    [Fact]
    public void ResetRound_ClearsCounts_ButKeepsDisabledModules()
    {
        var guard = new ModuleGuard(2, _failures.Add);
        void Failing() => throw new InvalidOperationException();
        guard.Run("A", "s", Failing);
        guard.Run("B", "s", Failing);
        guard.Run("B", "s", Failing);
        guard.ResetRound();
        guard.Run("A", "s", Failing);
        Assert.False(guard.IsDisabled("A"));
        Assert.True(guard.IsDisabled("B"));
    }

    [Fact]
    public void ErrorsAreCountedPerModule()
    {
        var budget = ErrorBudget.Create(2).RecordError("A").RecordError("B");
        Assert.False(budget.IsDisabled("A"));
        Assert.False(budget.IsDisabled("B"));
    }

    [Fact]
    public void Budget_RejectsNonPositiveMax()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ErrorBudget.Create(0));
    }
}
```

- [ ] **Step 3: Vérifier l'échec**

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: échec de compilation (types `Events` et `Modules` introuvables).

- [ ] **Step 4: Implémenter le bus**

`src/RetakeV4.Domain/Events/BusError.cs`
```csharp
namespace RetakeV4.Domain.Events;

public sealed record BusError(string Subscriber, Type EventType, Exception Exception);
```

`src/RetakeV4.Domain/Events/IEventBus.cs`
```csharp
namespace RetakeV4.Domain.Events;

public interface IEventBus
{
    IDisposable Subscribe<TEvent>(string subscriber, Action<TEvent> handler) where TEvent : notnull;

    void Publish<TEvent>(TEvent evt) where TEvent : notnull;
}
```

`src/RetakeV4.Domain/Events/EventBus.cs`
```csharp
using System.Collections.Immutable;

namespace RetakeV4.Domain.Events;

// Game-thread only: CounterStrikeSharp dispatches every event on the main thread.
public sealed class EventBus : IEventBus
{
    private readonly Action<BusError> _onError;
    private ImmutableDictionary<Type, ImmutableList<Subscription>> _subscriptions =
        ImmutableDictionary<Type, ImmutableList<Subscription>>.Empty;

    public EventBus(Action<BusError> onError)
    {
        ArgumentNullException.ThrowIfNull(onError);
        _onError = onError;
    }

    public IDisposable Subscribe<TEvent>(string subscriber, Action<TEvent> handler) where TEvent : notnull
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriber);
        ArgumentNullException.ThrowIfNull(handler);
        var key = typeof(TEvent);
        var subscription = new Subscription(subscriber, evt => handler((TEvent)evt));
        var current = _subscriptions.GetValueOrDefault(key, ImmutableList<Subscription>.Empty);
        _subscriptions = _subscriptions.SetItem(key, current.Add(subscription));
        return new Unsubscriber(() => Remove(key, subscription));
    }

    public void Publish<TEvent>(TEvent evt) where TEvent : notnull
    {
        if (!_subscriptions.TryGetValue(typeof(TEvent), out var subscriptions))
        {
            return;
        }
        foreach (var subscription in subscriptions)
        {
            Invoke(subscription, evt, typeof(TEvent));
        }
    }

    private void Invoke(Subscription subscription, object evt, Type eventType)
    {
        try
        {
            subscription.Handler(evt);
        }
        catch (Exception ex)
        {
            _onError(new BusError(subscription.Subscriber, eventType, ex));
        }
    }

    private void Remove(Type key, Subscription subscription)
    {
        if (_subscriptions.TryGetValue(key, out var subscriptions))
        {
            _subscriptions = _subscriptions.SetItem(key, subscriptions.Remove(subscription));
        }
    }

    private sealed class Subscription(string subscriber, Action<object> handler)
    {
        public string Subscriber { get; } = subscriber;
        public Action<object> Handler { get; } = handler;
    }

    private sealed class Unsubscriber(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
```

- [ ] **Step 5: Implémenter la planification et la garde**

`src/RetakeV4.Domain/Modules/ModuleDescriptor.cs`
```csharp
namespace RetakeV4.Domain.Modules;

public sealed record ModuleDescriptor(string Name, IReadOnlyList<string> DependsOn, bool Enabled);

public enum SkipReason
{
    Disabled,
    MissingDependency,
    DependencyCycle,
}

public sealed record SkippedModule(string Name, SkipReason Reason, string Detail);

public sealed record ModuleLoadPlan(IReadOnlyList<string> Order, IReadOnlyList<SkippedModule> Skipped);
```

`src/RetakeV4.Domain/Modules/ModuleLoadPlanner.cs`
```csharp
namespace RetakeV4.Domain.Modules;

public static class ModuleLoadPlanner
{
    public static ModuleLoadPlan Plan(IReadOnlyList<ModuleDescriptor> modules)
    {
        EnsureUniqueNames(modules);
        var unavailable = FindUnavailable(modules);
        var candidates = modules.Where(m => !unavailable.ContainsKey(m.Name)).ToList();
        var (order, cyclic) = TopologicalOrder(candidates);
        var skipped = unavailable.Values
            .Concat(cyclic.Select(name => new SkippedModule(name, SkipReason.DependencyCycle, "dependency cycle")))
            .ToList();
        return new ModuleLoadPlan(order, skipped);
    }

    private static void EnsureUniqueNames(IReadOnlyList<ModuleDescriptor> modules)
    {
        var duplicate = modules.GroupBy(m => m.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException($"Duplicate module name '{duplicate.Key}'", nameof(modules));
        }
    }

    private static Dictionary<string, SkippedModule> FindUnavailable(IReadOnlyList<ModuleDescriptor> modules)
    {
        var known = modules.Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
        var skipped = modules
            .Where(m => !m.Enabled)
            .ToDictionary(m => m.Name, m => new SkippedModule(m.Name, SkipReason.Disabled, "disabled in config"), StringComparer.Ordinal);
        bool changed;
        do
        {
            changed = false;
            foreach (var module in modules.Where(m => !skipped.ContainsKey(m.Name)))
            {
                var missing = module.DependsOn.FirstOrDefault(d => !known.Contains(d) || skipped.ContainsKey(d));
                if (missing is null)
                {
                    continue;
                }
                var detail = known.Contains(missing) ? $"dependency '{missing}' is not loaded" : $"dependency '{missing}' does not exist";
                skipped[module.Name] = new SkippedModule(module.Name, SkipReason.MissingDependency, detail);
                changed = true;
            }
        }
        while (changed);
        return skipped;
    }

    private static (List<string> Order, List<string> Cyclic) TopologicalOrder(List<ModuleDescriptor> candidates)
    {
        var remaining = candidates.ToList();
        var order = new List<string>();
        var placed = new HashSet<string>(StringComparer.Ordinal);
        while (remaining.Count > 0)
        {
            var next = remaining.FirstOrDefault(m => m.DependsOn.All(placed.Contains));
            if (next is null)
            {
                break;
            }
            order.Add(next.Name);
            placed.Add(next.Name);
            remaining.Remove(next);
        }
        return (order, remaining.Select(m => m.Name).ToList());
    }
}
```

`src/RetakeV4.Domain/Modules/ErrorBudget.cs`
```csharp
using System.Collections.Immutable;

namespace RetakeV4.Domain.Modules;

public sealed record ErrorBudget(int MaxErrorsPerRound, ImmutableDictionary<string, int> Counts, ImmutableHashSet<string> Disabled)
{
    public static ErrorBudget Create(int maxErrorsPerRound)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxErrorsPerRound, 1);
        return new ErrorBudget(maxErrorsPerRound, ImmutableDictionary<string, int>.Empty, ImmutableHashSet<string>.Empty);
    }

    public bool IsDisabled(string module) => Disabled.Contains(module);

    public ErrorBudget RecordError(string module)
    {
        var count = Counts.GetValueOrDefault(module) + 1;
        var counts = Counts.SetItem(module, count);
        return count >= MaxErrorsPerRound
            ? this with { Counts = counts, Disabled = Disabled.Add(module) }
            : this with { Counts = counts };
    }

    public ErrorBudget ResetRound() => this with { Counts = ImmutableDictionary<string, int>.Empty };
}
```

`src/RetakeV4.Domain/Modules/ModuleGuard.cs`
```csharp
namespace RetakeV4.Domain.Modules;

public sealed record GuardFailure(string Module, string Stage, Exception Exception, bool ModuleDisabled);

public sealed class ModuleGuard
{
    private readonly Action<GuardFailure> _onFailure;
    private ErrorBudget _budget;

    public ModuleGuard(int maxErrorsPerRound, Action<GuardFailure> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onFailure);
        _budget = ErrorBudget.Create(maxErrorsPerRound);
        _onFailure = onFailure;
    }

    public bool IsDisabled(string module) => _budget.IsDisabled(module);

    public void Run(string module, string stage, Action action) =>
        Run(module, stage, () =>
        {
            action();
            return true;
        }, false);

    public T Run<T>(string module, string stage, Func<T> action, T fallback)
    {
        if (_budget.IsDisabled(module))
        {
            return fallback;
        }
        try
        {
            return action();
        }
        catch (Exception ex)
        {
            _budget = _budget.RecordError(module);
            _onFailure(new GuardFailure(module, stage, ex, _budget.IsDisabled(module)));
            return fallback;
        }
    }

    public void ResetRound() => _budget = _budget.ResetRound();
}
```

- [ ] **Step 6: Vérifier le succès**

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: `Failed: 0`.

- [ ] **Step 7: Commit**

```bash
git add src/RetakeV4.Domain/Events src/RetakeV4.Domain/Modules tests/RetakeV4.Domain.Tests/Events tests/RetakeV4.Domain.Tests/Modules
git commit -m "feat: bus d'événements, planification des modules et garde d'erreurs (Domain)"
```

---

### Task 8: Pipeline de préparation et RoundTracker (Domain)

**Files:**
- Create: `src/RetakeV4.Domain/Rounds/PreparationContext.cs`, `IPreparationStep.cs`, `PreparationPipeline.cs`, `RoundTracker.cs`
- Create: `src/RetakeV4.Domain/Events/RoundEvents.cs`
- Test: `tests/RetakeV4.Domain.Tests/Rounds/PreparationPipelineTests.cs`, `tests/RetakeV4.Domain.Tests/Rounds/RoundTrackerTests.cs`

**Interfaces:**
- Consumes: `RoundState`, `RoundPhase`, `RoundSignal`, `RoundStateMachine` (Task 5) ; `IEventBus`, `EventBus` (Task 7) ; `ModuleGuard` (Task 7).
- Produces:
  - `sealed record PreparationContext(int RoundNumber)` — enrichi par les phases suivantes via de nouvelles propriétés `init`.
  - `interface IPreparationStep { string Name { get; } int Order { get; } PreparationContext Execute(PreparationContext context); }`
  - `sealed class PreparationPipeline(ModuleGuard guard)` avec `IDisposable Register(string module, IPreparationStep step)` et `PreparationContext Execute(PreparationContext initial)` — étapes triées par `Order` puis par ordre d'enregistrement ; une étape qui lève une exception est sautée (le contexte précédent est conservé) et comptée par la garde du module.
  - Événements : `sealed record RoundPhaseChanged(RoundPhase From, RoundPhase To, int RoundNumber)`, `sealed record RoundPrepared(PreparationContext Context)`, `sealed record MapStarted(string MapName)`, `sealed record WarmupForcedEnd(string MapName)`.
  - `sealed class RoundTracker(IEventBus bus, PreparationPipeline pipeline, RoundState initial)` avec `RoundState State { get; }`, `void Handle(RoundSignal signal)`, `void Reset(RoundState state)`, `static RoundState InitialStateFor(bool isWarmup)`. En entrant en `Preparing`, il exécute le pipeline, publie `RoundPrepared`, puis applique `PreparationCompleted`. Chaque changement d'état publie `RoundPhaseChanged`.

- [ ] **Step 1: Écrire les tests qui échouent**

`tests/RetakeV4.Domain.Tests/Rounds/PreparationPipelineTests.cs`
```csharp
using RetakeV4.Domain.Modules;
using RetakeV4.Domain.Rounds;

namespace RetakeV4.Domain.Tests.Rounds;

public class PreparationPipelineTests
{
    private sealed class Step(string name, int order, Func<PreparationContext, PreparationContext> run, List<string> log) : IPreparationStep
    {
        public string Name { get; } = name;
        public int Order { get; } = order;

        public PreparationContext Execute(PreparationContext context)
        {
            log.Add(Name);
            return run(context);
        }
    }

    private readonly List<GuardFailure> _failures = new();
    private readonly List<string> _log = new();

    [Fact]
    public void Execute_RunsStepsByOrderThenRegistration()
    {
        var pipeline = new PreparationPipeline(new ModuleGuard(5, _failures.Add));
        pipeline.Register("Spawns", new Step("site", 20, c => c, _log));
        pipeline.Register("RoundTypes", new Step("type", 10, c => c, _log));
        pipeline.Register("Teams", new Step("teams", 20, c => c, _log));
        pipeline.Execute(new PreparationContext(1));
        Assert.Equal(new[] { "type", "site", "teams" }, _log);
    }

    [Fact]
    public void Execute_PassesContextAlongTheChain()
    {
        var pipeline = new PreparationPipeline(new ModuleGuard(5, _failures.Add));
        pipeline.Register("A", new Step("a", 1, c => c with { RoundNumber = c.RoundNumber + 10 }, _log));
        pipeline.Register("B", new Step("b", 2, c => c with { RoundNumber = c.RoundNumber * 2 }, _log));
        Assert.Equal(22, pipeline.Execute(new PreparationContext(1)).RoundNumber);
    }

    [Fact]
    public void FailingStep_IsSkipped_AndReportedAgainstItsModule()
    {
        var pipeline = new PreparationPipeline(new ModuleGuard(5, _failures.Add));
        pipeline.Register("Broken", new Step("broken", 1, _ => throw new InvalidOperationException(), _log));
        pipeline.Register("Ok", new Step("ok", 2, c => c with { RoundNumber = 99 }, _log));
        Assert.Equal(99, pipeline.Execute(new PreparationContext(1)).RoundNumber);
        var failure = Assert.Single(_failures);
        Assert.Equal("Broken", failure.Module);
        Assert.Equal("prepare:broken", failure.Stage);
    }

    [Fact]
    public void DisposedRegistration_IsRemoved()
    {
        var pipeline = new PreparationPipeline(new ModuleGuard(5, _failures.Add));
        var registration = pipeline.Register("A", new Step("a", 1, c => c, _log));
        registration.Dispose();
        pipeline.Execute(new PreparationContext(1));
        Assert.Empty(_log);
    }
}
```

`tests/RetakeV4.Domain.Tests/Rounds/RoundTrackerTests.cs`
```csharp
using RetakeV4.Domain.Events;
using RetakeV4.Domain.Modules;
using RetakeV4.Domain.Rounds;

namespace RetakeV4.Domain.Tests.Rounds;

public class RoundTrackerTests
{
    private readonly EventBus _bus = new(_ => { });
    private readonly List<object> _events = new();
    private readonly PreparationPipeline _pipeline = new(new ModuleGuard(5, _ => { }));

    public RoundTrackerTests()
    {
        _bus.Subscribe<RoundPhaseChanged>("test", _events.Add);
        _bus.Subscribe<RoundPrepared>("test", _events.Add);
    }

    [Fact]
    public void RoundStart_PreparesThenEntersFreezeTime()
    {
        var tracker = new RoundTracker(_bus, _pipeline, new RoundState(RoundPhase.PostRound, 3));
        tracker.Handle(RoundSignal.RoundStarted);
        Assert.Equal(new RoundState(RoundPhase.FreezeTime, 4), tracker.State);
        Assert.Equal(new object[]
        {
            new RoundPhaseChanged(RoundPhase.PostRound, RoundPhase.Preparing, 4),
            new RoundPrepared(new PreparationContext(4)),
            new RoundPhaseChanged(RoundPhase.Preparing, RoundPhase.FreezeTime, 4),
        }, _events);
    }

    [Fact]
    public void IgnoredSignal_PublishesNothing()
    {
        var tracker = new RoundTracker(_bus, _pipeline, RoundState.Initial);
        tracker.Handle(RoundSignal.FreezeEnded);
        Assert.Empty(_events);
        Assert.Equal(RoundState.Initial, tracker.State);
    }

    [Fact]
    public void RoundStartedDuringLive_StartsNewPreparation()
    {
        var tracker = new RoundTracker(_bus, _pipeline, new RoundState(RoundPhase.Live, 5));
        tracker.Handle(RoundSignal.RoundStarted);
        Assert.Equal(new RoundState(RoundPhase.FreezeTime, 6), tracker.State);
        Assert.Single(_events.OfType<RoundPrepared>());
    }

    [Fact]
    public void FullRound_EndsInPostRound()
    {
        var tracker = new RoundTracker(_bus, _pipeline, new RoundState(RoundPhase.PostRound, 0));
        tracker.Handle(RoundSignal.RoundStarted);
        tracker.Handle(RoundSignal.FreezeEnded);
        tracker.Handle(RoundSignal.RoundEnded);
        Assert.Equal(new RoundState(RoundPhase.PostRound, 1), tracker.State);
    }

    [Fact]
    public void Reset_ReplacesStateWithoutPublishing()
    {
        var tracker = new RoundTracker(_bus, _pipeline, new RoundState(RoundPhase.Live, 9));
        tracker.Reset(RoundState.Initial);
        Assert.Equal(RoundState.Initial, tracker.State);
        Assert.Empty(_events);
    }

    [Fact]
    public void InitialStateFor_Warmup_IsWarmup() =>
        Assert.Equal(RoundState.Initial, RoundTracker.InitialStateFor(isWarmup: true));

    [Fact]
    public void InitialStateFor_NotWarmup_IsPostRound() =>
        Assert.Equal(new RoundState(RoundPhase.PostRound, 0), RoundTracker.InitialStateFor(isWarmup: false));
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo`
Expected: échec de compilation (`PreparationPipeline`, `RoundTracker`, `RoundPrepared`… introuvables).

- [ ] **Step 3: Implémentation**

`src/RetakeV4.Domain/Rounds/PreparationContext.cs`
```csharp
namespace RetakeV4.Domain.Rounds;

public sealed record PreparationContext(int RoundNumber);
```

`src/RetakeV4.Domain/Rounds/IPreparationStep.cs`
```csharp
namespace RetakeV4.Domain.Rounds;

public interface IPreparationStep
{
    string Name { get; }

    int Order { get; }

    PreparationContext Execute(PreparationContext context);
}
```

`src/RetakeV4.Domain/Rounds/PreparationPipeline.cs`
```csharp
using System.Collections.Immutable;
using RetakeV4.Domain.Modules;

namespace RetakeV4.Domain.Rounds;

public sealed class PreparationPipeline
{
    private readonly ModuleGuard _guard;
    private ImmutableList<Registration> _registrations = ImmutableList<Registration>.Empty;
    private long _sequence;

    public PreparationPipeline(ModuleGuard guard)
    {
        ArgumentNullException.ThrowIfNull(guard);
        _guard = guard;
    }

    public IDisposable Register(string module, IPreparationStep step)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        ArgumentNullException.ThrowIfNull(step);
        var registration = new Registration(module, step, _sequence++);
        _registrations = _registrations.Add(registration);
        return new Unregister(() => _registrations = _registrations.Remove(registration));
    }

    public PreparationContext Execute(PreparationContext initial)
    {
        var ordered = _registrations.OrderBy(r => r.Step.Order).ThenBy(r => r.Sequence);
        var context = initial;
        foreach (var registration in ordered)
        {
            var input = context;
            context = _guard.Run(registration.Module, $"prepare:{registration.Step.Name}", () => registration.Step.Execute(input), input);
        }
        return context;
    }

    private sealed record Registration(string Module, IPreparationStep Step, long Sequence);

    private sealed class Unregister(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
```

`src/RetakeV4.Domain/Events/RoundEvents.cs`
```csharp
using RetakeV4.Domain.Rounds;

namespace RetakeV4.Domain.Events;

public sealed record RoundPhaseChanged(RoundPhase From, RoundPhase To, int RoundNumber);

public sealed record RoundPrepared(PreparationContext Context);

public sealed record MapStarted(string MapName);

public sealed record WarmupForcedEnd(string MapName);
```

`src/RetakeV4.Domain/Rounds/RoundTracker.cs`
```csharp
using RetakeV4.Domain.Events;

namespace RetakeV4.Domain.Rounds;

public sealed class RoundTracker
{
    private readonly IEventBus _bus;
    private readonly PreparationPipeline _pipeline;

    public RoundTracker(IEventBus bus, PreparationPipeline pipeline, RoundState initial)
    {
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(initial);
        _bus = bus;
        _pipeline = pipeline;
        State = initial;
    }

    public RoundState State { get; private set; }

    public static RoundState InitialStateFor(bool isWarmup) =>
        isWarmup ? RoundState.Initial : new RoundState(RoundPhase.PostRound, 0);

    public void Reset(RoundState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        State = state;
    }

    public void Handle(RoundSignal signal)
    {
        var next = RoundStateMachine.Apply(State, signal);
        if (next == State)
        {
            return;
        }
        Transition(next);
        if (next.Phase == RoundPhase.Preparing)
        {
            Prepare(next.RoundNumber);
        }
    }

    private void Prepare(int roundNumber)
    {
        var context = _pipeline.Execute(new PreparationContext(roundNumber));
        _bus.Publish(new RoundPrepared(context));
        Transition(RoundStateMachine.Apply(State, RoundSignal.PreparationCompleted));
    }

    private void Transition(RoundState next)
    {
        var previous = State;
        State = next;
        _bus.Publish(new RoundPhaseChanged(previous.Phase, next.Phase, next.RoundNumber));
    }
}
```

- [ ] **Step 4: Vérifier le succès et la couverture**

Run: `dotnet test tests/RetakeV4.Domain.Tests --nologo /p:CollectCoverage=true /p:Include="[RetakeV4.Domain]*" /p:Threshold=80 /p:ThresholdType=line`
Expected: `Failed: 0` et un tableau coverlet avec `RetakeV4.Domain` ≥ 80 % de lignes (pas d'erreur de seuil).

- [ ] **Step 5: Commit**

```bash
git add src/RetakeV4.Domain/Rounds src/RetakeV4.Domain/Events/RoundEvents.cs tests/RetakeV4.Domain.Tests/Rounds
git commit -m "feat: pipeline de préparation et suivi des rounds (Domain)"
```

---

### Task 9: Projet plugin et store de configuration JSON

**Files:**
- Create: `src/RetakeV4/RetakeV4.csproj`
- Create: `src/RetakeV4/Configuration/ModuleConfig.cs`, `ConfigIssue.cs`, `IConfigValidator.cs`, `JsonConfigStore.cs`, `ConfigLogging.cs`
- Create: `tests/RetakeV4.Integration.Tests/RetakeV4.Integration.Tests.csproj`, `tests/RetakeV4.Integration.Tests/TempDirectory.cs`
- Test: `tests/RetakeV4.Integration.Tests/Configuration/JsonConfigStoreTests.cs`

**Interfaces:**
- Produces:
  - `abstract record ModuleConfig { int Version { get; init; } bool Enabled { get; init; } = true; bool Debug { get; init; } }`
  - `sealed record ConfigIssue(string File, string Key, string Message)`
  - `sealed record ValidationResult<T>(T Config, IReadOnlyList<ConfigIssue> Issues)`
  - `interface IConfigValidator<T> where T : ModuleConfig { ValidationResult<T> Validate(T config, T defaults, string file); }`
  - `sealed record ConfigLoadResult<T>(T Config, IReadOnlyList<ConfigIssue> Issues, bool CreatedDefault) where T : ModuleConfig`
  - `sealed class JsonConfigStore(string directory)` avec `ConfigLoadResult<T> Load<T>(string fileName, T defaults, IConfigValidator<T>? validator = null) where T : ModuleConfig` et `static JsonSerializerOptions SerializerOptions`
  - `static class ConfigLogging { void Report(ILogger logger, IEnumerable<ConfigIssue> issues); }`
- Règles : fichier absent → écrit les valeurs par défaut ; JSON invalide ou enum inconnue → valeurs par défaut + problème, **fichier intact** ; `Version` plus ancienne → problème ; clés absentes → valeurs par défaut de la classe.

- [ ] **Step 1: Projets**

`src/RetakeV4/RetakeV4.csproj`
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>RetakeV4</RootNamespace>
    <AssemblyName>RetakeV4</AssemblyName>
    <Version>4.0.0-alpha.1</Version>
    <GenerateRuntimeConfigurationFiles>true</GenerateRuntimeConfigurationFiles>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="CounterStrikeSharp.API" Version="1.0.370">
      <ExcludeAssets>runtime</ExcludeAssets>
    </PackageReference>
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\RetakeV4.Domain\RetakeV4.Domain.csproj" />
  </ItemGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="RetakeV4.Integration.Tests" />
  </ItemGroup>
</Project>
```

`tests/RetakeV4.Integration.Tests/RetakeV4.Integration.Tests.csproj`
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="CounterStrikeSharp.API" Version="1.0.370" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\RetakeV4\RetakeV4.csproj" />
  </ItemGroup>
</Project>
```

`tests/RetakeV4.Integration.Tests/TempDirectory.cs`
```csharp
namespace RetakeV4.Integration.Tests;

public sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "retakev4-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
```

Ajouter les deux projets à la solution :
```bash
dotnet sln RetakeV4.sln add src/RetakeV4/RetakeV4.csproj tests/RetakeV4.Integration.Tests/RetakeV4.Integration.Tests.csproj
```

- [ ] **Step 2: Écrire les tests qui échouent**

`tests/RetakeV4.Integration.Tests/Configuration/JsonConfigStoreTests.cs`
```csharp
using RetakeV4.Configuration;

namespace RetakeV4.Integration.Tests.Configuration;

public enum SampleMode
{
    Alpha,
    Beta,
}

public sealed record SampleConfig : ModuleConfig
{
    public SampleConfig() => Version = 2;

    public int Count { get; init; } = 3;

    public SampleMode Mode { get; init; } = SampleMode.Alpha;
}

public sealed class NonNegativeCountValidator : IConfigValidator<SampleConfig>
{
    public ValidationResult<SampleConfig> Validate(SampleConfig config, SampleConfig defaults, string file) =>
        config.Count >= 0
            ? new ValidationResult<SampleConfig>(config, Array.Empty<ConfigIssue>())
            : new ValidationResult<SampleConfig>(
                config with { Count = defaults.Count },
                new[] { new ConfigIssue(file, nameof(SampleConfig.Count), "must be >= 0; using default") });
}

public sealed class JsonConfigStoreTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly JsonConfigStore _store;

    public JsonConfigStoreTests() => _store = new JsonConfigStore(_dir.Path);

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void MissingFile_WritesDefaults()
    {
        var result = _store.Load("sample.json", new SampleConfig());
        Assert.True(result.CreatedDefault);
        Assert.Empty(result.Issues);
        var written = File.ReadAllText(_dir.File("sample.json"));
        Assert.Contains("\"Count\": 3", written);
        Assert.Contains("\"Mode\": \"Alpha\"", written);
    }

    [Fact]
    public void PartialFile_UsesDefaultsForMissingKeys()
    {
        File.WriteAllText(_dir.File("sample.json"), """{ "Version": 2, "Count": 7 }""");
        var result = _store.Load("sample.json", new SampleConfig());
        Assert.Equal(7, result.Config.Count);
        Assert.Equal(SampleMode.Alpha, result.Config.Mode);
        Assert.True(result.Config.Enabled);
        Assert.Empty(result.Issues);
        Assert.False(result.CreatedDefault);
    }

    [Fact]
    public void MalformedJson_UsesDefaults_AndLeavesFileUntouched()
    {
        const string broken = """{ "Count": """;
        File.WriteAllText(_dir.File("sample.json"), broken);
        var result = _store.Load("sample.json", new SampleConfig());
        Assert.Equal(3, result.Config.Count);
        Assert.Single(result.Issues);
        Assert.Equal(broken, File.ReadAllText(_dir.File("sample.json")));
    }

    [Fact]
    public void UnknownEnumValue_UsesDefaults_AndLeavesFileUntouched()
    {
        const string content = """{ "Version": 2, "Mode": "Gamma" }""";
        File.WriteAllText(_dir.File("sample.json"), content);
        var result = _store.Load("sample.json", new SampleConfig());
        Assert.Equal(SampleMode.Alpha, result.Config.Mode);
        var issue = Assert.Single(result.Issues);
        Assert.Contains("Mode", issue.Key);
        Assert.Equal(content, File.ReadAllText(_dir.File("sample.json")));
    }

    [Fact]
    public void CommentsAndTrailingCommas_AreAccepted()
    {
        File.WriteAllText(_dir.File("sample.json"), "{\n // comment\n \"Version\": 2, \"Mode\": \"Beta\",\n}");
        var result = _store.Load("sample.json", new SampleConfig());
        Assert.Equal(SampleMode.Beta, result.Config.Mode);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void OlderVersion_IsReported()
    {
        File.WriteAllText(_dir.File("sample.json"), """{ "Version": 1 }""");
        var result = _store.Load("sample.json", new SampleConfig());
        var issue = Assert.Single(result.Issues);
        Assert.Equal("Version", issue.Key);
    }

    [Fact]
    public void NullDocument_UsesDefaults()
    {
        File.WriteAllText(_dir.File("sample.json"), "null");
        var result = _store.Load("sample.json", new SampleConfig());
        Assert.Equal(3, result.Config.Count);
        Assert.Single(result.Issues);
    }

    [Fact]
    public void Validator_SanitizesAndReports()
    {
        File.WriteAllText(_dir.File("sample.json"), """{ "Version": 2, "Count": -4 }""");
        var result = _store.Load("sample.json", new SampleConfig(), new NonNegativeCountValidator());
        Assert.Equal(3, result.Config.Count);
        Assert.Equal("Count", Assert.Single(result.Issues).Key);
    }
}
```

- [ ] **Step 3: Vérifier l'échec**

Run: `dotnet test tests/RetakeV4.Integration.Tests --nologo`
Expected: échec de compilation (`RetakeV4.Configuration` introuvable).

- [ ] **Step 4: Implémentation**

`src/RetakeV4/Configuration/ModuleConfig.cs`
```csharp
namespace RetakeV4.Configuration;

public abstract record ModuleConfig
{
    public int Version { get; init; }

    public bool Enabled { get; init; } = true;

    public bool Debug { get; init; }
}
```

`src/RetakeV4/Configuration/ConfigIssue.cs`
```csharp
namespace RetakeV4.Configuration;

public sealed record ConfigIssue(string File, string Key, string Message);

public sealed record ValidationResult<T>(T Config, IReadOnlyList<ConfigIssue> Issues);

public sealed record ConfigLoadResult<T>(T Config, IReadOnlyList<ConfigIssue> Issues, bool CreatedDefault)
    where T : ModuleConfig;
```

`src/RetakeV4/Configuration/IConfigValidator.cs`
```csharp
namespace RetakeV4.Configuration;

public interface IConfigValidator<T> where T : ModuleConfig
{
    ValidationResult<T> Validate(T config, T defaults, string file);
}
```

`src/RetakeV4/Configuration/JsonConfigStore.cs`
```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RetakeV4.Configuration;

public sealed class JsonConfigStore
{
    private readonly string _directory;

    public JsonConfigStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    public static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public ConfigLoadResult<T> Load<T>(string fileName, T defaults, IConfigValidator<T>? validator = null)
        where T : ModuleConfig
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(defaults);
        var path = Path.Combine(_directory, fileName);
        if (!File.Exists(path))
        {
            Write(path, defaults);
            return new ConfigLoadResult<T>(defaults, Array.Empty<ConfigIssue>(), true);
        }

        var (config, readIssues) = Read(path, fileName, defaults);
        var versionIssues = config.Version < defaults.Version
            ? new[] { new ConfigIssue(fileName, nameof(ModuleConfig.Version), $"config version {config.Version} is older than {defaults.Version}; missing keys use defaults") }
            : Array.Empty<ConfigIssue>();
        var validated = validator?.Validate(config, defaults, fileName)
            ?? new ValidationResult<T>(config, Array.Empty<ConfigIssue>());
        var issues = readIssues.Concat(versionIssues).Concat(validated.Issues).ToList();
        return new ConfigLoadResult<T>(validated.Config, issues, false);
    }

    private static (T Config, IReadOnlyList<ConfigIssue> Issues) Read<T>(string path, string fileName, T defaults)
        where T : ModuleConfig
    {
        try
        {
            var config = JsonSerializer.Deserialize<T>(File.ReadAllText(path), SerializerOptions);
            return config is null
                ? (defaults, new[] { new ConfigIssue(fileName, "$", "document is null; using defaults") })
                : (config, Array.Empty<ConfigIssue>());
        }
        catch (JsonException ex)
        {
            var issue = new ConfigIssue(fileName, ex.Path ?? "$", $"invalid JSON ({ex.Message}); using defaults, file left untouched");
            return (defaults, new[] { issue });
        }
    }

    private static void Write<T>(string path, T config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(config, SerializerOptions));
    }
}
```

`src/RetakeV4/Configuration/ConfigLogging.cs`
```csharp
using Microsoft.Extensions.Logging;

namespace RetakeV4.Configuration;

public static class ConfigLogging
{
    public static void Report(ILogger logger, IEnumerable<ConfigIssue> issues)
    {
        foreach (var issue in issues)
        {
            logger.LogWarning("Config {File} [{Key}]: {Message}", issue.File, issue.Key, issue.Message);
        }
    }
}
```

- [ ] **Step 5: Vérifier le succès**

Run: `dotnet test tests/RetakeV4.Integration.Tests --nologo`
Expected: `Failed: 0`. Si `UnknownEnumValue` échoue parce que `issue.Key` vaut `$.Mode`, le test passe quand même (`Contains("Mode")`).

- [ ] **Step 6: Commit**

```bash
git add RetakeV4.sln src/RetakeV4/RetakeV4.csproj src/RetakeV4/Configuration tests/RetakeV4.Integration.Tests
git commit -m "feat: store de configuration JSON par module"
```

---

### Task 10: Localisation (lang/ + TextService)

**Files:**
- Create: `src/RetakeV4/lang/en.json`, `src/RetakeV4/lang/fr.json`
- Create: `src/RetakeV4/Localization/ITextService.cs`, `src/RetakeV4/Localization/TextService.cs`
- Modify: `src/RetakeV4/RetakeV4.csproj` (copie de `lang/` en sortie)
- Test: `tests/RetakeV4.Integration.Tests/Localization/LangFilesTests.cs`

**Interfaces:**
- Produces:
  - `interface ITextService { string Server(string key, params object[] args); string For(CCSPlayerController player, string key, params object[] args); void Chat(CCSPlayerController player, string key, params object[] args); void ChatAll(string key, params object[] args); }`
  - `sealed class TextService(IStringLocalizer localizer) : ITextService` — langue par joueur via `LocalizerExtensions.ForPlayer`, balises de couleur via `StringExtensions.ReplaceColorTags`, préfixe `core.prefix` en chat.
  - Clés phase 1 : `core.prefix`, `core.info.version`, `core.warmup.forced_end`.

- [ ] **Step 1: Écrire le test qui échoue**

`tests/RetakeV4.Integration.Tests/Localization/LangFilesTests.cs`
```csharp
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RetakeV4.Integration.Tests.Localization;

public partial class LangFilesTests
{
    private static readonly string LangDirectory = Path.Combine(AppContext.BaseDirectory, "lang");

    [GeneratedRegex(@"^[a-z]+(\.[a-z0-9_]+)+$")]
    private static partial Regex KeyPattern();

    [GeneratedRegex(@"\{(\d+)\}")]
    private static partial Regex PlaceholderPattern();

    private static Dictionary<string, string> Load(string language) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(LangDirectory, $"{language}.json")))
        ?? throw new InvalidDataException($"{language}.json is empty");

    [Fact]
    public void EnglishAndFrench_HaveTheSameKeys()
    {
        Assert.Equal(Load("en").Keys.Order(), Load("fr").Keys.Order());
    }

    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    public void Keys_FollowModuleSectionKeyConvention(string language)
    {
        Assert.All(Load(language).Keys, key => Assert.Matches(KeyPattern(), key));
    }

    [Fact]
    public void Placeholders_MatchAcrossLanguages()
    {
        var en = Load("en");
        var fr = Load("fr");
        foreach (var key in en.Keys)
        {
            var expected = PlaceholderPattern().Matches(en[key]).Select(m => m.Value).Order();
            var actual = PlaceholderPattern().Matches(fr[key]).Select(m => m.Value).Order();
            Assert.True(expected.SequenceEqual(actual), $"placeholder mismatch for '{key}'");
        }
    }

    [Fact]
    public void Phase1Keys_ArePresent()
    {
        var en = Load("en");
        Assert.Contains("core.prefix", en.Keys);
        Assert.Contains("core.info.version", en.Keys);
        Assert.Contains("core.warmup.forced_end", en.Keys);
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test tests/RetakeV4.Integration.Tests --nologo --filter "FullyQualifiedName~LangFilesTests"`
Expected: FAIL avec `FileNotFoundException` / `DirectoryNotFoundException` sur `lang/en.json`.

- [ ] **Step 3: Implémentation**

`src/RetakeV4/lang/en.json`
```json
{
  "core.prefix": "{lightblue}[Retake]{default}",
  "core.info.version": "{0} v{1} by {2}",
  "core.warmup.forced_end": "Warmup is over, the retake starts!"
}
```

`src/RetakeV4/lang/fr.json`
```json
{
  "core.prefix": "{lightblue}[Retake]{default}",
  "core.info.version": "{0} v{1} par {2}",
  "core.warmup.forced_end": "Fin du warmup, le retake commence !"
}
```

Dans `src/RetakeV4/RetakeV4.csproj`, ajouter :
```xml
  <ItemGroup>
    <None Include="lang\*.json" CopyToOutputDirectory="PreserveNewest"/>
  </ItemGroup>
```

`src/RetakeV4/Localization/ITextService.cs`
```csharp
using CounterStrikeSharp.API.Core;

namespace RetakeV4.Localization;

public interface ITextService
{
    string Server(string key, params object[] args);

    string For(CCSPlayerController player, string key, params object[] args);

    void Chat(CCSPlayerController player, string key, params object[] args);

    void ChatAll(string key, params object[] args);
}
```

`src/RetakeV4/Localization/TextService.cs`
```csharp
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using Microsoft.Extensions.Localization;

namespace RetakeV4.Localization;

public sealed class TextService : ITextService
{
    private readonly IStringLocalizer _localizer;

    public TextService(IStringLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        _localizer = localizer;
    }

    public string Server(string key, params object[] args) =>
        StringExtensions.ReplaceColorTags(_localizer[key, args].Value);

    public string For(CCSPlayerController player, string key, params object[] args) =>
        StringExtensions.ReplaceColorTags(_localizer.ForPlayer(player, key, args));

    public void Chat(CCSPlayerController player, string key, params object[] args) =>
        player.PrintToChat($" {For(player, "core.prefix")} {For(player, key, args)}");

    public void ChatAll(string key, params object[] args)
    {
        foreach (var player in Utilities.GetPlayers().Where(p => p is { IsValid: true, IsBot: false }))
        {
            Chat(player, key, args);
        }
    }
}
```

- [ ] **Step 4: Vérifier le succès**

Run: `dotnet test tests/RetakeV4.Integration.Tests --nologo`
Expected: `Failed: 0`. Si `ReplaceColorTags` n'est pas statique-appelable tel quel (signature différente), l'appeler en méthode d'extension (`value.ReplaceColorTags()`) : la réflexion l'a listé dans `StringExtensions` comme `String ReplaceColorTags(String)`.

- [ ] **Step 5: Commit**

```bash
git add src/RetakeV4/lang src/RetakeV4/Localization src/RetakeV4/RetakeV4.csproj tests/RetakeV4.Integration.Tests/Localization
git commit -m "feat: localisation lang/ en+fr et TextService"
```

---

### Task 11: Contrat de module, hôte de modules et bootstrap du plugin

**Files:**
- Create: `src/RetakeV4/Modules/IRetakeModule.cs`, `src/RetakeV4/Modules/ModuleContext.cs`, `src/RetakeV4/Modules/ModuleHost.cs`
- Create: `src/RetakeV4/RetakeV4Plugin.cs`
- Test: `tests/RetakeV4.Integration.Tests/Modules/ModuleHostTests.cs`, `tests/RetakeV4.Integration.Tests/ListLogger.cs`

**Interfaces:**
- Consumes: `JsonConfigStore`, `ModuleConfig`, `ConfigLogging` (Task 9) ; `ITextService`, `TextService` (Task 10) ; `IEventBus`, `EventBus`, `RoundPhaseChanged` (Tasks 7-8) ; `ModuleGuard`, `ModuleLoadPlanner`, `ModuleDescriptor` (Task 7) ; `PreparationPipeline`, `RoundTracker`, `RoundState`, `RoundPhase` (Tasks 5, 8).
- Produces:
  - `interface IRetakeModule { string Name { get; } IReadOnlyList<string> DependsOn { get; } ModuleConfig LoadConfig(JsonConfigStore store, ILogger logger); void Load(ModuleContext context); void Unload(); }`
  - `sealed record ModuleContext(BasePlugin Plugin, IEventBus Bus, ModuleGuard Guard, ITextService Text, ILogger Logger, PreparationPipeline Preparation, RoundTracker Rounds, bool HotReload)`
  - `sealed class ModuleHost(IReadOnlyList<IRetakeModule> modules, ILogger logger)` avec `void Start(JsonConfigStore store, Func<IRetakeModule, ModuleContext> contextFor)`, `void Stop()`, `IReadOnlyList<string> LoadedModules`.
  - `RetakeV4Plugin : BasePlugin` (`ModuleName = "RetakeV4"`, `ModuleVersion = "4.0.0-alpha.1"`, `ModuleAuthor = "NeuTroNBZh"`), avec des configs dans `addons/counterstrikesharp/configs/plugins/RetakeV4/`.

- [ ] **Step 1: Écrire les tests qui échouent**

`tests/RetakeV4.Integration.Tests/ListLogger.cs`
```csharp
using Microsoft.Extensions.Logging;

namespace RetakeV4.Integration.Tests;

public sealed class ListLogger : ILogger
{
    public List<(LogLevel Level, string Message)> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, formatter(state, exception)));
}
```

`tests/RetakeV4.Integration.Tests/Modules/ModuleHostTests.cs`
```csharp
using Microsoft.Extensions.Logging;
using RetakeV4.Configuration;
using RetakeV4.Modules;

namespace RetakeV4.Integration.Tests.Modules;

public sealed class ModuleHostTests : IDisposable
{
    private sealed record FakeConfig : ModuleConfig;

    private sealed class FakeModule(string name, List<string> journal, bool enabled = true, bool throwOnLoad = false, bool throwOnConfig = false, params string[] deps) : IRetakeModule
    {
        public string Name { get; } = name;
        public IReadOnlyList<string> DependsOn { get; } = deps;

        public ModuleConfig LoadConfig(JsonConfigStore store, ILogger logger) =>
            throwOnConfig ? throw new InvalidOperationException("config") : new FakeConfig { Enabled = enabled };

        public void Load(ModuleContext context)
        {
            if (throwOnLoad)
            {
                throw new InvalidOperationException("load");
            }
            journal.Add($"load:{Name}");
        }

        public void Unload() => journal.Add($"unload:{Name}");
    }

    private readonly TempDirectory _dir = new();
    private readonly List<string> _journal = new();
    private readonly ListLogger _logger = new();

    public void Dispose() => _dir.Dispose();

    private ModuleHost Start(params IRetakeModule[] modules)
    {
        var host = new ModuleHost(modules, _logger);
        host.Start(new JsonConfigStore(_dir.Path), _ => null!);
        return host;
    }

    [Fact]
    public void Start_LoadsInDependencyOrder()
    {
        var host = Start(new FakeModule("Hud", _journal, deps: new[] { "Core" }), new FakeModule("Core", _journal));
        Assert.Equal(new[] { "load:Core", "load:Hud" }, _journal);
        Assert.Equal(new[] { "Core", "Hud" }, host.LoadedModules);
    }

    [Fact]
    public void DisabledModule_IsNotLoaded_AndIsLogged()
    {
        var host = Start(new FakeModule("Core", _journal), new FakeModule("Links", _journal, enabled: false));
        Assert.Equal(new[] { "Core" }, host.LoadedModules);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("Links"));
    }

    [Fact]
    public void ModuleFailingToLoad_IsUnloaded_AndDependentsAreSkipped()
    {
        var host = Start(new FakeModule("Core", _journal, throwOnLoad: true), new FakeModule("Hud", _journal, deps: new[] { "Core" }));
        Assert.Empty(host.LoadedModules);
        Assert.Contains("unload:Core", _journal);
        Assert.DoesNotContain("load:Hud", _journal);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("Core"));
    }

    [Fact]
    public void ConfigFailure_DisablesTheModule()
    {
        var host = Start(new FakeModule("Core", _journal, throwOnConfig: true));
        Assert.Empty(host.LoadedModules);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public void Stop_UnloadsInReverseOrder()
    {
        var host = Start(new FakeModule("Core", _journal), new FakeModule("Hud", _journal, deps: new[] { "Core" }));
        _journal.Clear();
        host.Stop();
        Assert.Equal(new[] { "unload:Hud", "unload:Core" }, _journal);
        Assert.Empty(host.LoadedModules);
    }
}
```

Le `null!` passé comme `ModuleContext` est volontaire : les faux modules n'utilisent pas le contexte.

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test tests/RetakeV4.Integration.Tests --nologo --filter "FullyQualifiedName~ModuleHostTests"`
Expected: échec de compilation (`RetakeV4.Modules` introuvable).

- [ ] **Step 3: Implémentation du contrat et de l'hôte**

`src/RetakeV4/Modules/IRetakeModule.cs`
```csharp
using Microsoft.Extensions.Logging;
using RetakeV4.Configuration;

namespace RetakeV4.Modules;

public interface IRetakeModule
{
    string Name { get; }

    IReadOnlyList<string> DependsOn { get; }

    ModuleConfig LoadConfig(JsonConfigStore store, ILogger logger);

    void Load(ModuleContext context);

    void Unload();
}
```

`src/RetakeV4/Modules/ModuleContext.cs`
```csharp
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.Modules;
using RetakeV4.Domain.Rounds;
using RetakeV4.Localization;

namespace RetakeV4.Modules;

public sealed record ModuleContext(
    BasePlugin Plugin,
    IEventBus Bus,
    ModuleGuard Guard,
    ITextService Text,
    ILogger Logger,
    PreparationPipeline Preparation,
    RoundTracker Rounds,
    bool HotReload);
```

`src/RetakeV4/Modules/ModuleHost.cs`
```csharp
using Microsoft.Extensions.Logging;
using RetakeV4.Configuration;
using RetakeV4.Domain.Modules;

namespace RetakeV4.Modules;

public sealed class ModuleHost
{
    private readonly IReadOnlyList<IRetakeModule> _modules;
    private readonly ILogger _logger;
    private readonly List<IRetakeModule> _loaded = new();

    public ModuleHost(IReadOnlyList<IRetakeModule> modules, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(logger);
        _modules = modules;
        _logger = logger;
    }

    public IReadOnlyList<string> LoadedModules => _loaded.Select(m => m.Name).ToList();

    public void Start(JsonConfigStore store, Func<IRetakeModule, ModuleContext> contextFor)
    {
        var plan = ModuleLoadPlanner.Plan(_modules.Select(m => Describe(m, store)).ToList());
        foreach (var skipped in plan.Skipped)
        {
            _logger.LogWarning("Module {Module} not loaded: {Reason} ({Detail})", skipped.Name, skipped.Reason, skipped.Detail);
        }
        foreach (var name in plan.Order)
        {
            TryLoad(_modules.First(m => m.Name == name), contextFor);
        }
    }

    public void Stop()
    {
        foreach (var module in Enumerable.Reverse(_loaded).ToList())
        {
            SafeUnload(module);
        }
        _loaded.Clear();
    }

    private ModuleDescriptor Describe(IRetakeModule module, JsonConfigStore store)
    {
        try
        {
            var config = module.LoadConfig(store, _logger);
            return new ModuleDescriptor(module.Name, module.DependsOn, config.Enabled);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Module {Module}: config could not be loaded, module disabled", module.Name);
            return new ModuleDescriptor(module.Name, module.DependsOn, false);
        }
    }

    private void TryLoad(IRetakeModule module, Func<IRetakeModule, ModuleContext> contextFor)
    {
        var failedDependency = module.DependsOn.FirstOrDefault(d => _loaded.All(l => l.Name != d));
        if (failedDependency is not null)
        {
            _logger.LogWarning("Module {Module} not loaded: dependency {Dependency} failed to load", module.Name, failedDependency);
            return;
        }
        try
        {
            module.Load(contextFor(module));
            _loaded.Add(module);
            _logger.LogInformation("Module {Module} loaded", module.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Module {Module} failed to load and is disabled", module.Name);
            SafeUnload(module);
        }
    }

    private void SafeUnload(IRetakeModule module)
    {
        try
        {
            module.Unload();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Module {Module} failed to unload", module.Name);
        }
    }
}
```

- [ ] **Step 4: Vérifier le succès des tests**

Run: `dotnet test tests/RetakeV4.Integration.Tests --nologo --filter "FullyQualifiedName~ModuleHostTests"`
Expected: `Failed: 0`.

- [ ] **Step 5: Bootstrap du plugin (sans module pour l'instant)**

`src/RetakeV4/RetakeV4Plugin.cs`
```csharp
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using Microsoft.Extensions.Logging;
using RetakeV4.Configuration;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.Modules;
using RetakeV4.Domain.Rounds;
using RetakeV4.Localization;
using RetakeV4.Modules;

namespace RetakeV4;

[MinimumApiVersion(370)]
public sealed class RetakeV4Plugin : BasePlugin
{
    private const int MaxErrorsPerRound = 5;

    private ModuleHost? _host;
    private IDisposable? _roundResetSubscription;

    public override string ModuleName => "RetakeV4";
    public override string ModuleVersion => "4.0.0-alpha.1";
    public override string ModuleAuthor => "NeuTroNBZh";
    public override string ModuleDescription => "Modular CS2 retake plugin";

    public override void Load(bool hotReload)
    {
        var bus = new EventBus(OnBusError);
        var guard = new ModuleGuard(MaxErrorsPerRound, OnGuardFailure);
        var pipeline = new PreparationPipeline(guard);
        var rounds = new RoundTracker(bus, pipeline, RoundState.Initial);
        var text = new TextService(Localizer);
        _roundResetSubscription = bus.Subscribe<RoundPhaseChanged>("bootstrap", e =>
        {
            if (e.To == RoundPhase.PostRound)
            {
                guard.ResetRound();
            }
        });

        _host = new ModuleHost(CreateModules(), Logger);
        _host.Start(new JsonConfigStore(ConfigDirectory()), _ =>
            new ModuleContext(this, bus, guard, text, Logger, pipeline, rounds, hotReload));
        Logger.LogInformation("RetakeV4 {Version} loaded with modules: {Modules}", ModuleVersion, string.Join(", ", _host.LoadedModules));
    }

    public override void Unload(bool hotReload)
    {
        _host?.Stop();
        _host = null;
        _roundResetSubscription?.Dispose();
        _roundResetSubscription = null;
    }

    private static IReadOnlyList<IRetakeModule> CreateModules() => Array.Empty<IRetakeModule>();

    private string ConfigDirectory() =>
        Path.GetFullPath(Path.Combine(ModuleDirectory, "..", "..", "configs", "plugins", "RetakeV4"));

    private void OnBusError(BusError error) =>
        Logger.LogError(error.Exception, "Event {Event} handler of {Subscriber} failed", error.EventType.Name, error.Subscriber);

    private void OnGuardFailure(GuardFailure failure)
    {
        Logger.LogError(failure.Exception, "Module {Module} failed during {Stage}", failure.Module, failure.Stage);
        if (failure.ModuleDisabled)
        {
            Logger.LogError("Module {Module} disabled until next reload (too many errors this round)", failure.Module);
        }
    }
}
```

- [ ] **Step 6: Build complet sans warning**

Run: `dotnet build RetakeV4.sln -c Release --nologo`
Expected: `0 Avertissement(s)`, `0 Erreur(s)`.

- [ ] **Step 7: Commit**

```bash
git add src/RetakeV4/Modules src/RetakeV4/RetakeV4Plugin.cs tests/RetakeV4.Integration.Tests/Modules tests/RetakeV4.Integration.Tests/ListLogger.cs
git commit -m "feat: contrat de module, hôte de modules et bootstrap du plugin"
```

---

### Task 12: Module Core (cycle des rounds, cvars, warmup, info)

**Files:**
- Create: `src/RetakeV4/Modules/Core/CoreConfig.cs`, `CoreConfigValidator.cs`, `GameRulesAccessor.cs`, `CoreModule.cs`
- Create: `src/RetakeV4/cfg/RetakeV4/retake.cfg`
- Modify: `src/RetakeV4/RetakeV4Plugin.cs` (`CreateModules` renvoie `CoreModule`)
- Test: `tests/RetakeV4.Integration.Tests/Modules/Core/CoreConfigValidatorTests.cs`

**Interfaces:**
- Consumes: `ModuleConfig`, `IConfigValidator<T>`, `ValidationResult<T>`, `ConfigIssue`, `JsonConfigStore`, `ConfigLogging` (Task 9) ; `IRetakeModule`, `ModuleContext` (Task 11) ; `RoundSignal`, `RoundTracker`, `WarmupTracker`, `WarmupSnapshot` (Tasks 5, 6, 8) ; `MapStarted`, `WarmupForcedEnd`, `RoundPhaseChanged` (Task 8) ; `ITextService` (Task 10).
- Produces:
  - `sealed record CoreConfig : ModuleConfig { string ExecConfig = "RetakeV4/retake.cfg"; float WarmupFallbackSeconds = 16f; float WatchdogIntervalSeconds = 0.25f; }` (Version 1), dans le fichier `core.json`.
  - `sealed class CoreConfigValidator : IConfigValidator<CoreConfig>`
  - `CoreModule : IRetakeModule` (Name `"Core"`, sans dépendance) — traduit `round_start` / `round_freeze_end` / `round_end` en signaux ; exécute `ExecConfig` au démarrage de la map ; fait tourner le watchdog de warmup ; fournit la commande `css_retake_info` ; exécute `mp_restartgame 1` au hot reload ; journalise les changements de phase si `Debug`.

- [ ] **Step 1: Écrire les tests du validateur**

`tests/RetakeV4.Integration.Tests/Modules/Core/CoreConfigValidatorTests.cs`
```csharp
using RetakeV4.Modules.Core;

namespace RetakeV4.Integration.Tests.Modules.Core;

public class CoreConfigValidatorTests
{
    private static readonly CoreConfig Defaults = new();
    private readonly CoreConfigValidator _validator = new();

    [Fact]
    public void Defaults_AreValid()
    {
        var result = _validator.Validate(Defaults, Defaults, "core.json");
        Assert.Empty(result.Issues);
        Assert.Equal(Defaults, result.Config);
    }

    [Theory]
    [InlineData("../../server.cfg")]
    [InlineData("RetakeV4/retake.cfg; quit")]
    [InlineData("C:/evil.cfg")]
    [InlineData("/etc/passwd")]
    [InlineData("")]
    [InlineData("  ")]
    public void UnsafeExecConfig_FallsBackToDefault(string path)
    {
        var result = _validator.Validate(Defaults with { ExecConfig = path }, Defaults, "core.json");
        Assert.Equal(Defaults.ExecConfig, result.Config.ExecConfig);
        Assert.Contains(result.Issues, i => i.Key == nameof(CoreConfig.ExecConfig));
    }

    [Theory]
    [InlineData("RetakeV4/retake.cfg")]
    [InlineData("custom_retake.cfg")]
    [InlineData("my-server/retake_5v5.cfg")]
    public void SafeExecConfig_IsKept(string path)
    {
        var result = _validator.Validate(Defaults with { ExecConfig = path }, Defaults, "core.json");
        Assert.Equal(path, result.Config.ExecConfig);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void NegativeFallback_FallsBackToDefault()
    {
        var result = _validator.Validate(Defaults with { WarmupFallbackSeconds = -3f }, Defaults, "core.json");
        Assert.Equal(Defaults.WarmupFallbackSeconds, result.Config.WarmupFallbackSeconds);
        Assert.Single(result.Issues);
    }

    [Theory]
    [InlineData(0.01f)]
    [InlineData(10f)]
    public void OutOfRangeWatchdogInterval_FallsBackToDefault(float interval)
    {
        var result = _validator.Validate(Defaults with { WatchdogIntervalSeconds = interval }, Defaults, "core.json");
        Assert.Equal(Defaults.WatchdogIntervalSeconds, result.Config.WatchdogIntervalSeconds);
        Assert.Single(result.Issues);
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test tests/RetakeV4.Integration.Tests --nologo --filter "FullyQualifiedName~CoreConfigValidatorTests"`
Expected: échec de compilation (`RetakeV4.Modules.Core` introuvable).

- [ ] **Step 3: Implémenter la config et son validateur**

`src/RetakeV4/Modules/Core/CoreConfig.cs`
```csharp
using RetakeV4.Configuration;

namespace RetakeV4.Modules.Core;

public sealed record CoreConfig : ModuleConfig
{
    public CoreConfig() => Version = 1;

    public string ExecConfig { get; init; } = "RetakeV4/retake.cfg";

    public float WarmupFallbackSeconds { get; init; } = 16f;

    public float WatchdogIntervalSeconds { get; init; } = 0.25f;
}
```

`src/RetakeV4/Modules/Core/CoreConfigValidator.cs`
```csharp
using System.Text.RegularExpressions;
using RetakeV4.Configuration;

namespace RetakeV4.Modules.Core;

public sealed partial class CoreConfigValidator : IConfigValidator<CoreConfig>
{
    private const float MinWatchdogInterval = 0.05f;
    private const float MaxWatchdogInterval = 5f;

    [GeneratedRegex(@"^[A-Za-z0-9_\-]+(/[A-Za-z0-9_\-]+)*\.cfg$")]
    private static partial Regex SafeCfgPath();

    public ValidationResult<CoreConfig> Validate(CoreConfig config, CoreConfig defaults, string file)
    {
        var issues = new List<ConfigIssue>();
        var result = config;
        if (!SafeCfgPath().IsMatch(config.ExecConfig ?? string.Empty))
        {
            issues.Add(new ConfigIssue(file, nameof(CoreConfig.ExecConfig), "must be a relative .cfg path (letters, digits, _ - /); using default"));
            result = result with { ExecConfig = defaults.ExecConfig };
        }
        if (config.WarmupFallbackSeconds < 0f)
        {
            issues.Add(new ConfigIssue(file, nameof(CoreConfig.WarmupFallbackSeconds), "must be >= 0; using default"));
            result = result with { WarmupFallbackSeconds = defaults.WarmupFallbackSeconds };
        }
        if (config.WatchdogIntervalSeconds is < MinWatchdogInterval or > MaxWatchdogInterval)
        {
            issues.Add(new ConfigIssue(file, nameof(CoreConfig.WatchdogIntervalSeconds), $"must be between {MinWatchdogInterval} and {MaxWatchdogInterval}; using default"));
            result = result with { WatchdogIntervalSeconds = defaults.WatchdogIntervalSeconds };
        }
        return new ValidationResult<CoreConfig>(result, issues);
    }
}
```

- [ ] **Step 4: Vérifier le succès des tests**

Run: `dotnet test tests/RetakeV4.Integration.Tests --nologo --filter "FullyQualifiedName~CoreConfigValidatorTests"`
Expected: `Failed: 0`.

- [ ] **Step 5: Implémenter l'accès aux gamerules, le module et le cfg**

`src/RetakeV4/Modules/Core/GameRulesAccessor.cs`
```csharp
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace RetakeV4.Modules.Core;

internal static class GameRulesAccessor
{
    public static CCSGameRules? Get() =>
        Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
            .FirstOrDefault(proxy => proxy.IsValid)?.GameRules;
}
```

`src/RetakeV4/Modules/Core/CoreModule.cs`
```csharp
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using Microsoft.Extensions.Logging;
using RetakeV4.Configuration;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.Rounds;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace RetakeV4.Modules.Core;

public sealed class CoreModule : IRetakeModule
{
    private CoreConfig _config = new();
    private ModuleContext? _context;
    private WarmupTracker _warmup = WarmupTracker.Start(16f, 0f);
    private Timer? _watchdog;
    private IDisposable? _debugSubscription;
    private string _mapName = string.Empty;

    public string Name => "Core";

    public IReadOnlyList<string> DependsOn { get; } = Array.Empty<string>();

    private ModuleContext Context => _context ?? throw new InvalidOperationException("Core module is not loaded");

    public ModuleConfig LoadConfig(JsonConfigStore store, ILogger logger)
    {
        var result = store.Load("core.json", new CoreConfig(), new CoreConfigValidator());
        ConfigLogging.Report(logger, result.Issues);
        _config = result.Config;
        return _config;
    }

    public void Load(ModuleContext context)
    {
        _context = context;
        var plugin = context.Plugin;
        plugin.RegisterListener<Listeners.OnMapStart>(mapName => Context.Guard.Run(Name, "map_start", () => StartForMap(mapName, false)));
        plugin.RegisterEventHandler<EventRoundStart>((_, _) => Guarded("round_start", OnRoundStart));
        plugin.RegisterEventHandler<EventRoundFreezeEnd>((_, _) => Guarded("freeze_end", () => Context.Rounds.Handle(RoundSignal.FreezeEnded)));
        plugin.RegisterEventHandler<EventRoundEnd>((_, _) => Guarded("round_end", () => Context.Rounds.Handle(RoundSignal.RoundEnded)));
        plugin.AddCommand("css_retake_info", "Shows the RetakeV4 version", OnInfoCommand);
        if (_config.Debug)
        {
            _debugSubscription = context.Bus.Subscribe<RoundPhaseChanged>(Name, e =>
                context.Logger.LogInformation("Round {Round}: {From} -> {To}", e.RoundNumber, e.From, e.To));
        }
        if (!string.IsNullOrWhiteSpace(Server.MapName))
        {
            StartForMap(Server.MapName, context.HotReload);
        }
    }

    public void Unload()
    {
        _watchdog?.Kill();
        _watchdog = null;
        _debugSubscription?.Dispose();
        _debugSubscription = null;
        _context = null;
    }

    private HookResult Guarded(string stage, Action action)
    {
        Context.Guard.Run(Name, stage, action);
        return HookResult.Continue;
    }

    private void OnRoundStart()
    {
        if (GameRulesAccessor.Get()?.WarmupPeriod == true)
        {
            Context.Rounds.Handle(RoundSignal.WarmupStarted);
            return;
        }
        Context.Rounds.Handle(RoundSignal.WarmupEnded);
        Context.Rounds.Handle(RoundSignal.RoundStarted);
    }

    private void StartForMap(string mapName, bool isHotReload)
    {
        _mapName = mapName;
        Server.ExecuteCommand($"exec {_config.ExecConfig}");
        _warmup = WarmupTracker.Start(_config.WarmupFallbackSeconds, Server.CurrentTime);
        var isWarmup = GameRulesAccessor.Get()?.WarmupPeriod ?? true;
        Context.Rounds.Reset(RoundTracker.InitialStateFor(isWarmup));
        Context.Bus.Publish(new MapStarted(mapName));
        RestartWatchdog();
        if (isHotReload)
        {
            Server.ExecuteCommand("mp_restartgame 1");
        }
    }

    private void RestartWatchdog()
    {
        _watchdog?.Kill();
        _watchdog = Context.Plugin.AddTimer(
            _config.WatchdogIntervalSeconds,
            () => Context.Guard.Run(Name, "warmup_watchdog", CheckWarmup),
            TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void CheckWarmup()
    {
        var rules = GameRulesAccessor.Get();
        if (rules is null)
        {
            return;
        }
        var (next, forceEnd) = _warmup.Evaluate(new WarmupSnapshot(rules.WarmupPeriod, rules.WarmupPeriodEnd, Server.CurrentTime));
        _warmup = next;
        if (!forceEnd)
        {
            return;
        }
        Context.Logger.LogInformation("Forcing warmup end on {Map}", _mapName);
        Server.ExecuteCommand("mp_warmup_end");
        Context.Bus.Publish(new WarmupForcedEnd(_mapName));
        Context.Text.ChatAll("core.warmup.forced_end");
    }

    private void OnInfoCommand(CCSPlayerController? player, CommandInfo command)
    {
        var plugin = Context.Plugin;
        var args = new object[] { plugin.ModuleName, plugin.ModuleVersion, plugin.ModuleAuthor };
        command.ReplyToCommand(player is null
            ? Context.Text.Server("core.info.version", args)
            : Context.Text.For(player, "core.info.version", args));
    }
}
```

`src/RetakeV4/cfg/RetakeV4/retake.cfg`
```
// RetakeV4 - server cvars executed on every map start (see core.json -> ExecConfig)
// Required by the retake flow: do not change
bot_kick
bot_quota 0
mp_autoteambalance 0
mp_forcecamera 1
mp_give_player_c4 0
mp_free_armor 0
mp_halftime 0
mp_ignore_round_win_conditions 0
mp_join_grace_time 0
mp_match_can_clinch 0
mp_maxmoney 0
mp_playercashawards 0
mp_respawn_on_death_ct 0
mp_respawn_on_death_t 0
mp_solid_teammates 1
mp_teamcashawards 0
mp_warmup_pausetimer 0

// Tunable
mp_autokick 0
mp_buy_anywhere 0
mp_buytime 0
mp_c4timer 40
mp_freezetime 3
mp_friendlyfire 1
mp_round_restart_delay 2
sv_talk_enemy_dead 0
sv_talk_enemy_living 0
sv_deadtalk 1
spec_replay_enable 0
mp_maxrounds 30
mp_match_end_restart 0
mp_timelimit 0
mp_match_restart_delay 10
mp_death_drop_gun 1
mp_death_drop_defuser 1
mp_death_drop_grenade 1
mp_warmuptime 15

echo "RetakeV4 cvars loaded"
```

Dans `src/RetakeV4/RetakeV4Plugin.cs`, remplacer `CreateModules` et ajouter `using RetakeV4.Modules.Core;` :
```csharp
    private static IReadOnlyList<IRetakeModule> CreateModules() => new IRetakeModule[] { new CoreModule() };
```

- [ ] **Step 6: Build complet et tests**

Run: `dotnet build RetakeV4.sln -c Release --nologo && dotnet test RetakeV4.sln --nologo`
Expected: build à `0 Avertissement(s)`, tous les tests `Failed: 0`.

- [ ] **Step 7: Commit**

```bash
git add src/RetakeV4/Modules/Core src/RetakeV4/cfg src/RetakeV4/RetakeV4Plugin.cs tests/RetakeV4.Integration.Tests/Modules/Core
git commit -m "feat: module Core (cycle des rounds, cvars, watchdog warmup, info)"
```

---

### Task 13: Package de test, checklist en jeu et guide du dépôt

**Files:**
- Create: `scripts/package-dev.ps1`, `docs/CHECKLIST-INGAME.md`, `CLAUDE.md`
- Modify: `.gitignore` (ajouter `artifacts/` s'il manque)

**Interfaces:**
- Consumes: build de `src/RetakeV4` (Tasks 9-12).
- Produces: `artifacts/dev/` avec l'arborescence serveur (`addons/counterstrikesharp/plugins/RetakeV4/…`, `cfg/RetakeV4/retake.cfg`).

- [ ] **Step 1: Script de package**

`scripts/package-dev.ps1`
```powershell
param([string]$Configuration = "Release")
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
dotnet build "$root/src/RetakeV4/RetakeV4.csproj" -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

$bin = Join-Path $root "src/RetakeV4/bin/$Configuration/net10.0"
$out = Join-Path $root "artifacts/dev"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }

$pluginDir = Join-Path $out "addons/counterstrikesharp/plugins/RetakeV4"
New-Item -ItemType Directory -Force $pluginDir | Out-Null
foreach ($file in @("RetakeV4.dll", "RetakeV4.Domain.dll", "RetakeV4.deps.json", "RetakeV4.runtimeconfig.json")) {
    $source = Join-Path $bin $file
    if (-not (Test-Path $source)) { throw "Missing build output: $file" }
    Copy-Item $source $pluginDir
}
Copy-Item (Join-Path $bin "lang") $pluginDir -Recurse

$cfgDir = Join-Path $out "cfg/RetakeV4"
New-Item -ItemType Directory -Force $cfgDir | Out-Null
Copy-Item (Join-Path $root "src/RetakeV4/cfg/RetakeV4/retake.cfg") $cfgDir

Write-Host "Package ready: $out"
```

Run: `pwsh -NoProfile -File scripts/package-dev.ps1`
Expected: `Package ready: …/artifacts/dev` et, dans `artifacts/dev/addons/counterstrikesharp/plugins/RetakeV4/`, `RetakeV4.dll`, `RetakeV4.Domain.dll`, `RetakeV4.deps.json`, `RetakeV4.runtimeconfig.json` et `lang/en.json`, `lang/fr.json`.

- [ ] **Step 2: Checklist en jeu (phase 1)**

`docs/CHECKLIST-INGAME.md`
```markdown
# Checklist de test en jeu — RetakeV4

Chaque phase ajoute sa section. Cocher sur un serveur de test avant de passer à la phase suivante.
Préparation : retirer `plugins/CS2Retake/` (V3), copier `artifacts/dev/*` à la racine `csgo/` du serveur.

## Phase 1 — Fondations
- [ ] Au démarrage : log `RetakeV4 4.0.0-alpha.1 loaded with modules: Core`, aucune erreur.
- [ ] `configs/plugins/RetakeV4/core.json` est créé avec les valeurs par défaut.
- [ ] Log `RetakeV4 cvars loaded` à chaque changement de map.
- [ ] Mode compétitif : le warmup se termine seul (~16 s) avec le message « Fin du warmup, le retake commence ! » (client en `css_lang fr`).
- [ ] Avec `"Debug": true` dans `core.json` (puis restart map) : chaque round logue `PostRound -> Preparing`, `Preparing -> FreezeTime`, `FreezeTime -> Live`, `Live -> PostRound`, avec un numéro de round croissant.
- [ ] `mp_restartgame 1` en plein round : pas d'erreur, le numéro de round continue de croître.
- [ ] `css_retake_info` (console serveur et joueur) : `RetakeV4 v4.0.0-alpha.1 par NeuTroNBZh` (fr) / `by` (en).
- [ ] `css_plugins reload RetakeV4` en plein round : `mp_restartgame 1` est exécuté, le plugin repart sans erreur.
- [ ] `core.json` volontairement cassé (`{ "Debug": `) : warning `invalid JSON`, plugin chargé avec les valeurs par défaut, fichier non modifié.
- [ ] `core.json` avec `"ExecConfig": "../../server.cfg"` : warning `ExecConfig`, `RetakeV4/retake.cfg` exécuté à la place.
```

- [ ] **Step 3: Guide du dépôt**

`CLAUDE.md`
```markdown
# CLAUDE.md — RetakeV4

Plugin CounterStrikeSharp (C# / .NET 10, CSSharp 1.0.370+) de retake CS2, réécriture complète de CS2RetakeV3.

- Spec : `docs/superpowers/specs/2026-09-30-retake-v4-design.md` (source de vérité fonctionnelle)
- Plans : `docs/superpowers/plans/`
- Résultats du prototype HUD : `docs/spikes/hud-probe-findings.md`

## Structure
- `src/RetakeV4.Domain` : logique pure, **aucune référence à CounterStrikeSharp**, couverte à ≥ 80 %.
- `src/RetakeV4` : plugin CSSharp, adaptateurs fins uniquement. Un dossier par module sous `Modules/`, un JSON par module.
- `tests/RetakeV4.Domain.Tests` : tests unitaires du Domain. `tests/RetakeV4.Integration.Tests` : config, lang, hôte de modules.
- `spikes/` : prototypes jetables, hors solution.

## Commandes
- Build : `dotnet build RetakeV4.sln -c Release` (0 warning exigé, TreatWarningsAsErrors)
- Tests : `dotnet test RetakeV4.sln`
- Couverture Domain : `dotnet test tests/RetakeV4.Domain.Tests /p:CollectCoverage=true /p:Include="[RetakeV4.Domain]*" /p:Threshold=80 /p:ThresholdType=line`
- Package serveur de test : `pwsh -NoProfile -File scripts/package-dev.ps1` → `artifacts/dev/`

## Règles
- Toute décision métier va dans le Domain avec ses tests (TDD) ; les modules ne font que traduire événements CS2 ↔ Domain.
- Un module ne référence jamais un autre module : passer par `IEventBus`, `PreparationPipeline` ou les services du `ModuleContext`.
- Tout handler CSSharp d'un module passe par `ModuleGuard.Run`.
- Textes joueurs uniquement via `lang/*.json` (clés `module.section.key`, en + fr synchronisés) ; logs en anglais avec templates constants.
- Valeurs par défaut de config uniquement dans les records C# (`*Config.cs`).
- Tests en jeu : `docs/CHECKLIST-INGAME.md`, une section par phase.
```

- [ ] **Step 4: Vérification finale de la phase**

Run: `dotnet build RetakeV4.sln -c Release --nologo && dotnet test RetakeV4.sln --nologo && dotnet test tests/RetakeV4.Domain.Tests --nologo /p:CollectCoverage=true /p:Include="[RetakeV4.Domain]*" /p:Threshold=80 /p:ThresholdType=line`
Expected: 0 warning, `Failed: 0` partout, couverture Domain ≥ 80 %.

- [ ] **Step 5: Commit**

```bash
git add scripts/package-dev.ps1 docs/CHECKLIST-INGAME.md CLAUDE.md .gitignore
git commit -m "chore: package de test, checklist en jeu et guide du dépôt"
```

- [ ] **Step 6: CHECKPOINT HUMAIN — test en jeu de la phase 1**

Demander à l'utilisateur de déployer `artifacts/dev/` sur le serveur de test et de cocher la section « Phase 1 » de `docs/CHECKLIST-INGAME.md`. Les résultats de HudProbe (Task 4) et de cette checklist conditionnent le plan des phases 2-3.
