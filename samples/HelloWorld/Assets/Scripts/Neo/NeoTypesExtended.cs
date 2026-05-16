using UnityEngine;
using NeoCompose.Runtime;

namespace HelloWorld.Assets.Scripts.Neo
{
    public partial class ReadOnlyOutpost
    {
        protected override void LazyInitialize()
        {
            base.LazyInitialize();
            FunctionHandler ??= new OutpostFunctionHandler(this);

            if (SaveUnsafe is not null)
            {
                return;
            }
            
            HelloWorldNeo.Instance.Save.OutpostSaveMap.Add(valueId, new());
        }
    }

    public class OutpostFunctionHandler : IOutpostFunctionHandler
    {
        private readonly ReadOnlyOutpost Outpost;

        public OutpostFunctionHandler(ReadOnlyOutpost Outpost)
        {
            this.Outpost = Outpost;
        }

        public string DebugLog(string text)
        {
            string log = $"<color=green>{Outpost.Name}:</color> {text}";
            Debug.Log(log);
            return log;
        }
    }
}