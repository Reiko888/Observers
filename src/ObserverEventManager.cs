using HarmonyLib;

namespace Observers.src
{
    [HarmonyPatch(typeof(Terminal))]
    internal class TerminalVideoInjectorPatch
    {
        [HarmonyPatch("Start")]
        [HarmonyPostfix]
        public static void InjectVideo(Terminal __instance)
        {
            if (ObserverContentHandler.Instance.observerAssets.ObserverChaser == null) return;
            ObserverTerminalAssets container = ObserverContentHandler.Instance.observerAssets.ObserverChaser.GetComponent<ObserverTerminalAssets>();
            if (container == null || container.bestiaryVideo == null) return;
            foreach (TerminalNode node in __instance.enemyFiles)
            {
                if (node != null && node.name == "ObserverBestiaryNode")
                {
                    node.displayVideo = container.bestiaryVideo;
                    return;
                }
            }

            Plugin.Logger.LogWarning("Could not find a TerminalNode named 'ObserverBestiaryNode' in the Bestiary.");
        }
    }
}