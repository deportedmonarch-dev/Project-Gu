global using GuIndividual = ProjectGu.Domain.GuRecord;
using ProjectGu;
using ProjectGu.Domain;
using Verse;

/// <summary>
/// Test-side observation of a Schema V2 record in the vocabulary of the 0.2.5 fixtures. Custody is derived from the
/// native graph through the registry's pure <c>Observe</c> query. It is not stored by V2. Revision means custody revision.
/// </summary>
internal enum GuCustodyKind { Unavailable = 0, WildPawn = 1, DormantGround = 2, DormantInventory = 3, Aperture = 4, OtherHolder = 5 }

internal enum GuBiologicalState { Active = 1, Dormant = 2 }

internal readonly record struct GuCustody(GuCustodyKind Kind, string NativeOwnerId, string HolderPawnId = null);

internal static class GuV2TestView
{
    private static GuIndividualRegistry Registry => Current.Game.GetComponent<GuIndividualRegistry>();

    extension(GuRecord record)
    {
        public int Revision => record.CustodyRevision;
        public GuBiologicalState BiologicalState => (GuBiologicalState)(int)record.Condition;

        public GuCustody Custody
        {
            get
            {
                var observed = Registry.Observe(record);
                return observed.Form switch
                {
                    GuObservedForm.ActivePawn or GuObservedForm.WorldPawn => new(GuCustodyKind.WildPawn, observed.NativeId),
                    GuObservedForm.GroundThing => new(GuCustodyKind.DormantGround, observed.NativeId),
                    GuObservedForm.HeldThing => new(GuCustodyKind.DormantInventory, observed.NativeId, observed.HolderId),
                    GuObservedForm.Contained => new(GuCustodyKind.Aperture, observed.NativeId, observed.HolderId),
                    GuObservedForm.OtherHolder => new(GuCustodyKind.OtherHolder, observed.NativeId, observed.HolderId),
                    // Unbound records keep their stored identifiers, which are stable across reload.
                    _ => new(GuCustodyKind.Unavailable, record.ProjectionId ?? record.ContainerId ?? record.LegacyApertureLoadId, record.AnchorHint)
                };
            }
        }
    }

    extension(GuIndividualRegistry registry)
    {
        public System.Collections.Generic.IReadOnlyList<GuRecord> Individuals => registry.Records;
    }
}
