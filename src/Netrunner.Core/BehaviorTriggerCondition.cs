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
}
