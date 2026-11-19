# Lesson 02 — A rule should have one source of truth

Previously `Hediff_Aperture.cs` contained the aptitude thresholds and regeneration rules. Now `Domain/AptitudeRules.cs` owns both calculations.

```csharp
var grade = AptitudeRules.Grade(57); // C
var recovery = AptitudeRules.RegenerationPerHour(57); // 4f per 2500 ticks
```

Why? When we add awakening, recruitment, academy selection, and bloodlines, each system should call the same rules. Copy-pasted logic can disagree and create a pawn whose displayed grade differs from their effective mechanics.

`Contracts/GuContracts.cs` defines *interfaces*, promises that later add-ons may implement. Their presence does not make an integration operational. `IGuEducationProvider` reports class attendance without requiring the Core to reference a specific education mod.

Exercise: find `AptitudeRules.Grade` in the Hediff and trace where the on-screen grade comes from. Then change no code, and explain why a D-grade pawn with aptitude 25 regenerates 2 essence points per hour instead of 2% of its capacity.
