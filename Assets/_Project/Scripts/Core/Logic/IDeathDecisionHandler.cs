public interface IDeathDecisionHandler
{
    bool TryHandleZeroHealth(UnitHealthController healthController);
}
