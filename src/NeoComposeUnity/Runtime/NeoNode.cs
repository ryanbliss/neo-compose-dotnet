namespace NeoCompose.Runtime
{
    public class NeoNode
    {
        protected NeoClient client;
        internal NeoClient Client => client;

        public NeoNode(NeoClient client)
        {
            this.client = client;
        }
    }
}
