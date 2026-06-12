namespace B2A.DbTula.Core.Enums;

/// <summary>
/// How an integer column obtains its value when not supplied by an INSERT.
///
/// Detection (from catalog metadata):
///   IdentityAlways    — is_identity = YES, identity_generation = ALWAYS
///   IdentityByDefault — is_identity = YES, identity_generation = BY DEFAULT
///   Serial            — column_default = nextval('seq') AND the sequence is OWNED BY
///                       this column (pg_depend deptype 'a'); i.e. created via SERIAL
///   SequenceDefault   — column_default = nextval('seq') with no ownership link
///                       (a hand-written DEFAULT nextval(...))
///   None              — no automatic generation (plain column)
///
/// NOTE: Serial and SequenceDefault are indistinguishable from information_schema
/// alone — both surface as a nextval() default. They are only separable by inspecting
/// pg_depend for an auto-dependency between the sequence and the column. For semantic
/// comparison they are treated as one equivalence class (see GenerationStrategyAnalyzer).
/// </summary>
public enum GenerationStrategy
{
    None,
    IdentityAlways,
    IdentityByDefault,
    Serial,
    SequenceDefault
}
