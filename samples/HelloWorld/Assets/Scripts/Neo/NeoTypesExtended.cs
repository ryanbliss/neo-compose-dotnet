using UnityEngine;
using NeoCompose.Runtime;

namespace HelloWorld.Assets.Scripts.Neo
{
    public partial class ReadOnlyOutpost
    {
        protected override void LazyInitialize()
        {
            base.LazyInitialize();
            if (SaveUnsafe is not null)
            {
                return;
            }
            
            HelloWorldNeo.Instance.Save.OutpostSaveMap.Add(valueId, new());
        }
    }
}