namespace Orts.Common
{
    public interface IEventHandler
    {
        void HandleEvent(TrainEvent evt);
        void HandleEvent(TrainEvent evt, object viewer);
    }

}
