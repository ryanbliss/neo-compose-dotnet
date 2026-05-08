using NeoCompose.Runtime;

namespace HelloWorld.Assets.Scripts.Neo
{
    public partial class Outpost : ReadOnlyOutpost
    {
        protected override void LazyInitialize()
        {
            base.LazyInitialize();
            if (SaveUnsafe is not null) return;
            // HelloWorldNeo.Instance.Save.OutpostSaveMap.Add(valueId, new());
        }
    }
}