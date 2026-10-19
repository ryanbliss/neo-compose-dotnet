namespace NeoCompose.Runtime
{
    public class NeoNode
    {
        protected NeoClient client;
        internal NeoClient Client => client;

        internal NeoClient RuntimeClient => client;

        public NeoNode(NeoClient client)
        {
            this.client = client;
        }
    }
}