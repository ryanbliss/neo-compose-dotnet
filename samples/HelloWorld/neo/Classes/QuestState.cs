// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoSchemaClass("daf72c99-ad09-47d6-a863-f1ab31acf750")]
public partial class QuestState
{
    [NeoMember("a8a1eee9-5662-4b22-9b55-10ebfc8438c4")]
    public virtual WorldEnding Ending { get; init; } = WorldEnding.none;

    [NeoMember("42487172-af8c-49ff-b88a-2397894542fe")]
    public virtual bool EvidenceArchive { get; init; } = false;

    [NeoMember("35ac06e1-119c-4d1f-81e9-f119610e3865")]
    public virtual bool EvidenceFaith { get; init; } = false;

    [NeoMember("0794a73f-60b2-4ce0-b191-83d0829fe7bb")]
    public virtual bool EvidenceLedger { get; init; } = false;

    [NeoMember("26df3938-e800-4801-b897-602d50db7ec9")]
    [NeoNumber(Min = 0)]
    public virtual int FlareClock { get; init; } = 0;

    // NeoScript: Scripts/QuestState/NextHint.neo
    [NeoMember("3c947ad4-3033-4121-b3b0-3b5177ab30b7")]
    [NeoComputed]
    public virtual string NextHint { get; }

    [NeoMember("cb9bb845-466c-46ac-aee0-345163dcfbf3")]
    [NeoNumber(Min = 0)]
    public virtual int Reruns { get; init; } = 0;

    [NeoMember("3ed7f33a-67fe-4671-8af9-c8339751894b")]
    public virtual QuestStage Stage { get; init; } = QuestStage.arrival;
}
