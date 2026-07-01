using Dusk;
using UnityEngine;

namespace Observers
{
    internal class ObserverContentHandler : ContentHandler<ObserverContentHandler>
    {
        internal ObserverAssets? observerAssets;

        public class ObserverAssets(DuskMod mod, string filePath) : AssetBundleLoader<ObserverAssets>(mod, filePath) {
            [LoadFromBundle("ObserverChaser.prefab")]
            public GameObject ObserverChaser { get; private set; } = null!;
        }


        public ObserverContentHandler(DuskMod mod) : base(mod)
        {
            RegisterContent("observers", out observerAssets);
        }
    }
}

