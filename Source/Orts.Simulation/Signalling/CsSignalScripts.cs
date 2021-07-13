using System.IO;

namespace Orts.Simulation.Signalling
{
    // TODO 20210505 this class may be integrated somewhere else 
    public static class CsSignalScripts
    {
        private static readonly string scriptPath = Path.Combine(Simulator.Instance.RouteFolder.CurrentFolder, "Script", "Signal");

        public static CsSignalScript TryGetScript(string scriptName)
        {
            return Simulator.Instance.ScriptManager.Load(scriptPath, scriptName) as CsSignalScript;
        }
    }
}
