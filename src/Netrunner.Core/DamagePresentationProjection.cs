namespace Netrunner.Core;

internal static class DamagePresentationProjection
{
    internal static CardInstanceId? Source(IReadOnlyList<ResolutionFrame> stack)
    {
        for (int index = stack.Count - 1; index >= 0; index--)
        {
            if (stack[index] is EffectResolutionFrame)
            {
                return index > 0 ? stack[index - 1] switch
                {
                    BehaviorResolutionFrame behavior => behavior.SourceCardId,
                    IceSubroutineFrame ice => ice.IceId,
                    _ => null,
                } : null;
            }
        }
        return null;
    }
}
