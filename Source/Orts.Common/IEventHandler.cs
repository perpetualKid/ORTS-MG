namespace Orts.Common
{
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
    public interface IEventHandler
#pragma warning restore CA1711 // Identifiers should not have incorrect suffix
    {
        void HandleEvent(TrainEvent trainEvent);
        void HandleEvent(TrainEvent trainEvent, object viewer);
    }

}
