#nullable enable
using System;
namespace NeoCompose.Runtime.NeoScript
{
    internal sealed class NeoScriptErrorPayload
    {
        internal readonly string ClassId;
        internal readonly string Message;
        internal object? Value;
        internal NeoScriptExecutor.ErrorSelection? Selection;
        internal bool Searched;
        internal NeoScriptErrorPayload(string classId, string message, object? value = null)
        {
            ClassId = classId;
            Message = message;
            Value = value;
        }
    }

    public sealed class NeoScriptException : NSGetterRuntimeError
    {
        private readonly NSGetterEvaluator.Context? context;
        private NeoErrorBase? error;
        private InvalidOperationException? wrapperFailure;
        public NeoErrorBase Error
        {
            get
            {
                if (wrapperFailure is not null)
                    throw wrapperFailure;
                try
                {
                    return error ??= NSGetterEvaluator.ErrorView(Payload, context!);
                }
                catch (InvalidOperationException failure)
                {
                    // C# discards filter exceptions. Keep configuration failures visible.
                    wrapperFailure = failure;
                    UnityEngine.Debug.LogException(failure);
                    throw;
                }
            }
        }

        internal NeoClient OriginClient => context?.client ?? error!.Client;

        // A native throw starts a fresh dispatch while keeping the authored value.
        internal NeoScriptException ForNativeReentry() => new(this);

        private NeoScriptException(NeoScriptException original)
            : base(original.Message, original.Payload.ClassId, original)
        {
            Payload.Value = original.Payload.Value;
            context = original.context;
            error = original.error;
            if (original.Data.Contains("neoScriptFrames"))
                Data["neoScriptFrames"] = original.Data["neoScriptFrames"];
        }

        public NeoScriptException(NeoErrorBase error) : base(error.Message, error.classId!)
        {
            this.error = error;
            Payload.Value = error;
        }

        internal NeoScriptException(NSGetterRuntimeError failure, NSGetterEvaluator.Context context)
            : base(failure.Message, failure)
        {
            this.context = context;
        }
    }

    internal sealed class NeoScriptControlError : NSGetterRuntimeError
    {
        internal NeoScriptControlError(string message) : base(message) { }
    }
}
