namespace MediaEngine.Domain;

/// <summary>
/// Named constants for the metadata claim confidence hierarchy.
/// Higher values win in the Priority Cascade. These form a deliberate
/// semantic ordering — do not change values without understanding the
/// full cascade implications.
///
/// <list type="table">
///   <listheader><term>Tier</term><description>Range</description></listheader>
///   <item><term>User authority</term><description>1.00</description></item>
///   <item><term>Reconciliation match</term><description>0.98</description></item>
///   <item><term>Structured data</term><description>0.95</description></item>
///   <item><term>Wikidata properties</term><description>0.90</description></item>
///   <item><term>Supplementary data</term><description>0.85–0.88</description></item>
///   <item><term>Reduced authority</term><description>0.75</description></item>
///   <item><term>AI-generated</term><description>0.70</description></item>
/// </list>
/// </summary>
public static class ClaimConfidence
{
    /// <summary>User-locked claims — always wins in the cascade.</summary>
    public const double UserLock = 1.00;

    /// <summary>Wikidata reconciliation match label — highest auto-generated confidence.</summary>
    public const double ReconciliationTitle = 0.98;

    /// <summary>Pseudonym pen name claims — beats individual real-name claims.</summary>
    public const double PenName = 0.95;

    /// <summary>Author embedded in file metadata, emitted at Wikidata authority level.</summary>
    public const double EmbeddedAuthor = 0.95;

    /// <summary>Language-specific original title from Wikidata.</summary>
    public const double OriginalTitle = 0.95;

    /// <summary>External bridge identifiers (ASIN, ISBN, TMDB ID) from structured data.</summary>
    public const double BridgeId = 0.95;

    /// <summary>Collective pseudonym flag and member links.</summary>
    public const double CollectivePseudonym = 0.95;

    /// <summary>General Wikidata property normalisation (P-codes → claim keys).</summary>
    public const double WikidataProperty = 0.90;

    /// <summary>Wikipedia or retail provider description extract.</summary>
    public const double Description = 0.90;

    /// <summary>Person headshot image URL from Wikimedia Commons.</summary>
    public const double HeadshotUrl = 0.90;

    /// <summary>Corrected media type classification during hydration.</summary>
    public const double MediaTypeCorrection = 0.90;

    /// <summary>Audiobook narrator from retail provider metadata.</summary>
    public const double Narrator = 0.90;

    /// <summary>AI QID disambiguation fallback selection.</summary>
    public const double QidDisambiguator = 0.90;

    /// <summary>Entity QID companion references (e.g. genre_qid, director_qid).</summary>
    public const double EntityQidReference = 0.90;

    /// <summary>Wikidata narrative/plot summary.</summary>
    public const double PlotSummary = 0.88;

    /// <summary>Alias/alternate titles — intentionally lower than primary title.</summary>
    public const double AlternateTitle = 0.85;

    /// <summary>Duration from retail provider data.</summary>
    public const double Duration = 0.85;

    /// <summary>Publisher from retail provider data.</summary>
    public const double Publisher = 0.85;

    /// <summary>Raw Wikidata P50 author — reduced so embedded/pen-name author wins.</summary>
    public const double WikidataAuthorRaw = 0.75;

    /// <summary>Web scraping / URL metadata extraction.</summary>
    public const double UrlExtraction = 0.75;

    /// <summary>LLM-generated description (Description Intelligence).</summary>
    public const double AiDescription = 0.70;

    /// <summary>Valid cover art assessment from CoverArtValidator.</summary>
    public const double CoverArtValid = 0.70;

    /// <summary>Invalid/placeholder cover art detection (high confidence in the negative signal).</summary>
    public const double CoverArtInvalid = 0.95;
}
