namespace ProjectGu.Contracts
{
    // Stable DTOs for future adapters. Not yet used by gameplay.
    // Avoid exposing mutable internal dictionaries or third-party mod classes.
    public readonly struct GuActorId
    {
        public readonly string Value;
        public GuActorId(string value) { Value = value ?? ""; }
        public override string ToString() => Value;
    }

    public readonly struct GuInstitutionId
    {
        public readonly string Value;
        public GuInstitutionId(string value) { Value = value ?? ""; }
        public override string ToString() => Value;
    }

    public readonly struct GuEvent
    {
        public readonly string Kind;
        public readonly GuActorId Actor;
        public readonly GuInstitutionId Institution;
        public readonly int Tick;
        public GuEvent(string kind, GuActorId actor, GuInstitutionId institution, int tick)
        {
            Kind = kind ?? "";
            Actor = actor;
            Institution = institution;
            Tick = tick;
        }
    }

    // The core will own canonical records; integrations report observations.
    // No direct reference to any optional DLC or third-party mod.
    public interface IGuEducationProvider
    {
        bool TryGetCompletedClassTicks(GuActorId actor, out int classTicks);
    }

    public interface IGuWorldActivityProvider
    {
        // Lightweight snapshots only. Implementations must not perform map-wide
        // queries inside this call; snapshot updates are scheduled separately.
        bool TryGetActivity(GuInstitutionId institution, out string activityId);
    }
}
