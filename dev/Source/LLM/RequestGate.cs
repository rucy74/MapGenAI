using System.Threading;

namespace MapGenAI.LLM
{
    // Reset/close invalidates both an in-flight request and an already queued reply.
    public sealed class RequestGate
    {
        public sealed class Ticket
        {
            internal int Version;
            public CancellationToken Token;
        }
        public sealed class Reply
        {
            public string Text, Error;
            public object Context;
        }
        private readonly object sync = new object();
        private int version;
        private CancellationTokenSource cancellation;
        private Reply pending;
        public Ticket Begin()
        {
            Cancel();
            lock(sync)
            {
                cancellation = new CancellationTokenSource();
                return new Ticket { Version=version, Token=cancellation.Token };
            }
        }
        public void Complete(Ticket ticket, string text, string error, object context=null)
        {
            lock(sync)
                if(ticket.Version==version && !ticket.Token.IsCancellationRequested)
                    pending=new Reply { Text=text, Error=error, Context=context };
        }
        public Reply Take()
        {
            lock(sync) { var result=pending; pending=null; return result; }
        }
        public void Cancel()
        {
            CancellationTokenSource old;
            lock(sync) { version++; pending=null; old=cancellation; cancellation=null; }
            if(old!=null) { old.Cancel(); old.Dispose(); }
        }
    }
}
