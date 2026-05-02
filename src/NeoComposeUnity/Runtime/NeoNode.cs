namespace NeoCompose.Runtime
{
    public class NeoNode
    {
        protected NeoClient client;

        public NeoNode(NeoClient client)
        {
            this.client = client;
        }
    }
}