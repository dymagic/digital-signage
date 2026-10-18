namespace Signage.E2E.Infrastructure;

/// <summary>
/// Tests that change something every screen sees (the info overlay setting, identify all) run in this collection, on
/// their own after the parallel tests, so they can't disturb tests that count manifest pushes or take screenshots.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GlobalStateTests
{
    public const string Name = "Global state";
}
