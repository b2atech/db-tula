namespace B2A.DbTula.Core.Enums;

/// <summary>
/// Semantic classification of a schema difference, in order of increasing concern.
///
///   None          — no difference (or fully equivalent; nothing to report)
///   Cosmetic      — differs only in naming / formatting; zero behavioural effect
///   Metadata      — different DDL spelling, identical observable DML behaviour
///                   (e.g. IDENTITY BY DEFAULT vs SERIAL vs DEFAULT nextval()).
///                   Migration optional.
///   Functional    — observable INSERT/UPDATE/DELETE behaviour differs
///                   (e.g. auto-generation present on one side only). Migration required.
///   DataIntegrity — schema may match, but existing/future data can violate it
///                   (e.g. a sequence whose value trails MAX(id)). Data fix required.
/// </summary>
public enum DriftCategory
{
    None,
    Cosmetic,
    Metadata,
    Functional,
    DataIntegrity
}
