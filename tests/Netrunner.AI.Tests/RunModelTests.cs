using Netrunner.Core;
using Xunit;

namespace Netrunner.AI.Tests;

/// <summary>The whole-run model's subroutine vocabulary (A4a), read from typed specs.</summary>
public sealed class RunModelTests
{
    [Fact]
    public void SubroutinesMapToTheirEffectAndSeverity()
    {
        Assert.True(SubroutineEffect.Of(new EndRunSubroutineSpec()) is { EndsRun: true, Severity: SubroutineSeverity.Serious });
        Assert.True(SubroutineEffect.Of(new DamageSubroutineSpec(DamageKind.Net, 2)) is { Damage: 2, Severity: SubroutineSeverity.Moderate });
        Assert.True(SubroutineEffect.Of(new GiveTagsSubroutineSpec(1)) is { Tags: 1, Severity: SubroutineSeverity.Moderate });
        Assert.True(SubroutineEffect.Of(new GainCreditsSubroutineSpec(PlayerSide.Corp, 1)) is { CorpGains: 1, Severity: SubroutineSeverity.Minor });
        Assert.True(SubroutineEffect.Of(new LoseCreditsSubroutineSpec(PlayerSide.Runner, 3)) is { CreditsLost: 3, Severity: SubroutineSeverity.Moderate });
        Assert.True(SubroutineEffect.Of(new EndRunIfCreditsAtMostSubroutineSpec(PlayerSide.Runner, 6))
            is { EndsRunIfCreditsAtMost: 6, Severity: SubroutineSeverity.Serious });
        Assert.True(SubroutineEffect.Of(new PayCreditsOrTagSubroutineSpec(2)) is { PayOrTagCredits: 2, PayOrTagTags: 1 });
        // Ballista: the Corp chooses between trashing a program and ending the run, so it can always end it.
        Assert.True(SubroutineEffect.Of(new TrashInstalledRunnerCardSubroutineSpec(RuntimeCardClass.Program, endRunAlternative: true))
            is { EndsRun: true, TrashesCard: true, TrashClass: RuntimeCardClass.Program, Severity: SubroutineSeverity.Serious });
        Assert.True(SubroutineEffect.Of(new TrashInstalledRunnerCardSubroutineSpec(RuntimeCardClass.Program)) is { EndsRun: false, TrashesCard: true });
        // Diviner: net damage, then the run ends if the trashed card's cost is odd.
        Assert.True(SubroutineEffect.Of(new DamageSubroutineSpec(DamageKind.Net, 1, DamageSubroutineFollowUp.EndRunIfTrashedCostOdd))
            is { Damage: 1, MayEndRun: true, EndsRun: false, Severity: SubroutineSeverity.Serious });
    }
}
