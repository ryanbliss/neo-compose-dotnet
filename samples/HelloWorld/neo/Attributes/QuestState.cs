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
	if (this.Stage == QuestStage.arrival) {
		return ""Rumors ride the flare-wakes. The Capitol greeter knows more than his script. (Earth)"";
	}
	if (this.Stage == QuestStage.followTheWakes) {
		return ""Follow the wakes sunward-out: vanity shines on Venus; stone remembers on Mars."";
	}
	if (this.Stage == QuestStage.threePaths) {
		if (found >= 2) {
			return ""The Old Console waits beneath the Capitol. Bring light. (Earth)"";
		}
		if (!this.EvidenceArchive) {
			return ""The stars refuse to move — ask Ursa Major (Callisto), then the singing geysers (Enceladus)."";
		}
		if (!this.EvidenceLedger) {
			return ""Money older than light: Pour Lords (Ganymede), Titan, then the sideways relay (Uranus)."";
		}
		return ""Read the Red Nova verses in order — Mercury first, then the cloud courts of Venus."";
	}
	if (this.Stage == QuestStage.vaultOpen) {
		return ""Descend. The first words are still down there. (Earth)"";
	}
	if (this.Stage == QuestStage.endgame) {
		return ""The terminal waits for the world's last output."";
	}
	return ""The run has ended."";", RetJson = @"{""required"":true,""type"":3}")]
    public object? NextHint { get; init; }

    [NeoEnum("3ed7f33a-67fe-4671-8af9-c8339751894b", DefaultJson = @"{""value"":[""arrival""]}")]
    public QuestStage Stage { get; init; }
}
