namespace Netrunner.Core;

/// <summary>Typed predicates evaluated against the resolved occurrence and current turn facts.</summary>
public enum BehaviorTriggerCondition
{
    FacedownArchives,
    FirstRunnerTag,
    HqOrRdRun,

    /// <summary>EL-FACEUP-AGENDA: the occurrence's subject is an installed card that is faceup (BANGUN).</summary>
    FaceUpInstalledSubject,

    /// <summary>EL-HOSTED-FACEUP: the occurrence's subject was installed this turn without spending credits (Bling).</summary>
    SubjectInstalledWithoutCredits,

    /// <summary>EL-SUBROUTINE-RESOLVED-FACT: a subroutine resolved during the live run (Ryō "Phoenix" Ōno).</summary>
    SubroutineResolvedThisRun,

    /// <summary>
    /// The accessed card the Runner trashed was installed, by last-known
    /// information: its server is recorded (Aggressive Trendsetting).
    /// </summary>
    TrashedSubjectWasInstalled,

    /// <summary>VP-SCORING-FACTS: the occurrence's subject (a scored agenda) was installed this turn (Word on the Street).</summary>
    SubjectInstalledThisTurn,

    /// <summary>VP-SCORING-FACTS: the occurrence's subject was not installed this turn (Word on the Street's payoff).</summary>
    SubjectNotInstalledThisTurn,

    /// <summary>
    /// VP-ICE-PASSED: the passed ICE (the subject) is the outermost protecting its server, and the Runner fully broke it in
    /// the encounter that just ended (Sipa).
    /// </summary>
    SubjectOutermostFullyBroken,

    /// <summary>VP-SUBROUTINE-BROKEN: the encountered ICE (the subject) has 0 or less strength (The Tungsten Tailor).</summary>
    SubjectStrengthAtMostZero,

    /// <summary>VP-PAID-REDIRECT: the live run was started by the trigger's source, an ability's "Run Archives" (Baker).</summary>
    RunStartedBySource,

    /// <summary>VP-ADVANCEMENT-COST: the live run is against a server other than the source's (Sacrifice Zone Expansion).</summary>
    RunOnAnotherServer,
}
