// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using DialogueModel = NeoCompose.Runtime.Json.Dialogue;

namespace NeoCompose.Runtime
{
    public sealed class NeoDialogue : IDisposable
    {
        private bool started;
        private bool disposed;

        public string id { get; }
        public string? groupId { get; }
        public DialogueModel data { get; }
        public NeoDialogueContext context { get; }
        public bool isStarted => started;
        public bool isDisposed => disposed;

        public event Action<NeoDialogueTextNode>? ShowText;
        public event Action? OnFinish;
        public event Action<Exception>? OnError;

        public NeoDialogue(
            DialogueModel data,
            NeoDialogueContext context,
            string? groupId = null)
        {
            this.data = data;
            this.context = context;
            id = data.id;
            this.groupId = groupId;
        }

        public void Start()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(NeoDialogue));
            }
            if (started)
            {
                throw new InvalidOperationException($"Dialogue '{id}' has already started.");
            }
            started = true;
            Finish();
        }

        internal void EmitText(NeoDialogueTextNode node)
        {
            ShowText?.Invoke(node);
        }

        internal void Finish()
        {
            if (disposed) return;
            OnFinish?.Invoke();
            Dispose();
        }

        internal void Fail(Exception exception)
        {
            if (disposed) return;
            if (OnError != null)
            {
                OnError.Invoke(exception);
                Dispose();
                return;
            }
            Dispose();
            throw exception;
        }

        public void Dispose()
        {
            disposed = true;
            ShowText = null;
            OnFinish = null;
            OnError = null;
        }
    }

    public sealed class NeoDialogueTextNode
    {
        private readonly Action next;

        public string id { get; }
        public string text { get; }
        public object? Primary { get; }
        public bool saveChoice { get; }
        public IReadOnlyList<NeoDialogueTextOption> Options { get; }

        public NeoDialogueTextNode(
            string id,
            string text,
            object? primary,
            bool saveChoice,
            IReadOnlyList<NeoDialogueTextOption> options,
            Action next)
        {
            this.id = id;
            this.text = text;
            Primary = primary;
            this.saveChoice = saveChoice;
            Options = options;
            this.next = next;
        }

        public void Next()
        {
            if (Options.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Text node '{id}' has options; select an option instead of calling Next().");
            }
            next();
        }
    }

    public sealed class NeoDialogueTextOption
    {
        private readonly Action select;
        private bool selected;

        public string id { get; }
        public string text { get; }

        public NeoDialogueTextOption(string id, string text, Action select)
        {
            this.id = id;
            this.text = text;
            this.select = select;
        }

        public void Select()
        {
            if (selected)
            {
                throw new InvalidOperationException($"Dialogue option '{id}' has already been selected.");
            }
            selected = true;
            select();
        }
    }
}
