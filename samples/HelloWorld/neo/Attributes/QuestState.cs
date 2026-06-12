// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("daf72c99-ad09-47d6-a863-f1ab31acf750")]
public sealed class QuestState
{
    [NeoEnum("a8a1eee9-5662-4b22-9b55-10ebfc8438c4", DefaultJson = @"{""value"":[""none""]}")]
    public WorldEnding Ending { get; init; }

    [NeoBool("42487172-af8c-49ff-b88a-2397894542fe", DefaultJson = @"{""value"":false}")]
    public bool EvidenceArchive { get; init; }

    [NeoBool("35ac06e1-119c-4d1f-81e9-f119610e3865", DefaultJson = @"{""value"":false}")]
    public bool EvidenceFaith { get; init; }

    [NeoBool("0794a73f-60b2-4ce0-b191-83d0829fe7bb", DefaultJson = @"{""value"":false}")]
    public bool EvidenceLedger { get; init; }

    [NeoInt("26df3938-e800-4801-b897-602d50db7ec9", Min = 0, DefaultJson = @"{""value"":0}")]
    public int FlareClock { get; init; }

    [NeoGetter("3c947ad4-3033-4121-b3b0-3b5177ab30b7", Code = @"	int found = 0;
	if (this.EvidenceArchive) { found = found + 1; }
	if (this.EvidenceLedger) { found = found + 1; }
	if (this.EvidenceFaith) { found = found + 1; }
	string urgency = """";
	if (this.FlareClock >= 6) { urgency = ""The storms are stacking — hurry. ""; }
	if (this.FlareClock >= 9) { urgency = ""THE SKY IS TEARING. Finish it. ""; }
	if (this.Stage == QuestStage.arrival) {
		return $""{urgency}Rumors ride the flare-wakes. Start with the greeter at Capitol OG."";
	}
	if (this.Stage == QuestStage.followTheWakes) {
		return $""{urgency}Hear the prophecy at Mercurial, and ask the corn farmer on Iowan what the freighters saw."";
	}
	if (this.Stage == QuestStage.threePaths) {
		if (found >= 2) {
			return $""{urgency}Go BACK to Capitol OG — the greeter's script is fraying. Two proofs are enough."";
		}
		if (!this.EvidenceArchive) {
			return $""{urgency}Proof of the frozen sky: revisit Ursa Major's observatory, then offer Storm Corn at Etna Diadem. Outposts you've met have new things to say."";
		}
		if (!this.EvidenceLedger) {
			return $""{urgency}Proof in old money: go back to the Pour Lords, then feed their manifest to the relay at Caelus."";
		}
		return $""{urgency}Proof from the verses: sit with the Patriarch at Mercurial again, then buy the Oldest Pattern from the Venusians."";
	}
	if (this.Stage == QuestStage.vaultOpen) {
		return $""{urgency}Descend beneath Capitol OG. Bring the Abyssal Lantern from Europapas."";
	}
	if (this.Stage == QuestStage.endgame) {
		return $""{urgency}The Old Console at Capitol OG awaits your final output. The Regent's Signet may open one more door."";
	}
	return ""The run has ended. Thank you for playing."";", RetJson = @"{""required"":true,""type"":3}")]
    public object? NextHint { get; init; }

    [NeoInt("cb9bb845-466c-46ac-aee0-345163dcfbf3", Min = 0, DefaultJson = @"{""value"":0}")]
    public int Reruns { get; init; }

    [NeoEnum("3ed7f33a-67fe-4671-8af9-c8339751894b", DefaultJson = @"{""value"":[""arrival""]}")]
    public QuestStage Stage { get; init; }
}
