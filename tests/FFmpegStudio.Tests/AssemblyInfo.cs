using Xunit;

// L.SetLanguage is process-wide. Collections that switch the UI language must not
// overlap tests that assert German source text.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
